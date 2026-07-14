using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Binexus.IntegrationTests.Infrastructure;
using Binexus.Modules.Identity.Application;
using Binexus.Modules.Identity.Domain;
using Binexus.Modules.Orders.Contracts;
using Binexus.Modules.Orders.Domain;
using Binexus.Modules.Warehouse.Domain;
using Binexus.Platform.Ids;
using Binexus.Platform.Messaging;
using Binexus.Platform.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Binexus.IntegrationTests.Warehouse;

[Collection("postgres")]
public sealed class WarehouseFlowTests : IClassFixture<PostgresTestFixture>, IClassFixture<CloudApiFactory>
{
    private const string SigningKey = "warehouse-integration-signing-key-with-more-than-thirty-two-bytes";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly PostgresTestFixture _postgres;
    private readonly HttpClient _client;

    public WarehouseFlowTests(PostgresTestFixture postgres, CloudApiFactory factory)
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
    public async Task Order_approved_creates_one_picking_task_and_moves_order_to_picking()
    {
        await _postgres.ResetOutboxAsync();
        var context = await AcmeContextAsync();
        var seed = await SeedApprovedOrderAndMessageAsync(context.TenantId, context.BranchId, context.UserId, $"wh-create-{Guid.NewGuid():N}");

        var first = await ProcessOutboxAsync();
        var second = await ProcessOutboxAsync();
        await SeedApprovedMessageAsync(context.TenantId, context.BranchId, context.UserId, seed.OrderId, seed.LineId, seed.ProductId, seed.Quantity);
        var duplicateEvent = await ProcessOutboxAsync();

        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var task = await db.Set<PickingTask>().IgnoreQueryFilters().Include(x => x.Lines).SingleAsync(x => x.OrderId == seed.OrderId);
        var order = await db.Set<Order>().IgnoreQueryFilters().Include(x => x.Transitions).SingleAsync(x => x.Id == seed.OrderId);

        first.Should().BeGreaterThanOrEqualTo(1);
        second.Should().Be(0);
        duplicateEvent.Should().BeGreaterThanOrEqualTo(1);
        task.Status.Should().Be(PickingTaskStatus.Pending);
        task.Lines.Should().ContainSingle(line => line.OrderLineId == seed.LineId && line.Quantity == seed.Quantity);
        order.State.Should().Be(OrderState.Picking);
        (await db.Set<PickingTask>().IgnoreQueryFilters().CountAsync(x => x.OrderId == seed.OrderId)).Should().Be(1);
        order.Transitions.Count(x => x.ToState == OrderState.Picking).Should().Be(1);
    }

    [Fact]
    public async Task Warehouse_handler_effects_roll_back_when_order_move_fails()
    {
        await _postgres.ResetOutboxAsync();
        var context = await AcmeContextAsync();
        var seed = await SeedApprovedOrderAndMessageAsync(context.TenantId, context.BranchId, context.UserId, $"wh-rb-{Guid.NewGuid():N}");

        using (var scope = _postgres.CreateScope(services =>
        {
            services.RemoveAll<IOrderFulfillmentApi>();
            services.AddScoped<IOrderFulfillmentApi, FailingOrderFulfillmentApi>();
        }))
        {
            var processor = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();
            (await processor.ProcessBatchAsync("warehouse-rb", CancellationToken.None)).Should().Be(0);
        }

        using var verify = _postgres.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<BinexusDbContext>();
        (await db.Set<PickingTask>().IgnoreQueryFilters().CountAsync(x => x.OrderId == seed.OrderId)).Should().Be(0);
        var delivery = await db.EventHandlerDeliveries.AsNoTracking().SingleAsync(x => x.EventId == seed.MessageId);
        delivery.Status.Should().Be(EventHandlerDeliveryStatus.FailedTransient);
    }

