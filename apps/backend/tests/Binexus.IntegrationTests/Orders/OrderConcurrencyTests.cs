using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Binexus.IntegrationTests.Infrastructure;
using Binexus.Modules.Identity.Application;
using Binexus.Modules.Identity.Domain;
using Binexus.Modules.Inventory.Domain;
using Binexus.Modules.Orders.Application;
using Binexus.Modules.Orders.Domain;
using Binexus.Platform.Dispatching;
using Binexus.Platform.Ids;
using Binexus.Platform.Persistence;
using Binexus.Platform.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Binexus.IntegrationTests.Orders;

public sealed class OrderConcurrencyTests : IClassFixture<PostgresTestFixture>, IClassFixture<WebApplicationFactory<Program>>
{
    private const string SigningKey = "identity-integration-signing-key-with-more-than-thirty-two-bytes";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly PostgresTestFixture _postgres;
    private readonly HttpClient _client;

    public OrderConcurrencyTests(PostgresTestFixture postgres, WebApplicationFactory<Program> factory)
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
    public async Task Dual_approve_exactly_one_wins()
    {
        var tokens = await LoginAsync();
        var (tenantId, branchId, _) = await AcmeContextAsync();
        var productId = $"ord-race-approve-{Guid.NewGuid():N}";
        await SeedStockAsync(tenantId, branchId, productId, 5);
        var orderId = await CreateDraftOrderAsync(tokens.AccessToken, branchId, productId, quantity: 2);

        var responses = await Task.WhenAll(
            SendAsync(HttpMethod.Post, $"/orders/{orderId}/approve", tokens.AccessToken, idempotencyKey: $"approve-a-{orderId:N}"),
            SendAsync(HttpMethod.Post, $"/orders/{orderId}/approve", tokens.AccessToken, idempotencyKey: $"approve-b-{orderId:N}"));

        responses.Count(x => x.StatusCode == HttpStatusCode.OK).Should().Be(1);
        var loser = responses.Single(x => x.StatusCode != HttpStatusCode.OK);
        loser.StatusCode.Should().Be(HttpStatusCode.Conflict);
        // The losing request can either lose on the row-version save or observe the committed APPROVED state.
        (await ProblemCodeAsync(loser)).Should().BeOneOf("CONCURRENCY_CONFLICT", "INVALID_ORDER_TRANSITION");

        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var order = await db.Set<Order>().IgnoreQueryFilters().Include(x => x.Transitions).SingleAsync(x => x.Id == orderId);
        order.State.Should().Be(OrderState.Approved);
        order.Transitions.Count(x => x.ToState == OrderState.Approved).Should().Be(1);
        (await db.Set<StockReservation>().IgnoreQueryFilters().CountAsync(x =>
            x.TenantId == tenantId && x.OrderId == orderId && x.Status == StockReservationStatus.Active)).Should().Be(1);
        (await ReserveMovementCountAsync(db, tenantId, orderId)).Should().Be(1);
        (await OutboxCountAsync(db, tenantId, "ORDER_APPROVED", orderId)).Should().Be(1);
    }

    [Fact]
    public async Task Approve_vs_cancel_exactly_one_terminal()
    {
        var tokens = await LoginAsync();
        var (tenantId, branchId, _) = await AcmeContextAsync();
        var productId = $"ord-race-cancel-{Guid.NewGuid():N}";
        await SeedStockAsync(tenantId, branchId, productId, 5);
        var orderId = await CreateDraftOrderAsync(tokens.AccessToken, branchId, productId, quantity: 2);

        await Task.WhenAll(
            SendAsync(HttpMethod.Post, $"/orders/{orderId}/approve", tokens.AccessToken, idempotencyKey: $"approve-{orderId:N}"),
            SendAsync(
                HttpMethod.Post,
                $"/orders/{orderId}/cancel",
                tokens.AccessToken,
                new { reason = "customer changed mind" },
                idempotencyKey: $"cancel-{orderId:N}"));

        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var order = await db.Set<Order>().IgnoreQueryFilters().Include(x => x.Transitions).SingleAsync(x => x.Id == orderId);
        var item = await db.Set<StockItem>().IgnoreQueryFilters().SingleAsync(x => x.TenantId == tenantId && x.ProductId == productId);
        var activeReservations = await db.Set<StockReservation>().IgnoreQueryFilters().CountAsync(x =>
            x.TenantId == tenantId && x.OrderId == orderId && x.Status == StockReservationStatus.Active);

        if (order.State == OrderState.Approved)
        {
            item.Reserved.Should().BeGreaterThan(0);
            activeReservations.Should().Be(1);
        }
        else
        {
            order.State.Should().Be(OrderState.Cancelled);
            item.Reserved.Should().Be(0);
            activeReservations.Should().Be(0);
        }

        (order.State == OrderState.Cancelled && activeReservations > 0).Should().BeFalse();
        order.Transitions.Count(x => x.ToState is OrderState.Approved or OrderState.Cancelled).Should().Be(1);
    }

