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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Binexus.IntegrationTests.Sales;

[Collection("postgres")]
public sealed class SalesConcurrencyTests : IClassFixture<PostgresTestFixture>, IClassFixture<CloudApiFactory>
{
    private const string SigningKey = "sales-concurrency-signing-key-with-more-than-thirty-two-bytes";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly PostgresTestFixture _postgres;
    private readonly HttpClient _client;

    public SalesConcurrencyTests(PostgresTestFixture postgres, CloudApiFactory factory)
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
    public async Task Concurrent_last_stock_unit_has_single_winner()
    {
        var acme = await AcmeContextAsync();
        await SetPosRetailAsync(acme.TenantId, true);
        var productId = $"sku-race-{Guid.NewGuid():N}";
        await SeedStockAsync(acme.TenantId, acme.BranchId, productId, 1);
        var token = await LoginAsync();
        var session = await OpenSessionAsync(token, acme.BranchId);
        var body = new CreateSaleRequest(
            [new CreateSaleLineRequest(productId, "Last", 1, 100)],
            "MXN",
            [new CreateSalePaymentRequest("CASH", 100)]);

        var tasks = Enumerable.Range(0, 2).Select(_ =>
            SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/sales", token, body)).ToArray();
        var results = await Task.WhenAll(tasks);

        results.Count(x => x.StatusCode == HttpStatusCode.OK).Should().Be(1);
        results.Count(x => x.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict).Should().Be(1);

        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var item = await db.Set<StockItem>().IgnoreQueryFilters()
            .SingleAsync(x => x.TenantId == acme.TenantId && x.ProductId == productId);
        item.OnHand.Should().Be(0);
        (await db.Set<Sale>().IgnoreQueryFilters().CountAsync(x => x.TenantId == acme.TenantId && x.SessionId == session.Id))
            .Should().Be(1);
    }

    [Fact]
    public async Task Two_open_same_terminal_concurrent_has_single_winner()
    {
        var acme = await AcmeContextAsync();
        await SetPosRetailAsync(acme.TenantId, true);
        var token = await LoginAsync();
        var terminal = $"race-{Guid.NewGuid():N}"[..16];
        var body = new OpenSalesSessionRequest(acme.BranchId, terminal, 0, "MXN");

        var results = await Task.WhenAll(
            SendAsync(HttpMethod.Post, "/sales/sessions/open", token, body),
            SendAsync(HttpMethod.Post, "/sales/sessions/open", token, body));

        results.Count(x => x.StatusCode == HttpStatusCode.OK).Should().Be(1);
        results.Count(x => x.StatusCode == HttpStatusCode.Conflict).Should().Be(1);

        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        (await db.Set<SalesSession>().IgnoreQueryFilters().CountAsync(x =>
                x.TenantId == acme.TenantId && x.TerminalId == terminal && x.Status == SalesSessionStatus.Open))
            .Should().Be(1);
    }

    [Fact]
    public async Task Sale_vs_close_race_does_not_leave_sale_on_closed_without_stock_integrity()
    {
        var acme = await AcmeContextAsync();
        await SetPosRetailAsync(acme.TenantId, true);
        var productId = $"sku-close-race-{Guid.NewGuid():N}";
        await SeedStockAsync(acme.TenantId, acme.BranchId, productId, 3);
        var token = await LoginAsync();
        var session = await OpenSessionAsync(token, acme.BranchId, openingFloat: 0);

        var saleTask = SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/sales", token,
            new CreateSaleRequest(
                [new CreateSaleLineRequest(productId, "Item", 1, 500)],
                "MXN",
                [new CreateSalePaymentRequest("CASH", 500)]));
        // Admin + reason so close can finish even if the sale commits first (expected cash becomes 500).
        var closeTask = SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/close", token,
            new CloseSalesSessionRequest(0, null, "race"));
        var results = await Task.WhenAll(saleTask, closeTask);

        results.Should().Contain(x => x.StatusCode == HttpStatusCode.OK);
        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var closed = await db.Set<SalesSession>().IgnoreQueryFilters()
            .SingleAsync(x => x.Id == session.Id);
        closed.Status.Should().Be(SalesSessionStatus.Closed);

