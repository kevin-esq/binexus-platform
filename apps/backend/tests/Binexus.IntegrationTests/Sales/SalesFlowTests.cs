using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Binexus.IntegrationTests.Infrastructure;
using Binexus.Modules.Identity.Application;
using Binexus.Modules.Identity.Domain;
using Binexus.Modules.Inventory.Domain;
using Binexus.Modules.Sales.Application;
using Binexus.Modules.Sales.Domain;
using Binexus.Platform.Features.Contracts;
using Binexus.Platform.Ids;
using Binexus.Platform.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Binexus.IntegrationTests.Sales;

[Collection("postgres")]
public sealed class SalesFlowTests : IClassFixture<PostgresTestFixture>, IClassFixture<WebApplicationFactory<Program>>
{
    private const string SigningKey = "sales-integration-signing-key-with-more-than-thirty-two-bytes";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly PostgresTestFixture _postgres;
    private readonly HttpClient _client;

    public SalesFlowTests(PostgresTestFixture postgres, WebApplicationFactory<Program> factory)
    {
        _postgres = postgres;
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Testing");
            builder.UseSetting("Database:ConnectionString", postgres.ConnectionString);
            builder.UseSetting("Jwt:Issuer", "binexus");
            builder.UseSetting("Jwt:Audience", "binexus-api");
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.UseSetting("Jwt:AccessTokenLifetime", "00:15:00");
            builder.UseSetting("Jwt:RefreshTokenLifetime", "7.00:00:00");
            builder.UseSetting("Jwt:ClockSkew", "00:00:30");
            builder.UseSetting("IdentitySeed:AdminPassword", IdentitySeedDefaults.KnownInsecureDemoPassword);
            builder.UseSetting("Logistics:Storage:Provider", "Local");
        }).CreateClient();
    }

    [Fact]
    public async Task Feature_enabled_tenant_can_open_session_while_disabled_tenant_is_forbidden()
    {
        var acme = await AcmeContextAsync();
        await SetPosRetailAsync(acme.TenantId, true);
        var other = await CreateTenantUserAsync(RoleNames.Admin);
        await SetPosRetailAsync(other.TenantId, false);

        var enabled = await SendAsync(
            HttpMethod.Post,
            "/sales/sessions/open",
            await LoginAsync("acme", "admin@acme.test"),
            new OpenSalesSessionRequest(acme.BranchId, $"t-{Guid.NewGuid():N}"[..20], 1000, "MXN"));
        enabled.StatusCode.Should().Be(HttpStatusCode.OK);

        var disabled = await SendAsync(
            HttpMethod.Post,
            "/sales/sessions/open",
            await LoginAsync(other.Slug, other.Email),
            new OpenSalesSessionRequest(other.BranchId, "POS-1", 0, "MXN"));
        disabled.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ProblemCodeAsync(disabled)).Should().Be("FEATURE_DISABLED");
    }

    [Fact]
    public async Task Open_then_second_open_same_terminal_conflicts()
    {
        var acme = await AcmeContextAsync();
        await SetPosRetailAsync(acme.TenantId, true);
        var token = await LoginAsync("acme", "admin@acme.test");
        var terminal = $"term-{Guid.NewGuid():N}"[..16];

        var first = await SendAsync(HttpMethod.Post, "/sales/sessions/open", token,
            new OpenSalesSessionRequest(acme.BranchId, terminal, 500, "MXN"));
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var opened = await first.Content.ReadFromJsonAsync<OpenSalesSessionResult>(JsonOptions);
        opened!.Session.Status.Should().Be("OPEN");

        var second = await SendAsync(HttpMethod.Post, "/sales/sessions/open", token,
            new OpenSalesSessionRequest(acme.BranchId, terminal, 500, "MXN"));
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(second)).Should().Be("SALES_SESSION_ALREADY_OPEN");
    }

    [Fact]
    public async Task Cash_sale_decrements_inventory_and_emits_events()
    {
        var acme = await AcmeContextAsync();
        await SetPosRetailAsync(acme.TenantId, true);
        var productId = $"sku-sale-{Guid.NewGuid():N}";
        await SeedStockAsync(acme.TenantId, acme.BranchId, productId, 10);
        var token = await LoginAsync("acme", "admin@acme.test");
        var session = await OpenSessionAsync(token, acme.BranchId);

        var sale = await SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/sales", token,
            new CreateSaleRequest(
                [new CreateSaleLineRequest(productId, "Water", 2, 1500)],
                "MXN",
                [new CreateSalePaymentRequest("CASH", 3000)]));
        sale.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await sale.Content.ReadFromJsonAsync<CreateSaleResult>(JsonOptions);
        body!.Ticket.TotalCents.Should().Be(3000);
        body.Ticket.PaymentCaptures.Should().HaveCount(1);
        body.Ticket.CustomerLabel.Should().Be(SalesConstants.WalkInCustomerLabel);

        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var item = await db.Set<StockItem>().IgnoreQueryFilters()
            .SingleAsync(x => x.TenantId == acme.TenantId && x.ProductId == productId);
        item.OnHand.Should().Be(8);
        (await db.OutboxMessages.CountAsync(x => x.TenantId == acme.TenantId && x.EventName == "SALE_CREATED")).Should().BeGreaterThan(0);
        (await db.OutboxMessages.CountAsync(x => x.TenantId == acme.TenantId && x.EventName == "PAYMENT_REGISTERED")).Should().BeGreaterThan(0);
        (await db.OutboxMessages.CountAsync(x => x.TenantId == acme.TenantId && x.EventName == "STOCK_SOLD")).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Split_payments_cash_card_and_cash_transfer_are_accepted()
    {
        var acme = await AcmeContextAsync();
        await SetPosRetailAsync(acme.TenantId, true);
        var productId = $"sku-split-{Guid.NewGuid():N}";
        await SeedStockAsync(acme.TenantId, acme.BranchId, productId, 5);
        var token = await LoginAsync("acme", "admin@acme.test");
        var session = await OpenSessionAsync(token, acme.BranchId, openingFloat: 1000);

        var card = await SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/sales", token,
            new CreateSaleRequest(
                [new CreateSaleLineRequest(productId, "Item", 1, 1000)],
                "MXN",
                [new CreateSalePaymentRequest("CASH", 400), new CreateSalePaymentRequest("CARD", 600)]));
        card.StatusCode.Should().Be(HttpStatusCode.OK);

        var transfer = await SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/sales", token,
            new CreateSaleRequest(
                [new CreateSaleLineRequest(productId, "Item", 1, 1000)],
                "MXN",
                [new CreateSalePaymentRequest("CASH", 250), new CreateSalePaymentRequest("TRANSFER", 750)]));
        transfer.StatusCode.Should().Be(HttpStatusCode.OK);

        var close = await SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/close", token,
            new CloseSalesSessionRequest(1650, null, null));
        close.StatusCode.Should().Be(HttpStatusCode.OK);
        var closed = await close.Content.ReadFromJsonAsync<CloseSalesSessionResult>(JsonOptions);
        closed!.Session.ExpectedClosingCents.Should().Be(1650);
        closed.Session.DiscrepancyCents.Should().Be(0);
    }

    [Fact]
    public async Task Payment_sum_mismatch_invalid_method_and_zero_are_rejected()
    {
        var acme = await AcmeContextAsync();
        await SetPosRetailAsync(acme.TenantId, true);
        var productId = $"sku-pay-{Guid.NewGuid():N}";
        await SeedStockAsync(acme.TenantId, acme.BranchId, productId, 5);
        var token = await LoginAsync("acme", "admin@acme.test");
        var session = await OpenSessionAsync(token, acme.BranchId);

        var mismatch = await SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/sales", token,
            new CreateSaleRequest(
                [new CreateSaleLineRequest(productId, "Item", 1, 1000)],
                "MXN",
                [new CreateSalePaymentRequest("CASH", 900)]));
        mismatch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(mismatch)).Should().Be("PAYMENT_SUM_MISMATCH");

        var credit = await SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/sales", token,
            new CreateSaleRequest(
                [new CreateSaleLineRequest(productId, "Item", 1, 1000)],
                "MXN",
                [new CreateSalePaymentRequest("CREDIT", 1000)]));
        credit.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(credit)).Should().Be("CREDIT_NOT_SUPPORTED");

        var zero = await SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/sales", token,
            new CreateSaleRequest(
                [new CreateSaleLineRequest(productId, "Item", 1, 1000)],
                "MXN",
                [new CreateSalePaymentRequest("CASH", 0)]));
        zero.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Close_match_as_cashier_ok_discrepancy_forbidden_admin_with_reason_ok()
    {
        var acme = await AcmeContextAsync();
        await SetPosRetailAsync(acme.TenantId, true);
        var cashier = await CreateTenantUserAsync(RoleNames.Cashier, acme.TenantId, acme.BranchId);
        var adminToken = await LoginAsync("acme", "admin@acme.test");
        var cashierToken = await LoginAsync("acme", cashier.Email);

        var matchSession = await OpenSessionAsync(adminToken, acme.BranchId, openingFloat: 200);
        var match = await SendAsync(HttpMethod.Post, $"/sales/sessions/{matchSession.Id}/close", cashierToken,
            new CloseSalesSessionRequest(200, null, null));
        match.StatusCode.Should().Be(HttpStatusCode.OK);

        var discSession = await OpenSessionAsync(adminToken, acme.BranchId, openingFloat: 200);
        var cashierDenied = await SendAsync(HttpMethod.Post, $"/sales/sessions/{discSession.Id}/close", cashierToken,
            new CloseSalesSessionRequest(150, null, "short"));
        cashierDenied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ProblemCodeAsync(cashierDenied)).Should().Be("DISCREPANCY_CLOSE_FORBIDDEN");

        var adminOk = await SendAsync(HttpMethod.Post, $"/sales/sessions/{discSession.Id}/close", adminToken,
            new CloseSalesSessionRequest(150, "counted", "till short"));
        adminOk.StatusCode.Should().Be(HttpStatusCode.OK);
        var closed = await adminOk.Content.ReadFromJsonAsync<CloseSalesSessionResult>(JsonOptions);
        closed!.Session.DiscrepancyCents.Should().Be(-50);
        closed.Session.DiscrepancyReason.Should().Be("till short");
    }

    [Fact]
    public async Task Closed_session_rejects_create_sale_and_second_close()
    {
        var acme = await AcmeContextAsync();
        await SetPosRetailAsync(acme.TenantId, true);
        var productId = $"sku-closed-{Guid.NewGuid():N}";
        await SeedStockAsync(acme.TenantId, acme.BranchId, productId, 2);
        var token = await LoginAsync("acme", "admin@acme.test");
        var session = await OpenSessionAsync(token, acme.BranchId, openingFloat: 0);

        (await SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/close", token, new CloseSalesSessionRequest(0, null, null)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var sale = await SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/sales", token,
            new CreateSaleRequest(
                [new CreateSaleLineRequest(productId, "Item", 1, 500)],
                "MXN",
                [new CreateSalePaymentRequest("CASH", 500)]));
        sale.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(sale)).Should().Be("SALES_SESSION_NOT_OPEN");

        var closeAgain = await SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/close", token,
            new CloseSalesSessionRequest(0, null, null));
        closeAgain.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(closeAgain)).Should().Be("SALES_SESSION_ALREADY_CLOSED");
    }

    [Fact]
    public async Task Retry_sale_with_same_idempotency_key_replays()
    {
        var acme = await AcmeContextAsync();
        await SetPosRetailAsync(acme.TenantId, true);
        var productId = $"sku-idem-{Guid.NewGuid():N}";
        await SeedStockAsync(acme.TenantId, acme.BranchId, productId, 5);
        var token = await LoginAsync("acme", "admin@acme.test");
        var session = await OpenSessionAsync(token, acme.BranchId);
        var key = Guid.NewGuid().ToString("N");
        var body = new CreateSaleRequest(
            [new CreateSaleLineRequest(productId, "Item", 1, 800)],
            "MXN",
            [new CreateSalePaymentRequest("CASH", 800)]);

        var first = await SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/sales", token, body, key);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstTicket = (await first.Content.ReadFromJsonAsync<CreateSaleResult>(JsonOptions))!.Ticket;

        var second = await SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/sales", token, body, key);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondTicket = (await second.Content.ReadFromJsonAsync<CreateSaleResult>(JsonOptions))!.Ticket;
        secondTicket.Id.Should().Be(firstTicket.Id);

        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var item = await db.Set<StockItem>().IgnoreQueryFilters()
            .SingleAsync(x => x.TenantId == acme.TenantId && x.ProductId == productId);
        item.OnHand.Should().Be(4);
        (await db.Set<Sale>().IgnoreQueryFilters().CountAsync(x => x.TenantId == acme.TenantId && x.SessionId == session.Id))
            .Should().Be(1);
    }

    private async Task<SalesSessionSummary> OpenSessionAsync(string token, Guid branchId, int openingFloat = 0)
    {
        var response = await SendAsync(HttpMethod.Post, "/sales/sessions/open", token,
            new OpenSalesSessionRequest(branchId, $"t-{Guid.NewGuid():N}"[..18], openingFloat, "MXN"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OpenSalesSessionResult>(JsonOptions))!.Session;
    }

    private async Task SetPosRetailAsync(Guid tenantId, bool enabled)
    {
        using var scope = _postgres.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITenantFeatureService>()
            .SetEnabledAsync(tenantId, FeatureKey.PosRetail, enabled);
    }

    private async Task<(Guid TenantId, Guid BranchId)> AcmeContextAsync()
    {
        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var tenant = await db.Set<Tenant>().IgnoreQueryFilters().SingleAsync(x => x.Slug == "acme");
        var branch = await db.Set<Branch>().IgnoreQueryFilters().SingleAsync(x => x.TenantId == tenant.Id && x.Name == "Main");
        return (tenant.Id, branch.Id);
    }

    private async Task<(Guid TenantId, Guid BranchId, string Slug, string Email)> CreateTenantUserAsync(
        string role,
        Guid? existingTenantId = null,
        Guid? existingBranchId = null)
    {
        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        Guid tenantId;
        Guid branchId;
        string slug;
        if (existingTenantId is null)
        {
            slug = $"sales-{Guid.NewGuid():N}";
            var tenant = new Tenant(ids.NewId(), slug, slug, DateTimeOffset.UtcNow);
            var branch = new Branch(ids.NewId(), tenant.Id, "Main");
            db.AddRange(tenant, branch);
            tenantId = tenant.Id;
            branchId = branch.Id;
        }
        else
        {
            tenantId = existingTenantId.Value;
            branchId = existingBranchId ?? throw new InvalidOperationException("branch required");
            slug = "acme";
        }

        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@sales.test";
        db.Add(new User(ids.NewId(), tenantId, email, EmailNormalizer.Normalize(email),
            await hasher.HashAsync(IdentitySeedDefaults.KnownInsecureDemoPassword), role, branchId));
        await db.SaveChangesAsync();
        return (tenantId, branchId, slug, email);
    }

    private async Task SeedStockAsync(Guid tenantId, Guid branchId, string productId, int onHand)
    {
        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        db.Add(new StockItem(ids.NewId(), tenantId, branchId, productId, onHand, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    private async Task<string> LoginAsync(string slug, string email)
    {
        var response = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(slug, email, IdentitySeedDefaults.KnownInsecureDemoPassword));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthTokens>(JsonOptions))!.AccessToken;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string url,
        string token,
        object? body = null,
        string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString("N"));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request);
    }

    private static async Task<string> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("code").GetString()
            ?? doc.RootElement.GetProperty("title").GetString()
            ?? string.Empty;
    }
}
