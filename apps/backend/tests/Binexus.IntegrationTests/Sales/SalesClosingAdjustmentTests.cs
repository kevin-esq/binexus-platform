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
using Npgsql;

namespace Binexus.IntegrationTests.Sales;

[Collection("postgres")]
public sealed class SalesClosingAdjustmentTests : IClassFixture<PostgresTestFixture>, IClassFixture<WebApplicationFactory<Program>>
{
    private const string SigningKey = "sales-closing-signing-key-with-more-than-thirty-two-bytes";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly PostgresTestFixture _postgres;
    private readonly HttpClient _client;

    public SalesClosingAdjustmentTests(PostgresTestFixture postgres, WebApplicationFactory<Program> factory)
    {
        _postgres = postgres;
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Testing");
            builder.UseSetting("Database:ConnectionString", postgres.ConnectionString);
            builder.UseSetting("Jwt:Issuer", "binexus");
            builder.UseSetting("Jwt:Audience", "binexus-api");
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.UseSetting("Jwt:AccessTokenDuration", "00:15:00");
            builder.UseSetting("Jwt:RefreshTokenDuration", "7.00:00:00");
            builder.UseSetting("Jwt:ClockSkew", "00:00:30");
            builder.UseSetting("IdentitySeed:AdminPassword", IdentitySeedDefaults.KnownInsecureDemoPassword);
            builder.UseSetting("Logistics:Storage:Provider", "Local");
        }).CreateClient();
    }

    [Fact]
    public async Task Expected_cash_overflow_is_rejected_on_close()
    {
        var acme = await AcmeContextAsync();
        await SetPosRetailAsync(acme.TenantId, true);
        var productId = $"sku-ovf-{Guid.NewGuid():N}";
        await SeedStockAsync(acme.TenantId, acme.BranchId, productId, 2);
        var token = await LoginAsync("acme", "admin@acme.test");
        var session = await OpenSessionAsync(token, acme.BranchId, openingFloat: int.MaxValue);

        (await SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/sales", token,
            new CreateSaleRequest(
                [new CreateSaleLineRequest(productId, "Item", 1, 1)],
                "MXN",
                [new CreateSalePaymentRequest("CASH", 1)])))
            .EnsureSuccessStatusCode();

        var close = await SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/close", token,
            new CloseSalesSessionRequest(int.MaxValue, null, "overflow"));
        close.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(close)).Should().Be("INVALID_CLOSE");
    }

    [Fact]
    public async Task Close_snapshot_immutable_on_idempotent_retry_and_second_close()
    {
        var acme = await AcmeContextAsync();
        await SetPosRetailAsync(acme.TenantId, true);
        var productId = $"sku-snap-{Guid.NewGuid():N}";
        await SeedStockAsync(acme.TenantId, acme.BranchId, productId, 5);
        var token = await LoginAsync("acme", "admin@acme.test");
        var session = await OpenSessionAsync(token, acme.BranchId, openingFloat: 1000);

        (await SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/sales", token,
            new CreateSaleRequest(
                [new CreateSaleLineRequest(productId, "Item", 1, 500)],
                "MXN",
                [new CreateSalePaymentRequest("CASH", 500)])))
            .EnsureSuccessStatusCode();

        var closeKey = Guid.NewGuid().ToString("N");
        var closeBody = new CloseSalesSessionRequest(1500, null, null);
        var first = await SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/close", token, closeBody, closeKey);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await first.Content.ReadFromJsonAsync<CloseSalesSessionResult>(JsonOptions);
        firstBody!.Session.ExpectedClosingCents.Should().Be(1500);

        var replay = await SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/close", token, closeBody, closeKey);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayBody = await replay.Content.ReadFromJsonAsync<CloseSalesSessionResult>(JsonOptions);
        replayBody!.Session.ExpectedClosingCents.Should().Be(1500);
        replayBody.Session.DeclaredClosingCents.Should().Be(1500);

        var second = await SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/close", token,
            new CloseSalesSessionRequest(9999, null, null));
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(second)).Should().Be("SALES_SESSION_ALREADY_CLOSED");

        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var frozen = await db.Set<SalesSession>().IgnoreQueryFilters().SingleAsync(x => x.Id == session.Id);
        frozen.ExpectedClosingCents.Should().Be(1500);
        frozen.DeclaredClosingCents.Should().Be(1500);
    }

    [Fact]
    public async Task Arqueo_uses_only_this_session_cash_captures()
    {
        var acme = await AcmeContextAsync();
        await SetPosRetailAsync(acme.TenantId, true);
        var productId = $"sku-arqueo-{Guid.NewGuid():N}";
        await SeedStockAsync(acme.TenantId, acme.BranchId, productId, 10);
        var token = await LoginAsync("acme", "admin@acme.test");
        var sessionA = await OpenSessionAsync(token, acme.BranchId, openingFloat: 100, terminal: $"a-{Guid.NewGuid():N}"[..16]);
        var sessionB = await OpenSessionAsync(token, acme.BranchId, openingFloat: 0, terminal: $"b-{Guid.NewGuid():N}"[..16]);

        (await SendAsync(HttpMethod.Post, $"/sales/sessions/{sessionA.Id}/sales", token,
            new CreateSaleRequest(
                [new CreateSaleLineRequest(productId, "A", 1, 300)],
                "MXN",
                [new CreateSalePaymentRequest("CASH", 300)])))
            .EnsureSuccessStatusCode();
        (await SendAsync(HttpMethod.Post, $"/sales/sessions/{sessionB.Id}/sales", token,
            new CreateSaleRequest(
                [new CreateSaleLineRequest(productId, "B", 1, 900)],
                "MXN",
                [new CreateSalePaymentRequest("CASH", 900)])))
            .EnsureSuccessStatusCode();

        var closeA = await SendAsync(HttpMethod.Post, $"/sales/sessions/{sessionA.Id}/close", token,
            new CloseSalesSessionRequest(400, null, null));
        closeA.StatusCode.Should().Be(HttpStatusCode.OK);
        (await closeA.Content.ReadFromJsonAsync<CloseSalesSessionResult>(JsonOptions))!
            .Session.ExpectedClosingCents.Should().Be(400);
    }

    [Fact]
    public async Task Non_cash_sale_does_not_change_expected_cash_split_cash_portion_only()
    {
        var acme = await AcmeContextAsync();
        await SetPosRetailAsync(acme.TenantId, true);
        var productId = $"sku-cash-{Guid.NewGuid():N}";
        await SeedStockAsync(acme.TenantId, acme.BranchId, productId, 10);
        var token = await LoginAsync("acme", "admin@acme.test");
        var session = await OpenSessionAsync(token, acme.BranchId, openingFloat: 200);

        (await SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/sales", token,
            new CreateSaleRequest(
                [new CreateSaleLineRequest(productId, "Card", 1, 1000)],
                "MXN",
                [new CreateSalePaymentRequest("CARD", 1000)])))
            .EnsureSuccessStatusCode();

        (await SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/sales", token,
            new CreateSaleRequest(
                [new CreateSaleLineRequest(productId, "Split", 1, 1000)],
                "MXN",
                [new CreateSalePaymentRequest("CASH", 250), new CreateSalePaymentRequest("TRANSFER", 750)])))
            .EnsureSuccessStatusCode();

        var close = await SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/close", token,
            new CloseSalesSessionRequest(450, null, null));
        close.StatusCode.Should().Be(HttpStatusCode.OK);
        (await close.Content.ReadFromJsonAsync<CloseSalesSessionResult>(JsonOptions))!
            .Session.ExpectedClosingCents.Should().Be(450);
    }

    [Fact]
    public async Task Negative_discrepancy_exact_match_and_client_declared_not_authoritative()
    {
        var acme = await AcmeContextAsync();
        await SetPosRetailAsync(acme.TenantId, true);
        var token = await LoginAsync("acme", "admin@acme.test");
        var exact = await OpenSessionAsync(token, acme.BranchId, openingFloat: 300);
        var exactClose = await SendAsync(HttpMethod.Post, $"/sales/sessions/{exact.Id}/close", token,
            new CloseSalesSessionRequest(300, null, null));
        exactClose.StatusCode.Should().Be(HttpStatusCode.OK);
        (await exactClose.Content.ReadFromJsonAsync<CloseSalesSessionResult>(JsonOptions))!
            .Session.DiscrepancyCents.Should().Be(0);

        var shortSession = await OpenSessionAsync(token, acme.BranchId, openingFloat: 300);
        var shortClose = await SendAsync(HttpMethod.Post, $"/sales/sessions/{shortSession.Id}/close", token,
            new CloseSalesSessionRequest(250, "notes", "short till"));
        shortClose.StatusCode.Should().Be(HttpStatusCode.OK);
        var shortBody = await shortClose.Content.ReadFromJsonAsync<CloseSalesSessionResult>(JsonOptions);
        shortBody!.Session.ExpectedClosingCents.Should().Be(300);
        shortBody.Session.DeclaredClosingCents.Should().Be(250);
        shortBody.Session.DiscrepancyCents.Should().Be(-50);
    }

    [Fact]
    public async Task Partial_unique_open_allows_other_branch_and_other_tenant()
    {
        var acme = await AcmeContextAsync();
        await SetPosRetailAsync(acme.TenantId, true);
        var other = await CreateTenantWithBranchAsync();
        await SetPosRetailAsync(other.TenantId, true);

        Guid secondBranchId;
        using (var scope = _postgres.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
            var secondBranch = new Branch(ids.NewId(), acme.TenantId, $"Alt-{Guid.NewGuid():N}"[..12]);
            db.Add(secondBranch);
            await db.SaveChangesAsync();
            secondBranchId = secondBranch.Id;
        }

        var terminal = $"shared-{Guid.NewGuid():N}"[..16];
        var acmeToken = await LoginAsync("acme", "admin@acme.test");
        var otherToken = await LoginAsync(other.Slug, other.Email);

        (await SendAsync(HttpMethod.Post, "/sales/sessions/open", acmeToken,
            new OpenSalesSessionRequest(acme.BranchId, terminal, 0, "MXN")))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await SendAsync(HttpMethod.Post, "/sales/sessions/open", acmeToken,
            new OpenSalesSessionRequest(secondBranchId, terminal, 0, "MXN")))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await SendAsync(HttpMethod.Post, "/sales/sessions/open", otherToken,
            new OpenSalesSessionRequest(other.BranchId, terminal, 0, "MXN")))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await SendAsync(HttpMethod.Post, "/sales/sessions/open", acmeToken,
            new OpenSalesSessionRequest(acme.BranchId, terminal, 0, "MXN")))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Idempotent_sale_replay_same_ids_no_double_inventory_different_payload_conflicts()
    {
        var acme = await AcmeContextAsync();
        await SetPosRetailAsync(acme.TenantId, true);
        var productId = $"sku-idem2-{Guid.NewGuid():N}";
        await SeedStockAsync(acme.TenantId, acme.BranchId, productId, 5);
        var token = await LoginAsync("acme", "admin@acme.test");
        var session = await OpenSessionAsync(token, acme.BranchId);
        var key = Guid.NewGuid().ToString("N");
        var body = new CreateSaleRequest(
            [new CreateSaleLineRequest(productId, "Item", 1, 800)],
            "MXN",
            [new CreateSalePaymentRequest("CASH", 800)]);

        var first = await SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/sales", token, body, key);
        first.EnsureSuccessStatusCode();
        var firstTicket = (await first.Content.ReadFromJsonAsync<CreateSaleResult>(JsonOptions))!.Ticket;

        var second = await SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/sales", token, body, key);
        second.EnsureSuccessStatusCode();
        var secondTicket = (await second.Content.ReadFromJsonAsync<CreateSaleResult>(JsonOptions))!.Ticket;
        secondTicket.Id.Should().Be(firstTicket.Id);

        var reused = await SendAsync(
            HttpMethod.Post,
            $"/sales/sessions/{session.Id}/sales",
            token,
            new CreateSaleRequest(
                [new CreateSaleLineRequest(productId, "Item", 2, 800)],
                "MXN",
                [new CreateSalePaymentRequest("CASH", 1600)]),
            key);
        reused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(reused)).Should().Be("IDEMPOTENCY_KEY_REUSED");

        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var item = await db.Set<StockItem>().IgnoreQueryFilters()
            .SingleAsync(x => x.TenantId == acme.TenantId && x.ProductId == productId);
        item.OnHand.Should().Be(4);
        (await db.Set<Sale>().IgnoreQueryFilters().CountAsync(x => x.SessionId == session.Id)).Should().Be(1);

        var saleCreated = await db.OutboxMessages
            .Where(x => x.TenantId == acme.TenantId && x.EventName == "SALE_CREATED")
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstAsync();
        using var doc = JsonDocument.Parse(saleCreated.PayloadJson);
        var saleId = doc.RootElement.GetProperty("saleId").GetGuid();
        var ticketId = doc.RootElement.GetProperty("ticketId").GetGuid();
        saleId.Should().Be(ticketId);
        saleId.Should().Be(firstTicket.Id);

        var payment = await db.OutboxMessages
            .Where(x => x.TenantId == acme.TenantId && x.EventName == "PAYMENT_REGISTERED")
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstAsync();
        using var payDoc = JsonDocument.Parse(payment.PayloadJson);
        payDoc.RootElement.GetProperty("saleId").GetGuid().Should().Be(firstTicket.Id);
        payDoc.RootElement.GetProperty("sessionId").GetGuid().Should().Be(session.Id);
    }

    [Fact]
    public async Task Cashier_feature_disabled_and_wrong_tenant_session_rejected()
    {
        var acme = await AcmeContextAsync();
        await SetPosRetailAsync(acme.TenantId, true);
        var other = await CreateTenantWithBranchAsync();
        await SetPosRetailAsync(other.TenantId, true);

        var cashier = await CreateCashierAsync(acme.TenantId, acme.BranchId);
        var cashierToken = await LoginAsync("acme", cashier);
        var adminToken = await LoginAsync("acme", "admin@acme.test");
        var session = await OpenSessionAsync(adminToken, acme.BranchId);

        await SetPosRetailAsync(acme.TenantId, false);
        var disabled = await SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/sales", cashierToken,
            new CreateSaleRequest(
                [new CreateSaleLineRequest("x", "x", 1, 100)],
                "MXN",
                [new CreateSalePaymentRequest("CASH", 100)]));
        disabled.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ProblemCodeAsync(disabled)).Should().Be("FEATURE_DISABLED");
        await SetPosRetailAsync(acme.TenantId, true);

        var otherToken = await LoginAsync(other.Slug, other.Email);
        var crossTenant = await SendAsync(HttpMethod.Get, $"/sales/sessions/{session.Id}", otherToken);
        crossTenant.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sale_on_session_from_other_branch_rejected_for_branch_scoped_cashier()
    {
        var acme = await AcmeContextAsync();
        await SetPosRetailAsync(acme.TenantId, true);
        Guid secondBranchId;
        using (var scope = _postgres.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
            var second = new Branch(ids.NewId(), acme.TenantId, $"B2-{Guid.NewGuid():N}"[..10]);
            db.Add(second);
            await db.SaveChangesAsync();
            secondBranchId = second.Id;
        }

        var productId = $"sku-br-{Guid.NewGuid():N}";
        await SeedStockAsync(acme.TenantId, acme.BranchId, productId, 5);
        await SeedStockAsync(acme.TenantId, secondBranchId, productId, 5);

        var adminToken = await LoginAsync("acme", "admin@acme.test");
        var sessionOnMain = await OpenSessionAsync(adminToken, acme.BranchId);
        var cashierOtherBranch = await CreateCashierAsync(acme.TenantId, secondBranchId);
        var cashierToken = await LoginAsync("acme", cashierOtherBranch);

        var rejected = await SendAsync(HttpMethod.Post, $"/sales/sessions/{sessionOnMain.Id}/sales", cashierToken,
            new CreateSaleRequest(
                [new CreateSaleLineRequest(productId, "Item", 1, 100)],
                "MXN",
                [new CreateSalePaymentRequest("CASH", 100)]));
        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(rejected)).Should().Be("INVALID_BRANCH");
    }

    [Fact]
    public async Task Payment_capture_wrong_session_violates_composite_fk()
    {
        var acme = await AcmeContextAsync();
        await SetPosRetailAsync(acme.TenantId, true);
        var productId = $"sku-fk-{Guid.NewGuid():N}";
        await SeedStockAsync(acme.TenantId, acme.BranchId, productId, 2);
        var token = await LoginAsync("acme", "admin@acme.test");
        var session = await OpenSessionAsync(token, acme.BranchId);
        var otherSession = await OpenSessionAsync(token, acme.BranchId, terminal: $"o-{Guid.NewGuid():N}"[..16]);

        var sale = await SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/sales", token,
            new CreateSaleRequest(
                [new CreateSaleLineRequest(productId, "Item", 1, 100)],
                "MXN",
                [new CreateSalePaymentRequest("CASH", 100)]));
        sale.EnsureSuccessStatusCode();
        var ticket = (await sale.Content.ReadFromJsonAsync<CreateSaleResult>(JsonOptions))!.Ticket;

        await using var connection = new NpgsqlConnection(_postgres.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO payment_captures (id, tenant_id, sale_id, session_id, method, amount_cents, currency, captured_at_utc)
            VALUES (@id, @tenant, @sale, @session, 'CASH', 50, 'MXN', NOW())
            """,
            connection);
        cmd.Parameters.AddWithValue("id", Guid.CreateVersion7());
        cmd.Parameters.AddWithValue("tenant", acme.TenantId);
        cmd.Parameters.AddWithValue("sale", ticket.Id);
        cmd.Parameters.AddWithValue("session", otherSession.Id);

        var act = async () => await cmd.ExecuteNonQueryAsync();
        await act.Should().ThrowAsync<PostgresException>();
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

    private async Task<(Guid TenantId, Guid BranchId, string Slug, string Email)> CreateTenantWithBranchAsync()
    {
        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var slug = $"sales-c-{Guid.NewGuid():N}";
        var tenant = new Tenant(ids.NewId(), slug, slug, DateTimeOffset.UtcNow);
        var branch = new Branch(ids.NewId(), tenant.Id, "Main");
        var email = $"admin-{Guid.NewGuid():N}@sales.test";
        db.AddRange(tenant, branch);
        db.Add(new User(ids.NewId(), tenant.Id, email, EmailNormalizer.Normalize(email),
            await hasher.HashAsync(IdentitySeedDefaults.KnownInsecureDemoPassword), RoleNames.Admin, branch.Id));
        await db.SaveChangesAsync();
        return (tenant.Id, branch.Id, slug, email);
    }

    private async Task<string> CreateCashierAsync(Guid tenantId, Guid branchId)
    {
        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var email = $"cashier-{Guid.NewGuid():N}@sales.test";
        db.Add(new User(ids.NewId(), tenantId, email, EmailNormalizer.Normalize(email),
            await hasher.HashAsync(IdentitySeedDefaults.KnownInsecureDemoPassword), RoleNames.Cashier, branchId));
        await db.SaveChangesAsync();
        return email;
    }

    private async Task SeedStockAsync(Guid tenantId, Guid branchId, string productId, int onHand)
    {
        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        db.Add(new StockItem(ids.NewId(), tenantId, branchId, productId, onHand, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    private async Task<SalesSessionSummary> OpenSessionAsync(
        string token,
        Guid branchId,
        int openingFloat = 0,
        string? terminal = null)
    {
        var response = await SendAsync(HttpMethod.Post, "/sales/sessions/open", token,
            new OpenSalesSessionRequest(branchId, terminal ?? $"t-{Guid.NewGuid():N}"[..18], openingFloat, "MXN"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OpenSalesSessionResult>(JsonOptions))!.Session;
    }

    private async Task<string> LoginAsync(string slug, string email)
    {
        var response = await _client.PostAsJsonAsync("/auth/login",
            new LoginRequest(slug, email, IdentitySeedDefaults.KnownInsecureDemoPassword));
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