        var saleCount = await db.Set<Sale>().IgnoreQueryFilters().CountAsync(x => x.SessionId == session.Id);
        closed.ExpectedClosingCents.Should().Be(saleCount == 1 ? 500 : 0);
    }

    [Fact]
    public async Task Two_close_concurrent_has_single_winner()
    {
        var acme = await AcmeContextAsync();
        await SetPosRetailAsync(acme.TenantId, true);
        var token = await LoginAsync();
        var session = await OpenSessionAsync(token, acme.BranchId, openingFloat: 100);
        var body = new CloseSalesSessionRequest(100, null, null);

        var results = await Task.WhenAll(
            SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/close", token, body),
            SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/close", token, body));

        results.Count(x => x.StatusCode == HttpStatusCode.OK).Should().Be(1);
        results.Count(x => x.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest).Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_distinct_sales_same_session_both_succeed()
    {
        var acme = await AcmeContextAsync();
        await SetPosRetailAsync(acme.TenantId, true);
        var productId = $"sku-both-{Guid.NewGuid():N}";
        await SeedStockAsync(acme.TenantId, acme.BranchId, productId, 10);
        var token = await LoginAsync();
        var session = await OpenSessionAsync(token, acme.BranchId);

        var saleA = new CreateSaleRequest(
            [new CreateSaleLineRequest(productId, "A", 1, 100)],
            "MXN",
            [new CreateSalePaymentRequest("CASH", 100)]);
        var saleB = new CreateSaleRequest(
            [new CreateSaleLineRequest(productId, "B", 1, 200)],
            "MXN",
            [new CreateSalePaymentRequest("CARD", 200)]);

        var results = await Task.WhenAll(
            SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/sales", token, saleA),
            SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/sales", token, saleB));

        results.Should().OnlyContain(x => x.StatusCode == HttpStatusCode.OK);

        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        (await db.Set<Sale>().IgnoreQueryFilters().CountAsync(x => x.SessionId == session.Id)).Should().Be(2);
        var item = await db.Set<StockItem>().IgnoreQueryFilters()
            .SingleAsync(x => x.TenantId == acme.TenantId && x.ProductId == productId);
        item.OnHand.Should().Be(8);
    }

    [Fact]
    public async Task Sale_then_close_includes_cash_in_expected_snapshot()
    {
        var acme = await AcmeContextAsync();
        await SetPosRetailAsync(acme.TenantId, true);
        var productId = $"sku-order-{Guid.NewGuid():N}";
        await SeedStockAsync(acme.TenantId, acme.BranchId, productId, 3);
        var token = await LoginAsync();
        var session = await OpenSessionAsync(token, acme.BranchId, openingFloat: 100);

        (await SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/sales", token,
            new CreateSaleRequest(
                [new CreateSaleLineRequest(productId, "Item", 1, 400)],
                "MXN",
                [new CreateSalePaymentRequest("CASH", 400)])))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var close = await SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/close", token,
            new CloseSalesSessionRequest(500, null, null));
        close.StatusCode.Should().Be(HttpStatusCode.OK);
        var closed = await close.Content.ReadFromJsonAsync<CloseSalesSessionResult>(JsonOptions);
        closed!.Session.ExpectedClosingCents.Should().Be(500);
    }

    [Fact]
    public async Task Close_then_sale_rejected_without_orphaned_cash()
    {
        var acme = await AcmeContextAsync();
        await SetPosRetailAsync(acme.TenantId, true);
        var productId = $"sku-rev-{Guid.NewGuid():N}";
        await SeedStockAsync(acme.TenantId, acme.BranchId, productId, 3);
        var token = await LoginAsync();
        var session = await OpenSessionAsync(token, acme.BranchId, openingFloat: 50);

        (await SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/close", token,
            new CloseSalesSessionRequest(50, null, null)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var sale = await SendAsync(HttpMethod.Post, $"/sales/sessions/{session.Id}/sales", token,
            new CreateSaleRequest(
                [new CreateSaleLineRequest(productId, "Item", 1, 100)],
                "MXN",
                [new CreateSalePaymentRequest("CASH", 100)]));
        sale.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemCodeAsync(sale)).Should().Be("SALES_SESSION_NOT_OPEN");

        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var closed = await db.Set<SalesSession>().IgnoreQueryFilters().SingleAsync(x => x.Id == session.Id);
        closed.ExpectedClosingCents.Should().Be(50);
        (await db.Set<Sale>().IgnoreQueryFilters().CountAsync(x => x.SessionId == session.Id)).Should().Be(0);
    }

    private static async Task<string> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("code").GetString()
            ?? doc.RootElement.GetProperty("title").GetString()
            ?? string.Empty;
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

    private async Task SeedStockAsync(Guid tenantId, Guid branchId, string productId, int onHand)
    {
        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        db.Add(new StockItem(ids.NewId(), tenantId, branchId, productId, onHand, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    private async Task<SalesSessionSummary> OpenSessionAsync(string token, Guid branchId, int openingFloat = 0)
    {
        var response = await SendAsync(HttpMethod.Post, "/sales/sessions/open", token,
            new OpenSalesSessionRequest(branchId, $"c-{Guid.NewGuid():N}"[..18], openingFloat, "MXN"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OpenSalesSessionResult>(JsonOptions))!.Session;
    }

    private async Task<string> LoginAsync()
    {
        var response = await _client.PostAsJsonAsync("/auth/login",
            new LoginRequest("acme", "admin@acme.test", IdentitySeedDefaults.KnownInsecureDemoPassword));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthTokens>(JsonOptions))!.AccessToken;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request);
    }
}