    [Fact]
    public async Task Dual_cancel_after_approve_is_idempotent_safe()
    {
        var tokens = await LoginAsync();
        var (tenantId, branchId, _) = await AcmeContextAsync();
        var productId = $"ord-race-dual-cancel-{Guid.NewGuid():N}";
        await SeedStockAsync(tenantId, branchId, productId, 5);
        var orderId = await CreateDraftOrderAsync(tokens.AccessToken, branchId, productId, quantity: 2);

        (await SendAsync(HttpMethod.Post, $"/orders/{orderId}/approve", tokens.AccessToken)).EnsureSuccessStatusCode();

        var cancelBody = new { reason = "customer cancelled" };
        var key = $"cancel-same-{orderId:N}";
        await Task.WhenAll(
            SendAsync(HttpMethod.Post, $"/orders/{orderId}/cancel", tokens.AccessToken, cancelBody, key),
            SendAsync(HttpMethod.Post, $"/orders/{orderId}/cancel", tokens.AccessToken, cancelBody, key));

        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var order = await db.Set<Order>().IgnoreQueryFilters().Include(x => x.Transitions).SingleAsync(x => x.Id == orderId);
        var item = await db.Set<StockItem>().IgnoreQueryFilters().SingleAsync(x => x.TenantId == tenantId && x.ProductId == productId);

        order.State.Should().Be(OrderState.Cancelled);
        order.Transitions.Count(x => x.ToState == OrderState.Cancelled).Should().Be(1);
        item.Reserved.Should().Be(0);
        (await OutboxCountAsync(db, tenantId, "INVENTORY_RELEASED", orderId)).Should().Be(1);
        (await ReleaseMovementCountAsync(db, tenantId, productId)).Should().Be(1);
    }

    [Fact]
    public async Task Dual_requeue_exactly_one_transition()
    {
        var tokens = await LoginAsync();
        var (tenantId, branchId, userId) = await AcmeContextAsync();
        var productId = $"ord-race-requeue-{Guid.NewGuid():N}";
        await SeedStockAsync(tenantId, branchId, productId, 5);
        var orderId = await CreateDraftOrderAsync(tokens.AccessToken, branchId, productId, quantity: 1);
        (await SendAsync(HttpMethod.Post, $"/orders/{orderId}/approve", tokens.AccessToken)).EnsureSuccessStatusCode();
        await AdvanceToDeliveryAttemptFailedAsync(tenantId, branchId, userId, orderId);

        await Task.WhenAll(
            SendAsync(
                HttpMethod.Post,
                $"/orders/{orderId}/requeue-for-delivery",
                tokens.AccessToken,
                new { reason = "retry route" },
                idempotencyKey: $"requeue-a-{orderId:N}"),
            SendAsync(
                HttpMethod.Post,
                $"/orders/{orderId}/requeue-for-delivery",
                tokens.AccessToken,
                new { reason = "retry route" },
                idempotencyKey: $"requeue-b-{orderId:N}"));

        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var order = await db.Set<Order>().IgnoreQueryFilters().Include(x => x.Transitions).SingleAsync(x => x.Id == orderId);
        order.State.Should().Be(OrderState.ReadyForDeliveryRoute);
        order.Transitions.Count(x =>
            x.FromState == OrderState.DeliveryAttemptFailed
            && x.ToState == OrderState.ReadyForDeliveryRoute).Should().Be(1);
    }

