using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Binexus.IntegrationTests.Infrastructure;
using Binexus.Modules.Identity.Application;
using Binexus.Modules.Identity.Domain;
using Binexus.Modules.Inventory.Domain;
using Binexus.Platform.Ids;
using Binexus.Platform.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Binexus.IntegrationTests.Orders;

public sealed class OrderFlowTests : IClassFixture<PostgresTestFixture>, IClassFixture<WebApplicationFactory<Program>>
{
    private const string SigningKey = "identity-integration-signing-key-with-more-than-thirty-two-bytes";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly PostgresTestFixture _postgres;
    private readonly HttpClient _client;

    public OrderFlowTests(PostgresTestFixture postgres, WebApplicationFactory<Program> factory)
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
        }).CreateClient();
    }

    [Fact]
    public async Task Create_approve_reserves_stock_and_cancel_releases()
    {
        var tokens = await LoginAsync();
        var (tenantId, branchId) = await BranchAsync();
        var productId = $"ord-sku-{Guid.NewGuid():N}";
        await SeedStockAsync(tenantId, branchId, productId, 5);

        var create = await SendAsync(
            HttpMethod.Post,
            "/orders",
            tokens.AccessToken,
            new
            {
                branchId,
                customerId = "cust-1",
                currency = "USD",
                paymentMethod = "CASH",
                lines = new[] { new { productId, productName = "Widget", quantity = 2, unitPriceCents = 500 } },
            },
            idempotencyKey: $"create-{productId}");
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        using var createDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var orderId = createDoc.RootElement.GetProperty("id").GetGuid();

        var approve = await SendAsync(HttpMethod.Post, $"/orders/{orderId}/approve", tokens.AccessToken);
        approve.StatusCode.Should().Be(HttpStatusCode.OK, await approve.Content.ReadAsStringAsync());
        using var approveDoc = JsonDocument.Parse(await approve.Content.ReadAsStringAsync());
        approveDoc.RootElement.GetProperty("state").GetString().Should().Be("APPROVED");

        using (var scope = _postgres.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var item = await db.Set<StockItem>().IgnoreQueryFilters()
                .SingleAsync(x => x.TenantId == tenantId && x.ProductId == productId);
            item.Reserved.Should().Be(2);
            (await db.OutboxMessages.CountAsync(x => x.EventName == "ORDER_APPROVED" && x.TenantId == tenantId))
                .Should().BeGreaterThanOrEqualTo(1);
            (await db.OutboxMessages.CountAsync(x => x.EventName == "INVENTORY_RESERVED" && x.TenantId == tenantId))
                .Should().BeGreaterThanOrEqualTo(1);
        }

        var cancel = await SendAsync(
            HttpMethod.Post,
            $"/orders/{orderId}/cancel",
            tokens.AccessToken,
            new { reason = "customer cancelled" });
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        using var cancelDoc = JsonDocument.Parse(await cancel.Content.ReadAsStringAsync());
        cancelDoc.RootElement.GetProperty("state").GetString().Should().Be("CANCELLED");

        using (var scope = _postgres.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var item = await db.Set<StockItem>().IgnoreQueryFilters()
                .SingleAsync(x => x.TenantId == tenantId && x.ProductId == productId);
            item.Reserved.Should().Be(0);
            (await db.OutboxMessages.AnyAsync(x => x.EventName == "ORDER_CANCELLED" && x.TenantId == tenantId))
                .Should().BeTrue();
        }
    }

    [Fact]
    public async Task Approve_with_insufficient_stock_keeps_draft_and_returns_409()
    {
        var tokens = await LoginAsync();
        var (tenantId, branchId) = await BranchAsync();
        var productId = $"ord-short-{Guid.NewGuid():N}";
        await SeedStockAsync(tenantId, branchId, productId, 1);

        var create = await SendAsync(
            HttpMethod.Post,
            "/orders",
            tokens.AccessToken,
            new
            {
                branchId,
                customerId = "cust-2",
                currency = "USD",
                paymentMethod = "CASH",
                lines = new[] { new { productId, productName = "Widget", quantity = 5, unitPriceCents = 100 } },
            });
        create.EnsureSuccessStatusCode();
        using var createDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var orderId = createDoc.RootElement.GetProperty("id").GetGuid();

        var approve = await SendAsync(HttpMethod.Post, $"/orders/{orderId}/approve", tokens.AccessToken);
        approve.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var problem = JsonDocument.Parse(await approve.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().Should().Be("INSUFFICIENT_STOCK");

        var detail = await SendAsync(HttpMethod.Get, $"/orders/{orderId}", tokens.AccessToken);
        detail.StatusCode.Should().Be(HttpStatusCode.OK);
        using var detailDoc = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        detailDoc.RootElement.GetProperty("state").GetString().Should().Be("DRAFT");

        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var item = await db.Set<StockItem>().IgnoreQueryFilters()
            .SingleAsync(x => x.TenantId == tenantId && x.ProductId == productId);
        item.Reserved.Should().Be(0);
        (await db.OutboxMessages.AnyAsync(x => x.EventName == "INVENTORY_RESERVATION_FAILED" && x.TenantId == tenantId))
            .Should().BeFalse();
        (await db.Set<StockReservation>().IgnoreQueryFilters().CountAsync(x => x.OrderId == orderId))
            .Should().Be(0);
    }

    [Fact]
    public async Task Tenant_cannot_read_or_mutate_another_tenants_order()
    {
        var tokensA = await LoginAsync();
        var (tenantA, branchA) = await BranchAsync();
        var productId = $"ord-iso-{Guid.NewGuid():N}";
        await SeedStockAsync(tenantA, branchA, productId, 3);

        var create = await SendAsync(
            HttpMethod.Post,
            "/orders",
            tokensA.AccessToken,
            new
            {
                branchId = branchA,
                customerId = "cust-a",
                currency = "USD",
                paymentMethod = "CASH",
                lines = new[] { new { productId, productName = "Widget", quantity = 1, unitPriceCents = 100 } },
            });
        create.EnsureSuccessStatusCode();
        using var createDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var orderId = createDoc.RootElement.GetProperty("id").GetGuid();

        var tokensB = await LoginOtherTenantAsync();
        var get = await SendAsync(HttpMethod.Get, $"/orders/{orderId}", tokensB.AccessToken);
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var approve = await SendAsync(HttpMethod.Post, $"/orders/{orderId}/approve", tokensB.AccessToken);
        approve.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_with_same_idempotency_key_returns_existing_order()
    {
        var tokens = await LoginAsync();
        var (_, branchId) = await BranchAsync();
        var productId = $"ord-idem-{Guid.NewGuid():N}";
        var key = $"create-{productId}";
        var body = new
        {
            branchId,
            customerId = "cust-idem",
            currency = "USD",
            paymentMethod = "CASH",
            lines = new[] { new { productId, productName = "Widget", quantity = 1, unitPriceCents = 250 } },
        };

        var first = await SendAsync(HttpMethod.Post, "/orders", tokens.AccessToken, body, key);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        using var firstDoc = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var orderId = firstDoc.RootElement.GetProperty("id").GetGuid();

        var second = await SendAsync(HttpMethod.Post, "/orders", tokens.AccessToken, body, key);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        using var secondDoc = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        secondDoc.RootElement.GetProperty("id").GetGuid().Should().Be(orderId);
    }

    private async Task<AuthTokens> LoginAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/auth/login",
            new LoginRequest("acme", "admin@acme.test", IdentitySeedDefaults.KnownInsecureDemoPassword));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthTokens>(JsonOptions))!;
    }

    private async Task<AuthTokens> LoginOtherTenantAsync()
    {
        var slug = "other-" + Guid.NewGuid().ToString("N")[..8];
        const string email = "admin@other.test";
        using (var scope = _postgres.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var tenant = new Tenant(ids.NewId(), slug, "Other", DateTimeOffset.UtcNow);
            var branch = new Branch(ids.NewId(), tenant.Id, "Main");
            db.Add(tenant);
            db.Add(branch);
            db.Add(new User(
                ids.NewId(),
                tenant.Id,
                email,
                EmailNormalizer.Normalize(email),
                await hasher.HashAsync(IdentitySeedDefaults.KnownInsecureDemoPassword),
                RoleNames.Admin,
                branch.Id));
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsJsonAsync(
            "/auth/login",
            new LoginRequest(slug, email, IdentitySeedDefaults.KnownInsecureDemoPassword));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthTokens>(JsonOptions))!;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string url,
        string accessToken,
        object? body = null,
        string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request);
    }

    private async Task<(Guid TenantId, Guid BranchId)> BranchAsync()
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
}
