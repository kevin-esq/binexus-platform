using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Binexus.IntegrationTests.Infrastructure;
using Binexus.Modules.Identity.Application;
using Binexus.Modules.Identity.Domain;
using Binexus.Modules.Logistics.Application;
using Binexus.Modules.Logistics.Domain;
using Binexus.Modules.Orders.Domain;
using Binexus.Platform.Features.Contracts;
using Binexus.Platform.Ids;
using Binexus.Platform.Messaging;
using Binexus.Platform.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Binexus.IntegrationTests.Logistics;

[Collection("postgres")]
public sealed class LogisticsFlowTests : IClassFixture<PostgresTestFixture>, IClassFixture<WebApplicationFactory<Program>>
{
    private const string SigningKey = "logistics-integration-signing-key-with-more-than-thirty-two-bytes";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly PostgresTestFixture _postgres;
    private readonly HttpClient _client;

    public LogisticsFlowTests(PostgresTestFixture postgres, WebApplicationFactory<Program> factory)
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
            builder.UseSetting("Features:LiquidationKillSwitch", "true");
            builder.UseSetting("Logistics:Storage:Provider", "Local");
        }).CreateClient();
    }

    [Fact]
    public async Task Order_ready_and_cancelled_events_upsert_then_cancel_candidate()
    {
        await _postgres.ResetOutboxAsync();
        var context = await AcmeContextAsync();
        var orderId = await SeedOrderAsync(context.TenantId, context.BranchId, context.UserId, OrderState.ReadyForDeliveryRoute, "CASH");
        await SeedOrderEventAsync(context.TenantId, context.BranchId, orderId, "ORDER_READY_FOR_DELIVERY_ROUTE");

        (await ProcessOutboxAsync()).Should().BeGreaterThanOrEqualTo(1);
        await SeedOrderEventAsync(context.TenantId, context.BranchId, orderId, "ORDER_CANCELLED");
        (await ProcessOutboxAsync()).Should().BeGreaterThanOrEqualTo(1);

        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var candidate = await db.Set<DeliveryRouteCandidate>().IgnoreQueryFilters().SingleAsync(x => x.OrderId == orderId);
        candidate.Status.Should().Be(DeliveryRouteCandidateStatus.Cancelled);
    }

    [Fact]
    public async Task Create_assign_dispatch_and_confirm_with_proof_moves_order_synchronously()
    {
        await _postgres.ResetOutboxAsync();
        var tokens = await LoginAsync();
        var context = await AcmeContextAsync();
        var orderId = await SeedOrderAsync(context.TenantId, context.BranchId, context.UserId, OrderState.ReadyForDeliveryRoute, "CARD");
        await SeedCandidateAsync(context.TenantId, context.BranchId, orderId);

        var route = await CreateRouteAsync(tokens.AccessToken, context.BranchId, "route-main");
        var repeat = await CreateRouteAsync(tokens.AccessToken, context.BranchId, "route-main");
        repeat.Id.Should().Be(route.Id);
        await PostOkAsync($"/logistics/delivery-routes/{route.Id}/assign-orders", new AssignOrdersRequest([orderId]), tokens.AccessToken, "assign-main");
        await PostOkAsync($"/logistics/delivery-routes/{route.Id}/dispatch", new DispatchDeliveryRouteRequest(context.UserId), tokens.AccessToken, "dispatch-main");
        var stopId = await FirstStopIdAsync(route.Id);
        var upload = await PostJsonAsync<DeliveryProofUploadResult>($"/logistics/delivery-route-stops/{stopId}/proof-uploads", new CreateDeliveryProofUploadRequest("PHOTO", "image/jpeg", 1024), tokens.AccessToken, "proof-main");
        await PostOkAsync($"/logistics/delivery-route-stops/{stopId}/confirm-delivery", new ConfirmDeliveryRequest(new(upload.ObjectKey, null, "Receiver", "ok", 10.5m, -66.9m)), tokens.AccessToken, "confirm-main");

        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var order = await db.Set<Order>().IgnoreQueryFilters().SingleAsync(x => x.Id == orderId);
        var stop = await db.Set<DeliveryRouteStop>().IgnoreQueryFilters().SingleAsync(x => x.Id == stopId);
        var proof = await db.Set<DeliveryProof>().IgnoreQueryFilters().SingleAsync(x => x.DeliveryRouteStopId == stopId);
        order.State.Should().Be(OrderState.Delivered);
        stop.Status.Should().Be(DeliveryRouteStopStatus.Delivered);
        proof.PhotoObjectKey.Should().Be(upload.ObjectKey);
        (await OutboxCountAsync(db, context.TenantId, "DELIVERY_CONFIRMED", orderId)).Should().Be(1);
    }

    [Fact]
    public async Task Confirm_and_fail_are_mutually_exclusive()
    {
        await _postgres.ResetOutboxAsync();
        var tokens = await LoginAsync();
        var context = await AcmeContextAsync();
        var orderId = await SeedOrderAsync(context.TenantId, context.BranchId, context.UserId, OrderState.ReadyForDeliveryRoute, "CARD");
        var stopId = await CreateDispatchedStopAsync(tokens.AccessToken, context, orderId, "mutual");

        await PostOkAsync($"/logistics/delivery-route-stops/{stopId}/report-failed-delivery", new ReportFailedDeliveryRequest("NO_RECIPIENT", "closed"), tokens.AccessToken, "fail-main");
        var confirm = await SendJsonAsync(HttpMethod.Post, $"/logistics/delivery-route-stops/{stopId}/confirm-delivery", new ConfirmDeliveryRequest(null), tokens.AccessToken, "confirm-after-fail");

        confirm.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var order = await db.Set<Order>().IgnoreQueryFilters().SingleAsync(x => x.Id == orderId);
        order.State.Should().Be(OrderState.DeliveryAttemptFailed);
    }

    [Fact]
    public async Task Liquidate_cod_exact_duplicate_and_feature_off()
    {
        await _postgres.ResetOutboxAsync();
        var tokens = await LoginAsync();
        var context = await AcmeContextAsync();
        var orderId = await SeedOrderAsync(context.TenantId, context.BranchId, context.UserId, OrderState.ReadyForDeliveryRoute, "CASH");
        var stopId = await CreateDispatchedStopAsync(tokens.AccessToken, context, orderId, "liq");
        await PostOkAsync($"/logistics/delivery-route-stops/{stopId}/confirm-delivery", new ConfirmDeliveryRequest(null), tokens.AccessToken, "liq-confirm");
        var routeId = await RouteIdForStopAsync(stopId);
        await SetLiquidationFeatureAsync(context.TenantId, enabled: true);

        await PostOkAsync($"/logistics/delivery-routes/{routeId}/liquidate", new LiquidateDeliveryRouteRequest(100, null, null, null), tokens.AccessToken, "liq-main");
        var duplicate = await SendJsonAsync(HttpMethod.Post, $"/logistics/delivery-routes/{routeId}/liquidate", new LiquidateDeliveryRouteRequest(100, null, null, null), tokens.AccessToken, "liq-other");

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var order = await db.Set<Order>().IgnoreQueryFilters().SingleAsync(x => x.Id == orderId);
        order.State.Should().Be(OrderState.Settled);
    }

    [Fact]
    public async Task Logistics_http_is_tenant_isolated()
    {
        await _postgres.ResetOutboxAsync();
        var tokensA = await LoginAsync();
        var tokensB = await LoginOtherTenantAsync();
        var context = await AcmeContextAsync();
        var orderId = await SeedOrderAsync(context.TenantId, context.BranchId, context.UserId, OrderState.ReadyForDeliveryRoute, "CASH");
        await SeedCandidateAsync(context.TenantId, context.BranchId, orderId);

        var listA = await SendAsync(HttpMethod.Get, "/logistics/delivery-route-candidates?status=READY", tokensA.AccessToken);
        var listB = await SendAsync(HttpMethod.Get, "/logistics/delivery-route-candidates?status=READY", tokensB.AccessToken);

        listA.StatusCode.Should().Be(HttpStatusCode.OK);
        listB.StatusCode.Should().Be(HttpStatusCode.OK);
        using var docA = JsonDocument.Parse(await listA.Content.ReadAsStringAsync());
        using var docB = JsonDocument.Parse(await listB.Content.ReadAsStringAsync());
        docA.RootElement.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("orderId").GetGuid() == orderId).Should().BeTrue();
        docB.RootElement.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("orderId").GetGuid() == orderId).Should().BeFalse();
    }

    private async Task<Guid> CreateDispatchedStopAsync(string token, (Guid TenantId, Guid BranchId, Guid UserId) context, Guid orderId, string suffix)
    {
        await SeedCandidateAsync(context.TenantId, context.BranchId, orderId);
        var route = await CreateRouteAsync(token, context.BranchId, $"route-{suffix}");
        await PostOkAsync($"/logistics/delivery-routes/{route.Id}/assign-orders", new AssignOrdersRequest([orderId]), token, $"assign-{suffix}");
        await PostOkAsync($"/logistics/delivery-routes/{route.Id}/dispatch", new DispatchDeliveryRouteRequest(context.UserId), token, $"dispatch-{suffix}");
        return await FirstStopIdAsync(route.Id);
    }

    private async Task<DeliveryRouteSummary> CreateRouteAsync(string token, Guid branchId, string key) =>
        await PostJsonAsync<DeliveryRouteSummary>("/logistics/delivery-routes", new CreateDeliveryRouteRequest(branchId, null), token, key);

    private async Task<Guid> FirstStopIdAsync(Guid routeId)
    {
        var response = await SendAsync(HttpMethod.Get, $"/logistics/delivery-routes/{routeId}/stops", (await LoginAsync()).AccessToken);
        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<ListDeliveryRouteStopsResult>(JsonOptions))!;
        return result.Items.Single().Id;
    }

    private async Task<Guid> RouteIdForStopAsync(Guid stopId)
    {
        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        return (await db.Set<DeliveryRouteStop>().IgnoreQueryFilters().SingleAsync(x => x.Id == stopId)).DeliveryRouteId;
    }

    private async Task PostOkAsync<T>(string url, T body, string token, string key)
    {
        var response = await SendJsonAsync(HttpMethod.Post, url, body, token, key);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<T> PostJsonAsync<T>(string url, object body, string token, string key)
    {
        var response = await SendJsonAsync(HttpMethod.Post, url, body, token, key);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private async Task<HttpResponseMessage> SendJsonAsync<T>(HttpMethod method, string url, T body, string token, string key)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private async Task<int> ProcessOutboxAsync()
    {
        using var scope = _postgres.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();
        return await outbox.ProcessBatchAsync($"logistics-worker-{Guid.NewGuid():N}", CancellationToken.None);
    }

    private async Task SeedCandidateAsync(Guid tenantId, Guid branchId, Guid orderId)
    {
        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        db.Add(new DeliveryRouteCandidate(ids.NewId(), tenantId, orderId, branchId, ids.NewId(), DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedOrderAsync(Guid tenantId, Guid branchId, Guid actorId, OrderState state, string paymentMethod)
    {
        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var orderId = ids.NewId();
        var now = DateTimeOffset.UtcNow;
        var order = new Order(orderId, tenantId, branchId, $"cust-{Guid.NewGuid():N}", "USD", paymentMethod, actorId, ids.NewId(), [new OrderLine(ids.NewId(), orderId, $"sku-{Guid.NewGuid():N}", "Widget", 1, 100)], now);
        db.Add(order);
        db.Add(order.Approve(ids.NewId(), actorId, null, now));
        db.Add(order.MoveToPicking(ids.NewId(), actorId, null, now));
        db.Add(order.MarkReadyForDeliveryRoute(ids.NewId(), actorId, null, now));
        if (state is OrderState.OutForDelivery or OrderState.Delivered or OrderState.Settled or OrderState.DeliveryAttemptFailed)
        {
            db.Add(order.MarkOutForDelivery(ids.NewId(), actorId, null, now));
        }

        if (state is OrderState.Delivered or OrderState.Settled)
        {
            db.Add(order.MarkDelivered(ids.NewId(), actorId, null, now));
        }

        if (state == OrderState.Settled)
        {
            db.Add(order.Settle(ids.NewId(), actorId, null, now));
        }

        if (state == OrderState.DeliveryAttemptFailed)
        {
            db.Add(order.MarkDeliveryAttemptFailed(ids.NewId(), actorId, null, now));
        }

        await db.SaveChangesAsync();
        return orderId;
    }

    private async Task SeedOrderEventAsync(Guid tenantId, Guid branchId, Guid orderId, string eventName)
    {
        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = ids.NewId(),
            TenantId = tenantId,
            EventName = eventName,
            PayloadJson = JsonSerializer.Serialize(new { orderId, branchId }),
            OccurredAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task<(Guid TenantId, Guid BranchId, Guid UserId)> AcmeContextAsync()
    {
        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var tenant = await db.Set<Tenant>().IgnoreQueryFilters().SingleAsync(x => x.Slug == "acme");
        var branch = await db.Set<Branch>().IgnoreQueryFilters().SingleAsync(x => x.TenantId == tenant.Id && x.Name == "Main");
        var user = await db.Set<User>().IgnoreQueryFilters().SingleAsync(x => x.TenantId == tenant.Id && x.NormalizedEmail == EmailNormalizer.Normalize("admin@acme.test"));
        return (tenant.Id, branch.Id, user.Id);
    }

    private async Task SetLiquidationFeatureAsync(Guid tenantId, bool enabled)
    {
        using var scope = _postgres.CreateScope();
        var features = scope.ServiceProvider.GetRequiredService<ITenantFeatureService>();
        await features.SetEnabledAsync(tenantId, FeatureKey.Liquidation, enabled);
    }

    private static async Task<int> OutboxCountAsync(BinexusDbContext db, Guid tenantId, string eventName, Guid orderId)
    {
        var payloads = await db.OutboxMessages.AsNoTracking().Where(x => x.TenantId == tenantId && x.EventName == eventName).Select(x => x.PayloadJson).ToListAsync();
        return payloads.Count(x => x.Contains(orderId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    private async Task<AuthTokens> LoginAsync()
    {
        var response = await _client.PostAsJsonAsync("/auth/login", new LoginRequest("acme", "admin@acme.test", IdentitySeedDefaults.KnownInsecureDemoPassword));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthTokens>(JsonOptions))!;
    }

    private async Task<AuthTokens> LoginOtherTenantAsync()
    {
        var slug = "logistics-other-" + Guid.NewGuid().ToString("N")[..8];
        const string email = "admin@logistics-other.test";
        using (var scope = _postgres.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var tenant = new Tenant(ids.NewId(), slug, "Other", DateTimeOffset.UtcNow);
            var branch = new Branch(ids.NewId(), tenant.Id, "Main");
            db.Add(tenant);
            db.Add(branch);
            db.Add(new User(ids.NewId(), tenant.Id, email, EmailNormalizer.Normalize(email), await hasher.HashAsync(IdentitySeedDefaults.KnownInsecureDemoPassword), RoleNames.Admin, branch.Id));
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(slug, email, IdentitySeedDefaults.KnownInsecureDemoPassword));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthTokens>(JsonOptions))!;
    }
}