    [Fact]
    public async Task Create_approve_cancel_use_uuid_v7_ids()
    {
        var tokens = await LoginAsync();
        var (tenantId, branchId, _) = await AcmeContextAsync();
        var productId = $"ord-uuid-{Guid.NewGuid():N}";
        await SeedStockAsync(tenantId, branchId, productId, 3);
        var orderId = await CreateDraftOrderAsync(tokens.AccessToken, branchId, productId, quantity: 1);
        (await SendAsync(HttpMethod.Post, $"/orders/{orderId}/approve", tokens.AccessToken)).EnsureSuccessStatusCode();
        (await SendAsync(HttpMethod.Post, $"/orders/{orderId}/cancel", tokens.AccessToken, new { reason = "uuid check" })).EnsureSuccessStatusCode();

        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var order = await db.Set<Order>().IgnoreQueryFilters()
            .Include(x => x.Lines)
            .Include(x => x.Transitions)
            .SingleAsync(x => x.Id == orderId);

        AssertUuidV7(order.Id);
        order.Lines.Select(x => x.Id).Should().OnlyContain(id => IsUuidV7(id));
        order.Transitions.Select(x => x.Id).Should().OnlyContain(id => IsUuidV7(id));
    }

    [Fact]
    public async Task Cancel_draft_does_not_release_inventory()
    {
        var tokens = await LoginAsync();
        var (tenantId, branchId, _) = await AcmeContextAsync();
        var productId = $"ord-cancel-draft-{Guid.NewGuid():N}";
        await SeedStockAsync(tenantId, branchId, productId, 3);
        var orderId = await CreateDraftOrderAsync(tokens.AccessToken, branchId, productId, quantity: 1);

        var cancel = await SendAsync(HttpMethod.Post, $"/orders/{orderId}/cancel", tokens.AccessToken, new { reason = "draft cancel" });
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var item = await db.Set<StockItem>().IgnoreQueryFilters().SingleAsync(x => x.TenantId == tenantId && x.ProductId == productId);
        item.Reserved.Should().Be(0);
        (await OutboxCountAsync(db, tenantId, "INVENTORY_RELEASED", orderId)).Should().Be(0);
        (await ReleaseMovementCountAsync(db, tenantId, productId)).Should().Be(0);
    }

    [Fact]
    public async Task Cancel_approved_releases_once()
    {
        var tokens = await LoginAsync();
        var (tenantId, branchId, _) = await AcmeContextAsync();
        var productId = $"ord-cancel-approved-{Guid.NewGuid():N}";
        await SeedStockAsync(tenantId, branchId, productId, 3);
        var orderId = await CreateDraftOrderAsync(tokens.AccessToken, branchId, productId, quantity: 1);
        (await SendAsync(HttpMethod.Post, $"/orders/{orderId}/approve", tokens.AccessToken)).EnsureSuccessStatusCode();

        var cancel = await SendAsync(HttpMethod.Post, $"/orders/{orderId}/cancel", tokens.AccessToken, new { reason = "approved cancel" });
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var item = await db.Set<StockItem>().IgnoreQueryFilters().SingleAsync(x => x.TenantId == tenantId && x.ProductId == productId);
        item.Reserved.Should().Be(0);
        (await OutboxCountAsync(db, tenantId, "INVENTORY_RELEASED", orderId)).Should().Be(1);
        (await ReleaseMovementCountAsync(db, tenantId, productId)).Should().Be(1);
    }

    [Fact]
    public async Task Cancel_delivery_attempt_failed_releases_active_reservations_nest_parity()
    {
        var tokens = await LoginAsync();
        var (tenantId, branchId, userId) = await AcmeContextAsync();
        var productId = $"ord-cancel-failed-{Guid.NewGuid():N}";
        await SeedStockAsync(tenantId, branchId, productId, 3);
        var orderId = await CreateDraftOrderAsync(tokens.AccessToken, branchId, productId, quantity: 1);
        (await SendAsync(HttpMethod.Post, $"/orders/{orderId}/approve", tokens.AccessToken)).EnsureSuccessStatusCode();
        await AdvanceToDeliveryAttemptFailedAsync(tenantId, branchId, userId, orderId);

        var cancel = await SendAsync(HttpMethod.Post, $"/orders/{orderId}/cancel", tokens.AccessToken, new { reason = "failed delivery cancel" });
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var order = await db.Set<Order>().IgnoreQueryFilters().SingleAsync(x => x.Id == orderId);
        var item = await db.Set<StockItem>().IgnoreQueryFilters().SingleAsync(x => x.TenantId == tenantId && x.ProductId == productId);
        var activeReservations = await db.Set<StockReservation>().IgnoreQueryFilters().CountAsync(x =>
            x.TenantId == tenantId && x.OrderId == orderId && x.Status == StockReservationStatus.Active);

        order.State.Should().Be(OrderState.Cancelled);
        item.Reserved.Should().Be(0);
        activeReservations.Should().Be(0);
        (await OutboxCountAsync(db, tenantId, "INVENTORY_RELEASED", orderId)).Should().Be(1);
        (await ReleaseMovementCountAsync(db, tenantId, productId)).Should().Be(1);
    }