    [Fact]
    public async Task Order_approved_after_cancel_is_ignored_and_creates_no_task()
    {
        await _postgres.ResetOutboxAsync();
        var context = await AcmeContextAsync();
        var seed = await SeedApprovedOrderAndMessageAsync(context.TenantId, context.BranchId, context.UserId, $"wh-cancel-{Guid.NewGuid():N}");
        using (var cancelScope = _postgres.CreateScope())
        {
            var db = cancelScope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var ids = cancelScope.ServiceProvider.GetRequiredService<IIdGenerator>();
            var order = await db.Set<Order>().IgnoreQueryFilters().SingleAsync(x => x.Id == seed.OrderId);
            db.Add(order.Cancel(ids.NewId(), context.UserId, "cancelled before warehouse", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        (await ProcessOutboxAsync()).Should().Be(1);

        using var scope = _postgres.CreateScope();
        var verify = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        (await verify.Set<PickingTask>().IgnoreQueryFilters().CountAsync(x => x.OrderId == seed.OrderId)).Should().Be(0);
        var delivery = await verify.EventHandlerDeliveries.AsNoTracking().SingleAsync(x => x.EventId == seed.MessageId);
        delivery.Status.Should().Be(EventHandlerDeliveryStatus.ProcessedIgnored);
    }

    [Fact]
    public async Task Order_approved_for_missing_order_is_ignored()
    {
        await _postgres.ResetOutboxAsync();
        var context = await AcmeContextAsync();
        var messageId = await SeedApprovedMessageAsync(
            context.TenantId,
            context.BranchId,
            context.UserId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            $"wh-missing-{Guid.NewGuid():N}",
            1);

        (await ProcessOutboxAsync()).Should().Be(1);

        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var delivery = await db.EventHandlerDeliveries.AsNoTracking().SingleAsync(x => x.EventId == messageId);
        delivery.Status.Should().Be(EventHandlerDeliveryStatus.ProcessedIgnored);
        (await db.Set<PickingTask>().IgnoreQueryFilters().CountAsync(x => x.CreatedFromEventId == messageId)).Should().Be(0);
    }

    [Fact]
    public async Task Order_approved_wrong_payload_tenant_is_permanent()
    {
        await _postgres.ResetOutboxAsync();
        var context = await AcmeContextAsync();
        var seed = await SeedApprovedOrderAndMessageAsync(context.TenantId, context.BranchId, context.UserId, $"wh-wrong-tenant-{Guid.NewGuid():N}");
        using (var scope = _postgres.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var message = await db.OutboxMessages.SingleAsync(x => x.Id == seed.MessageId);
            message.PayloadJson = JsonSerializer.Serialize(new
            {
                tenantId = Guid.CreateVersion7(),
                orderId = seed.OrderId,
                branchId = context.BranchId,
                eventId = seed.MessageId,
                actorId = context.UserId,
                lines = new[] { new { orderLineId = seed.LineId, productId = seed.ProductId, quantity = seed.Quantity } },
            });
            await db.SaveChangesAsync();
        }

        (await ProcessOutboxAsync()).Should().Be(0);

        using var verifyScope = _postgres.CreateScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var delivery = await verify.EventHandlerDeliveries.AsNoTracking().SingleAsync(x => x.EventId == seed.MessageId);
        delivery.Status.Should().Be(EventHandlerDeliveryStatus.FailedPermanent);
    }

    [Fact]
    public async Task Order_approved_branch_mismatch_on_existing_task_is_permanent()
    {
        await _postgres.ResetOutboxAsync();
        var context = await AcmeContextAsync();
        var seed = await SeedApprovedOrderAndMessageAsync(context.TenantId, context.BranchId, context.UserId, $"wh-branch-{Guid.NewGuid():N}");
        using (var scope = _postgres.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
            var mismatchBranch = new Branch(ids.NewId(), context.TenantId, "Mismatch");
            db.Add(mismatchBranch);
            var taskId = ids.NewId();
            db.Add(new PickingTask(
                taskId,
                context.TenantId,
                mismatchBranch.Id,
                seed.OrderId,
                ids.NewId(),
                [new PickingLine(ids.NewId(), context.TenantId, taskId, seed.LineId, seed.ProductId, seed.Quantity)],
                DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        (await ProcessOutboxAsync()).Should().Be(0);

        using var verifyScope = _postgres.CreateScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var delivery = await verify.EventHandlerDeliveries.AsNoTracking().SingleAsync(x => x.EventId == seed.MessageId);
        delivery.Status.Should().Be(EventHandlerDeliveryStatus.FailedPermanent);
    }

    [Fact]
    public async Task Order_approved_concurrency_conflict_retries_then_succeeds()
    {
        await _postgres.ResetOutboxAsync();
        var context = await AcmeContextAsync();
        var seed = await SeedApprovedOrderAndMessageAsync(context.TenantId, context.BranchId, context.UserId, $"wh-concurrency-{Guid.NewGuid():N}");

        using (var first = _postgres.CreateScope(services =>
        {
            services.RemoveAll<IOrderFulfillmentApi>();
            services.AddSingleton<IOrderFulfillmentApi>(new ConcurrencyThenSuccessOrderFulfillmentApi());
        }))
        {
            var processor = first.ServiceProvider.GetRequiredService<IOutboxProcessor>();
            (await processor.ProcessBatchAsync("warehouse-concurrency-1", CancellationToken.None)).Should().Be(0);
        }

        using (var advance = _postgres.CreateScope())
        {
            var db = advance.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var delivery = await db.EventHandlerDeliveries.SingleAsync(x => x.EventId == seed.MessageId);
            delivery.NextAttemptAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        using (var second = _postgres.CreateScope(services =>
        {
            services.RemoveAll<IOrderFulfillmentApi>();
            services.AddSingleton<IOrderFulfillmentApi>(new ConcurrencyThenSuccessOrderFulfillmentApi(skipConflict: true));
        }))
        {
            var processor = second.ServiceProvider.GetRequiredService<IOutboxProcessor>();
            (await processor.ProcessBatchAsync("warehouse-concurrency-2", CancellationToken.None)).Should().Be(1);
        }

        using var verifyScope = _postgres.CreateScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var finalDelivery = await verify.EventHandlerDeliveries.AsNoTracking().SingleAsync(x => x.EventId == seed.MessageId);
        finalDelivery.Status.Should().Be(EventHandlerDeliveryStatus.Processed);
        (await verify.Set<PickingTask>().IgnoreQueryFilters().CountAsync(x => x.OrderId == seed.OrderId)).Should().Be(1);
    }

    [Fact]
    public async Task Complete_endpoint_marks_task_ready_for_delivery_route_and_emits_picking_completed()
    {
        await _postgres.ResetOutboxAsync();
        var tokens = await LoginAsync();
        var context = await AcmeContextAsync();
        var seed = await SeedPickingTaskAsync(context.TenantId, context.BranchId, context.UserId, $"wh-complete-{Guid.NewGuid():N}");

        var response = await SendAsync(HttpMethod.Post, $"/warehouse/picking-tasks/{seed.PickingTaskId}/complete", tokens.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var task = await db.Set<PickingTask>().IgnoreQueryFilters().Include(x => x.Lines).SingleAsync(x => x.Id == seed.PickingTaskId);
        var order = await db.Set<Order>().IgnoreQueryFilters().SingleAsync(x => x.Id == seed.OrderId);
        task.Status.Should().Be(PickingTaskStatus.Completed);
        task.Lines.Should().OnlyContain(line => line.PickedQuantity == line.Quantity);
        order.State.Should().Be(OrderState.ReadyForDeliveryRoute);
        (await OutboxCountAsync(db, context.TenantId, "PICKING_COMPLETED", seed.OrderId)).Should().Be(1);

        var duplicate = await SendAsync(HttpMethod.Post, $"/warehouse/picking-tasks/{seed.PickingTaskId}/complete", tokens.AccessToken);
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Complete_with_same_idempotency_key_returns_same_success()
    {
        await _postgres.ResetOutboxAsync();
        var tokens = await LoginAsync();
        var context = await AcmeContextAsync();
        var seed = await SeedPickingTaskAsync(context.TenantId, context.BranchId, context.UserId, $"wh-complete-idem-{Guid.NewGuid():N}");
        var key = $"complete-{Guid.NewGuid():N}";

        var first = await SendAsync(HttpMethod.Post, $"/warehouse/picking-tasks/{seed.PickingTaskId}/complete", tokens.AccessToken, key);
        var second = await SendAsync(HttpMethod.Post, $"/warehouse/picking-tasks/{seed.PickingTaskId}/complete", tokens.AccessToken, key);

        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        second.StatusCode.Should().Be(HttpStatusCode.OK, await second.Content.ReadAsStringAsync());
        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        (await OutboxCountAsync(db, context.TenantId, "PICKING_COMPLETED", seed.OrderId)).Should().Be(1);
    }

    [Fact]
    public async Task Complete_when_order_is_not_picking_rolls_back_task()
    {
        await _postgres.ResetOutboxAsync();
        var tokens = await LoginAsync();
        var context = await AcmeContextAsync();
        var seed = await SeedPickingTaskAsync(context.TenantId, context.BranchId, context.UserId, $"wh-complete-rollback-{Guid.NewGuid():N}", moveOrderToPicking: false);

        var response = await SendAsync(HttpMethod.Post, $"/warehouse/picking-tasks/{seed.PickingTaskId}/complete", tokens.AccessToken, $"rollback-{Guid.NewGuid():N}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var task = await db.Set<PickingTask>().IgnoreQueryFilters().Include(x => x.Lines).SingleAsync(x => x.Id == seed.PickingTaskId);
        task.Status.Should().Be(PickingTaskStatus.Pending);
        task.CompletionOperationKey.Should().BeNull();
        task.Lines.Should().OnlyContain(line => line.PickedQuantity == 0);
        (await OutboxCountAsync(db, context.TenantId, "PICKING_COMPLETED", seed.OrderId)).Should().Be(0);
    }

    [Fact]
    public async Task Concurrent_complete_allows_one_winner()
    {
        await _postgres.ResetOutboxAsync();
        var tokens = await LoginAsync();
        var context = await AcmeContextAsync();
        var seed = await SeedPickingTaskAsync(context.TenantId, context.BranchId, context.UserId, $"wh-concurrent-{Guid.NewGuid():N}");

        var first = SendAsync(HttpMethod.Post, $"/warehouse/picking-tasks/{seed.PickingTaskId}/complete", tokens.AccessToken);
        var second = SendAsync(HttpMethod.Post, $"/warehouse/picking-tasks/{seed.PickingTaskId}/complete", tokens.AccessToken);
        var responses = await Task.WhenAll(first, second);

        responses.Count(response => response.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Count(response => response.StatusCode == HttpStatusCode.Conflict).Should().Be(1);

        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        (await db.Set<PickingTask>().IgnoreQueryFilters().CountAsync(x => x.Id == seed.PickingTaskId && x.Status == PickingTaskStatus.Completed)).Should().Be(1);
        (await OutboxCountAsync(db, context.TenantId, "PICKING_COMPLETED", seed.OrderId)).Should().Be(1);
    }

    [Fact]
    public async Task Warehouse_http_is_tenant_isolated()
    {
        await _postgres.ResetOutboxAsync();
        var tokensA = await LoginAsync();
        var context = await AcmeContextAsync();
        var seed = await SeedPickingTaskAsync(context.TenantId, context.BranchId, context.UserId, $"wh-iso-{Guid.NewGuid():N}");
        var tokensB = await LoginOtherTenantAsync();

        var listA = await SendAsync(HttpMethod.Get, "/warehouse/picking-tasks?status=PENDING", tokensA.AccessToken);
        var listB = await SendAsync(HttpMethod.Get, "/warehouse/picking-tasks?status=PENDING", tokensB.AccessToken);
        var completeB = await SendAsync(HttpMethod.Post, $"/warehouse/picking-tasks/{seed.PickingTaskId}/complete", tokensB.AccessToken);

        listA.StatusCode.Should().Be(HttpStatusCode.OK);
        listB.StatusCode.Should().Be(HttpStatusCode.OK);
        using var docA = JsonDocument.Parse(await listA.Content.ReadAsStringAsync());
        using var docB = JsonDocument.Parse(await listB.Content.ReadAsStringAsync());
        docA.RootElement.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("id").GetGuid() == seed.PickingTaskId).Should().BeTrue();
        docB.RootElement.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("id").GetGuid() == seed.PickingTaskId).Should().BeFalse();
        completeB.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<int> ProcessOutboxAsync()
    {
        using var scope = _postgres.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();
        return await outbox.ProcessBatchAsync($"warehouse-worker-{Guid.NewGuid():N}", CancellationToken.None);
    }

    private async Task<(Guid OrderId, Guid LineId, string ProductId, int Quantity, Guid MessageId)> SeedApprovedOrderAndMessageAsync(
        Guid tenantId,
        Guid branchId,
        Guid actorId,
        string productId)
    {
        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var orderId = ids.NewId();
        var lineId = ids.NewId();
        var now = DateTimeOffset.UtcNow;
        var order = new Order(
            orderId,
            tenantId,
            branchId,
            $"cust-{productId}",
            "USD",
            "CASH",
            actorId,
            ids.NewId(),
            [new OrderLine(lineId, orderId, productId, "Widget", 2, 100)],
            now);
        db.Add(order);
        db.Add(order.Approve(ids.NewId(), actorId, null, now));
        var messageId = await SeedApprovedMessageAsync(db, ids, tenantId, branchId, actorId, orderId, lineId, productId, 2);
        await db.SaveChangesAsync();
        return (orderId, lineId, productId, 2, messageId);
    }

    private async Task<Guid> SeedApprovedMessageAsync(
        Guid tenantId,
        Guid branchId,
        Guid actorId,
        Guid orderId,
        Guid lineId,
        string productId,
        int quantity)
    {
        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var messageId = await SeedApprovedMessageAsync(db, ids, tenantId, branchId, actorId, orderId, lineId, productId, quantity);
        await db.SaveChangesAsync();
        return messageId;
    }

    private static Task<Guid> SeedApprovedMessageAsync(
        BinexusDbContext db,
        IIdGenerator ids,
        Guid tenantId,
        Guid branchId,
        Guid actorId,
        Guid orderId,
        Guid lineId,
        string productId,
        int quantity)
    {
        var messageId = ids.NewId();
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = messageId,
            TenantId = tenantId,
            EventName = "ORDER_APPROVED",
            PayloadJson = JsonSerializer.Serialize(new
            {
                tenantId,
                orderId,
                branchId,
                eventId = messageId,
                actorId,
                lines = new[] { new { orderLineId = lineId, productId, quantity } },
            }),
            OccurredAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        return Task.FromResult(messageId);
    }

    private async Task<(Guid OrderId, Guid PickingTaskId)> SeedPickingTaskAsync(Guid tenantId, Guid branchId, Guid actorId, string productId, bool moveOrderToPicking = true)
    {
        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var orderId = ids.NewId();
        var lineId = ids.NewId();
        var taskId = ids.NewId();
        var now = DateTimeOffset.UtcNow;
        var order = new Order(
            orderId,
            tenantId,
            branchId,
            $"cust-{productId}",
            "USD",
            "CASH",
            actorId,
            ids.NewId(),
            [new OrderLine(lineId, orderId, productId, "Widget", 1, 100)],
            now);
        db.Add(order);
        db.Add(order.Approve(ids.NewId(), actorId, null, now));
        if (moveOrderToPicking)
        {
            db.Add(order.MoveToPicking(ids.NewId(), actorId, null, now));
        }
        db.Add(new PickingTask(
            taskId,
            tenantId,
            branchId,
            orderId,
            ids.NewId(),
            [new PickingLine(ids.NewId(), tenantId, taskId, lineId, productId, 1)],
            now));
        await db.SaveChangesAsync();
        return (orderId, taskId);
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

    private static async Task<int> OutboxCountAsync(BinexusDbContext db, Guid tenantId, string eventName, Guid orderId)
    {
        var payloads = await db.OutboxMessages
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.EventName == eventName)
            .Select(x => x.PayloadJson)
            .ToListAsync();
        return payloads.Count(x => x.Contains(orderId.ToString(), StringComparison.OrdinalIgnoreCase));
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
        var slug = "warehouse-other-" + Guid.NewGuid().ToString("N")[..8];
        const string email = "admin@warehouse-other.test";
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

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string accessToken, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        return await _client.SendAsync(request);
    }

    private sealed class FailingOrderFulfillmentApi : IOrderFulfillmentApi
    {
        public Task<OrderFulfillmentResult> MoveToPickingAsync(OrderFulfillmentRequest request, CancellationToken ct) =>
            throw new InvalidOperationException("boom");

        public Task<OrderFulfillmentResult> MarkReadyForDeliveryRouteAsync(OrderFulfillmentRequest request, CancellationToken ct) =>
            throw new InvalidOperationException("boom");
        public Task<OrderFulfillmentResult> MarkOutForDeliveryAsync(OrderFulfillmentRequest request, CancellationToken ct) =>
            throw new InvalidOperationException("boom");

        public Task<OrderFulfillmentResult> MarkOutForDeliveryAsync(OrderFulfillmentBatchRequest request, CancellationToken ct) =>
            throw new InvalidOperationException("boom");

        public Task<OrderFulfillmentResult> MarkDeliveredAsync(OrderFulfillmentRequest request, CancellationToken ct) =>
            throw new InvalidOperationException("boom");

        public Task<OrderFulfillmentResult> MarkDeliveryAttemptFailedAsync(OrderFulfillmentRequest request, CancellationToken ct) =>
            throw new InvalidOperationException("boom");

        public Task<OrderFulfillmentResult> SettleCodOrdersAsync(SettleCodOrdersRequest request, CancellationToken ct) =>
            throw new InvalidOperationException("boom");

        public Task<CashCollectionFactsResult> GetCashCollectionFactsAsync(Guid tenantId, IReadOnlyList<Guid> orderIds, CancellationToken ct) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class ConcurrencyThenSuccessOrderFulfillmentApi(bool skipConflict = false) : IOrderFulfillmentApi
    {
        public Task<OrderFulfillmentResult> MoveToPickingAsync(OrderFulfillmentRequest request, CancellationToken ct) =>
            Task.FromResult(skipConflict
                ? new OrderFulfillmentResult(OrderFulfillmentOutcome.Success)
                : new OrderFulfillmentResult(OrderFulfillmentOutcome.ConcurrencyConflict, "ORDER_CONCURRENCY_CONFLICT", "try again"));

        public Task<OrderFulfillmentResult> MarkReadyForDeliveryRouteAsync(OrderFulfillmentRequest request, CancellationToken ct) =>
            Task.FromResult(new OrderFulfillmentResult(OrderFulfillmentOutcome.Success));
        public Task<OrderFulfillmentResult> MarkOutForDeliveryAsync(OrderFulfillmentRequest request, CancellationToken ct) =>
            Task.FromResult(new OrderFulfillmentResult(OrderFulfillmentOutcome.Success));

        public Task<OrderFulfillmentResult> MarkOutForDeliveryAsync(OrderFulfillmentBatchRequest request, CancellationToken ct) =>
            Task.FromResult(new OrderFulfillmentResult(OrderFulfillmentOutcome.Success));

        public Task<OrderFulfillmentResult> MarkDeliveredAsync(OrderFulfillmentRequest request, CancellationToken ct) =>
            Task.FromResult(new OrderFulfillmentResult(OrderFulfillmentOutcome.Success));

        public Task<OrderFulfillmentResult> MarkDeliveryAttemptFailedAsync(OrderFulfillmentRequest request, CancellationToken ct) =>
            Task.FromResult(new OrderFulfillmentResult(OrderFulfillmentOutcome.Success));

        public Task<OrderFulfillmentResult> SettleCodOrdersAsync(SettleCodOrdersRequest request, CancellationToken ct) =>
            Task.FromResult(new OrderFulfillmentResult(OrderFulfillmentOutcome.Success));

        public Task<CashCollectionFactsResult> GetCashCollectionFactsAsync(Guid tenantId, IReadOnlyList<Guid> orderIds, CancellationToken ct) =>
            Task.FromResult(new CashCollectionFactsResult([], []));
    }
}