    [Fact]
    public async Task Approve_same_key_twice_is_idempotent()
    {
        var tokens = await LoginAsync();
        var (tenantId, branchId, _) = await AcmeContextAsync();
        var productId = $"ord-idem-approve-{Guid.NewGuid():N}";
        await SeedStockAsync(tenantId, branchId, productId, 3);
        var orderId = await CreateDraftOrderAsync(tokens.AccessToken, branchId, productId, quantity: 1);
        var key = $"approve-idem-{orderId:N}";

        var first = await SendAsync(HttpMethod.Post, $"/orders/{orderId}/approve", tokens.AccessToken, idempotencyKey: key);
        var second = await SendAsync(HttpMethod.Post, $"/orders/{orderId}/approve", tokens.AccessToken, idempotencyKey: key);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var order = await db.Set<Order>().IgnoreQueryFilters().Include(x => x.Transitions).SingleAsync(x => x.Id == orderId);
        order.Transitions.Count(x => x.ToState == OrderState.Approved).Should().Be(1);
        (await ReserveMovementCountAsync(db, tenantId, orderId)).Should().Be(1);
        (await OutboxCountAsync(db, tenantId, "ORDER_APPROVED", orderId)).Should().Be(1);
    }

    [Fact]
    public async Task Approve_same_key_on_different_order_conflicts()
    {
        var tokens = await LoginAsync();
        var (tenantId, branchId, _) = await AcmeContextAsync();
        var firstProduct = $"ord-idem-approve-a-{Guid.NewGuid():N}";
        var secondProduct = $"ord-idem-approve-b-{Guid.NewGuid():N}";
        await SeedStockAsync(tenantId, branchId, firstProduct, 3);
        await SeedStockAsync(tenantId, branchId, secondProduct, 3);
        var firstOrderId = await CreateDraftOrderAsync(tokens.AccessToken, branchId, firstProduct, quantity: 1);
        var secondOrderId = await CreateDraftOrderAsync(tokens.AccessToken, branchId, secondProduct, quantity: 1);
        var key = $"approve-reused-{Guid.NewGuid():N}";
        (await SendAsync(HttpMethod.Post, $"/orders/{firstOrderId}/approve", tokens.AccessToken, idempotencyKey: key)).EnsureSuccessStatusCode();

        var reused = await SendAsync(HttpMethod.Post, $"/orders/{secondOrderId}/approve", tokens.AccessToken, idempotencyKey: key);

        reused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(reused)).Should().Be("IDEMPOTENCY_KEY_REUSED");
    }

    [Fact]
    public async Task Cancel_same_key_different_reason_returns_409()
    {
        var tokens = await LoginAsync();
        var (tenantId, branchId, _) = await AcmeContextAsync();
        var productId = $"ord-idem-cancel-{Guid.NewGuid():N}";
        await SeedStockAsync(tenantId, branchId, productId, 3);
        var orderId = await CreateDraftOrderAsync(tokens.AccessToken, branchId, productId, quantity: 1);
        (await SendAsync(HttpMethod.Post, $"/orders/{orderId}/approve", tokens.AccessToken)).EnsureSuccessStatusCode();
        var key = $"cancel-reason-{Guid.NewGuid():N}";
        (await SendAsync(HttpMethod.Post, $"/orders/{orderId}/cancel", tokens.AccessToken, new { reason = "first" }, key)).EnsureSuccessStatusCode();

        var reused = await SendAsync(HttpMethod.Post, $"/orders/{orderId}/cancel", tokens.AccessToken, new { reason = "second" }, key);

        reused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(reused)).Should().Be("IDEMPOTENCY_KEY_REUSED");
    }

    [Fact]
    public async Task Create_same_key_different_payload_returns_409()
    {
        var tokens = await LoginAsync();
        var (_, branchId, _) = await AcmeContextAsync();
        var productId = $"ord-idem-create-{Guid.NewGuid():N}";
        var key = $"create-reused-{Guid.NewGuid():N}";
        var first = new
        {
            branchId,
            customerId = "cust-a",
            currency = "USD",
            paymentMethod = "CASH",
            lines = new[] { new { productId, productName = "Widget", quantity = 1, unitPriceCents = 100 } },
        };
        var second = new
        {
            branchId,
            customerId = "cust-b",
            currency = "USD",
            paymentMethod = "CASH",
            lines = new[] { new { productId, productName = "Widget", quantity = 1, unitPriceCents = 100 } },
        };
        (await SendAsync(HttpMethod.Post, "/orders", tokens.AccessToken, first, key)).EnsureSuccessStatusCode();

        var reused = await SendAsync(HttpMethod.Post, "/orders", tokens.AccessToken, second, key);

        reused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemCodeAsync(reused)).Should().Be("IDEMPOTENCY_KEY_REUSED");
    }

    private async Task<AuthTokens> LoginAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/auth/login",
            new LoginRequest("acme", "admin@acme.test", IdentitySeedDefaults.KnownInsecureDemoPassword));
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

    private async Task<Guid> CreateDraftOrderAsync(string accessToken, Guid branchId, string productId, int quantity)
    {
        var create = await SendAsync(
            HttpMethod.Post,
            "/orders",
            accessToken,
            new
            {
                branchId,
                customerId = $"cust-{productId}",
                currency = "USD",
                paymentMethod = "CASH",
                lines = new[] { new { productId, productName = "Widget", quantity, unitPriceCents = 500 } },
            },
            idempotencyKey: $"create-{productId}");
        create.StatusCode.Should().Be(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        using var createDoc = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        return createDoc.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<(Guid TenantId, Guid BranchId, Guid UserId)> AcmeContextAsync()
    {
        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var tenant = await db.Set<Tenant>().IgnoreQueryFilters().SingleAsync(x => x.Slug == "acme");
        var branch = await db.Set<Branch>().IgnoreQueryFilters().SingleAsync(x => x.TenantId == tenant.Id && x.Name == "Main");
        var user = await db.Set<User>().IgnoreQueryFilters().SingleAsync(x =>
            x.TenantId == tenant.Id && x.NormalizedEmail == EmailNormalizer.Normalize("admin@acme.test"));
        return (tenant.Id, branch.Id, user.Id);
    }

    private async Task SeedStockAsync(Guid tenantId, Guid branchId, string productId, int onHand)
    {
        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        db.Add(new StockItem(ids.NewId(), tenantId, branchId, productId, onHand, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    private async Task AdvanceToDeliveryAttemptFailedAsync(Guid tenantId, Guid branchId, Guid userId, Guid orderId)
    {
        using var scope = _postgres.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        tenant.SetContext(new TenantContext(tenantId, userId, RoleNames.Admin, branchId, "orders-lifecycle-test"));
        var dispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        (await dispatcher.DispatchAsync(new MoveOrderToPickingCommand(orderId, userId, null, "orders-lifecycle-test"))).IsSuccess.Should().BeTrue();
        (await dispatcher.DispatchAsync(new MarkOrderReadyForDeliveryRouteCommand(orderId, userId, null, "orders-lifecycle-test"))).IsSuccess.Should().BeTrue();
        (await dispatcher.DispatchAsync(new MarkOrderOutForDeliveryCommand(orderId, userId, null, "orders-lifecycle-test"))).IsSuccess.Should().BeTrue();
        (await dispatcher.DispatchAsync(new MarkOrderDeliveryAttemptFailedCommand(orderId, userId, "recipient unavailable", "orders-lifecycle-test"))).IsSuccess.Should().BeTrue();
    }

    private static async Task<int> ReserveMovementCountAsync(BinexusDbContext db, Guid tenantId, Guid orderId) =>
        await db.Set<StockMovement>().IgnoreQueryFilters().CountAsync(x =>
            x.TenantId == tenantId
            && x.Type == StockMovementType.Reserve
            && x.OperationKey != null
            && x.OperationKey.StartsWith($"order:{orderId}:"));

    private static async Task<int> ReleaseMovementCountAsync(BinexusDbContext db, Guid tenantId, string productId) =>
        await db.Set<StockMovement>().IgnoreQueryFilters().CountAsync(x =>
            x.TenantId == tenantId && x.ProductId == productId && x.Type == StockMovementType.Release);

    private static async Task<int> OutboxCountAsync(BinexusDbContext db, Guid tenantId, string eventName, Guid orderId)
    {
        var payloads = await db.OutboxMessages
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.EventName == eventName)
            .Select(x => x.PayloadJson)
            .ToListAsync();
        return payloads.Count(x => x.Contains(orderId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<string> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.GetProperty("code").GetString()!;
    }

    private static void AssertUuidV7(Guid value) => IsUuidV7(value).Should().BeTrue($"{value} should be a UUID v7");

    private static bool IsUuidV7(Guid value) => value.ToString("D")[14] == '7';
}
