using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Binexus.IntegrationTests.Infrastructure;
using Binexus.Modules.Identity.Application;
using Binexus.Modules.Identity.Domain;
using Binexus.Modules.Inventory.Application;
using Binexus.Modules.Inventory.Contracts;
using Binexus.Modules.Inventory.Domain;
using Binexus.Platform.Ids;
using Binexus.Platform.Persistence;
using Binexus.Platform.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Binexus.IntegrationTests.Inventory;

public sealed class InventoryFlowTests : IClassFixture<PostgresTestFixture>, IClassFixture<CloudApiFactory>
{
    private const string SigningKey = "identity-integration-signing-key-with-more-than-thirty-two-bytes";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly PostgresTestFixture _postgres;
    private readonly HttpClient _client;

    public InventoryFlowTests(PostgresTestFixture postgres, CloudApiFactory factory)
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
    public async Task Adjust_list_transfer_receive_cancel_and_idempotent_adjust()
    {
        var login = await LoginAsync();
        login.AccessToken.Should().NotBeNullOrWhiteSpace();
        var me = await SendAuthorizedAsync(HttpMethod.Get, "/auth/me", login.AccessToken);
        me.StatusCode.Should().Be(HttpStatusCode.OK, await me.Content.ReadAsStringAsync());

        var (tenantId, sourceBranchId, destinationBranchId) = await EnsureBranchesAsync();
        var productId = $"sku-{Guid.NewGuid():N}";

        var adjust = await SendAuthorizedAsync(
            HttpMethod.Post,
            "/inventory/stock/adjust",
            login.AccessToken,
            new { branchId = sourceBranchId, productId, delta = 10, reason = "initial stock", operationKey = $"adj-{productId}" });
        adjust.StatusCode.Should().Be(HttpStatusCode.OK, await adjust.Content.ReadAsStringAsync());
        var adjustBody = await adjust.Content.ReadFromJsonAsync<AdjustStockResult>(JsonOptions);
        adjustBody!.StockItem.OnHand.Should().Be(10);
        adjustBody.StockItem.Available.Should().Be(10);

        var adjustAgain = await SendAuthorizedAsync(
            HttpMethod.Post,
            "/inventory/stock/adjust",
            login.AccessToken,
            new { branchId = sourceBranchId, productId, delta = 10, reason = "initial stock", operationKey = $"adj-{productId}" });
        adjustAgain.StatusCode.Should().Be(HttpStatusCode.OK);
        var adjustAgainBody = await adjustAgain.Content.ReadFromJsonAsync<AdjustStockResult>(JsonOptions);
        adjustAgainBody!.MovementId.Should().Be(adjustBody.MovementId);
        adjustAgainBody.StockItem.OnHand.Should().Be(10);

        var list = await SendAuthorizedAsync(HttpMethod.Get, $"/inventory/stock?branchId={sourceBranchId}&productId={productId}", login.AccessToken);
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var listBody = await list.Content.ReadFromJsonAsync<ListStockItemsResult>(JsonOptions);
        listBody!.Items.Should().ContainSingle(x => x.ProductId == productId && x.OnHand == 10);

        var createTransfer = await SendAuthorizedAsync(
            HttpMethod.Post,
            "/inventory/stock/transfers",
            login.AccessToken,
            new
            {
                sourceBranchId,
                destinationBranchId,
                productId,
                quantity = 4,
                reason = "move stock",
            });
        createTransfer.StatusCode.Should().Be(HttpStatusCode.OK);
        using var createDoc = JsonDocument.Parse(await createTransfer.Content.ReadAsStringAsync());
        var transferId = createDoc.RootElement.GetProperty("transfer").GetProperty("id").GetGuid();
        createDoc.RootElement.GetProperty("transfer").GetProperty("status").GetString().Should().Be("PENDING");

        var afterReserve = await GetStockAsync(login.AccessToken, sourceBranchId, productId);
        afterReserve.OnHand.Should().Be(10);
        afterReserve.Reserved.Should().Be(4);
        afterReserve.Available.Should().Be(6);

        var receive = await SendAuthorizedAsync(
            HttpMethod.Post,
            $"/inventory/stock/transfers/{transferId}/receive",
            login.AccessToken);
        receive.StatusCode.Should().Be(HttpStatusCode.OK);

        var sourceAfterReceive = await GetStockAsync(login.AccessToken, sourceBranchId, productId);
        sourceAfterReceive.OnHand.Should().Be(6);
        sourceAfterReceive.Reserved.Should().Be(0);
        var destAfterReceive = await GetStockAsync(login.AccessToken, destinationBranchId, productId);
        destAfterReceive.OnHand.Should().Be(4);

        var receiveAgain = await SendAuthorizedAsync(
            HttpMethod.Post,
            $"/inventory/stock/transfers/{transferId}/receive",
            login.AccessToken);
        receiveAgain.StatusCode.Should().Be(HttpStatusCode.OK);

        var cancelProduct = $"sku-cancel-{Guid.NewGuid():N}";
        await SendAuthorizedAsync(
            HttpMethod.Post,
            "/inventory/stock/adjust",
            login.AccessToken,
            new { branchId = sourceBranchId, productId = cancelProduct, delta = 5, reason = "cancel seed" });
        var pending = await SendAuthorizedAsync(
            HttpMethod.Post,
            "/inventory/stock/transfers",
            login.AccessToken,
            new { sourceBranchId, destinationBranchId, productId = cancelProduct, quantity = 2, reason = "will cancel" });
        using var pendingDoc = JsonDocument.Parse(await pending.Content.ReadAsStringAsync());
        var pendingId = pendingDoc.RootElement.GetProperty("transfer").GetProperty("id").GetGuid();
        var cancel = await SendAuthorizedAsync(
            HttpMethod.Post,
            $"/inventory/stock/transfers/{pendingId}/cancel",
            login.AccessToken);
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterCancel = await GetStockAsync(login.AccessToken, sourceBranchId, cancelProduct);
        afterCancel.Reserved.Should().Be(0);
        afterCancel.OnHand.Should().Be(5);

        _ = tenantId;
    }

    [Fact]
    public async Task Reservation_api_is_atomic_idempotent_and_writes_outbox()
    {
        var (tenantId, branchId, _) = await EnsureBranchesAsync();
        var productId = $"sku-res-{Guid.NewGuid():N}";
        await SeedStockAsync(tenantId, branchId, productId, onHand: 5);

        using var scope = _postgres.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        tenant.SetContext(new TenantContext(tenantId, Guid.NewGuid(), RoleNames.Admin, branchId, "test"));
        var api = scope.ServiceProvider.GetRequiredService<IInventoryReservationApi>();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();

        InventoryReservationResult? first = null;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            first = await api.TryReserveForOrderAsync(
                new InventoryReserveForOrderRequest(
                    tenantId,
                    orderId,
                    [new InventoryReservationLine(branchId, lineId, productId, 3)]),
                CancellationToken.None);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        });
        first!.Succeeded.Should().BeTrue();

        var second = await api.TryReserveForOrderAsync(
            new InventoryReserveForOrderRequest(
                tenantId,
                orderId,
                [new InventoryReservationLine(branchId, lineId, productId, 3)]),
            CancellationToken.None);
        second.Succeeded.Should().BeTrue();

        var item = await db.Set<StockItem>().SingleAsync(x => x.TenantId == tenantId && x.ProductId == productId);
        item.Reserved.Should().Be(3);
        (await db.OutboxMessages.CountAsync(x => x.EventName == "INVENTORY_RESERVED" && x.TenantId == tenantId))
            .Should().BeGreaterThanOrEqualTo(1);

        var failOrder = Guid.NewGuid();
        var fail = await api.TryReserveForOrderAsync(
            new InventoryReserveForOrderRequest(
                tenantId,
                failOrder,
                [new InventoryReservationLine(branchId, Guid.NewGuid(), productId, 10)]),
            CancellationToken.None);
        fail.Succeeded.Should().BeFalse();
        fail.FailureCode.Should().Be(InventoryError.InsufficientStock);
        item = await db.Set<StockItem>().SingleAsync(x => x.TenantId == tenantId && x.ProductId == productId);
        item.Reserved.Should().Be(3);
        (await db.Set<StockReservation>().CountAsync(x => x.OrderId == failOrder && x.Status == StockReservationStatus.Failed))
            .Should().Be(0);
        (await db.OutboxMessages.AnyAsync(x => x.EventName == "INVENTORY_RESERVATION_FAILED" && x.TenantId == tenantId))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Sale_api_respects_reservations_and_is_idempotent()
    {
        var (tenantId, branchId, _) = await EnsureBranchesAsync();
        var productId = $"sku-sale-{Guid.NewGuid():N}";
        await SeedStockAsync(tenantId, branchId, productId, onHand: 5);

        using var scope = _postgres.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        tenant.SetContext(new TenantContext(tenantId, Guid.NewGuid(), RoleNames.Admin, branchId, "test"));
        var reservationApi = scope.ServiceProvider.GetRequiredService<IInventoryReservationApi>();
        var saleApi = scope.ServiceProvider.GetRequiredService<IInventorySaleApi>();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();

        var saleId = Guid.NewGuid();
        var saleLineId = Guid.NewGuid();
        InventorySaleDecrementResult? oversell = null;
        InventorySaleDecrementResult? ok = null;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await reservationApi.TryReserveForOrderAsync(
                new InventoryReserveForOrderRequest(
                    tenantId,
                    Guid.NewGuid(),
                    [new InventoryReservationLine(branchId, Guid.NewGuid(), productId, 3)]),
                CancellationToken.None);
            oversell = await saleApi.DecrementForSaleAsync(
                new InventorySaleDecrementRequest(
                    tenantId,
                    saleId,
                    [new InventorySaleLine(branchId, saleLineId, productId, 3)]),
                CancellationToken.None);
            ok = await saleApi.DecrementForSaleAsync(
                new InventorySaleDecrementRequest(
                    tenantId,
                    saleId,
                    [new InventorySaleLine(branchId, saleLineId, productId, 2)]),
                CancellationToken.None);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        });
        oversell!.Succeeded.Should().BeFalse();
        ok!.Succeeded.Should().BeTrue();

        var again = await saleApi.DecrementForSaleAsync(
            new InventorySaleDecrementRequest(
                tenantId,
                saleId,
                [new InventorySaleLine(branchId, saleLineId, productId, 2)]),
            CancellationToken.None);
        again.Succeeded.Should().BeTrue();

        var item = await db.Set<StockItem>().SingleAsync(x => x.TenantId == tenantId && x.ProductId == productId);
        item.OnHand.Should().Be(3);
        item.Reserved.Should().Be(3);
        (await db.Set<StockMovement>().CountAsync(x => x.Type == StockMovementType.Sale && x.ProductId == productId))
            .Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_sale_of_last_unit_allows_only_one_winner()
    {
        var (tenantId, branchId, _) = await EnsureBranchesAsync();
        var productId = $"sku-race-{Guid.NewGuid():N}";
        await SeedStockAsync(tenantId, branchId, productId, onHand: 1);

        async Task<bool> AttemptAsync(Guid saleId)
        {
            using var scope = _postgres.CreateScope();
            var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            tenant.SetContext(new TenantContext(tenantId, Guid.NewGuid(), RoleNames.Admin, branchId, "race"));
            var saleApi = scope.ServiceProvider.GetRequiredService<IInventorySaleApi>();
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            try
            {
                var succeeded = false;
                await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    await using var transaction = await db.Database.BeginTransactionAsync();
                    var result = await saleApi.DecrementForSaleAsync(
                        new InventorySaleDecrementRequest(
                            tenantId,
                            saleId,
                            [new InventorySaleLine(branchId, Guid.NewGuid(), productId, 1)]),
                        CancellationToken.None);
                    if (!result.Succeeded)
                    {
                        return;
                    }

                    await db.SaveChangesAsync();
                    await transaction.CommitAsync();
                    succeeded = true;
                });
                return succeeded;
            }
            catch (DbUpdateConcurrencyException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(AttemptAsync(Guid.NewGuid()), AttemptAsync(Guid.NewGuid()));
        results.Count(x => x).Should().Be(1);

        using var verify = _postgres.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var item = await db.Set<StockItem>().IgnoreQueryFilters()
            .SingleAsync(x => x.TenantId == tenantId && x.ProductId == productId);
        item.OnHand.Should().Be(0);
    }

    [Fact]
    public async Task Tenant_isolation_prevents_cross_tenant_stock_reads()
    {
        var login = await LoginAsync();
        var (tenantId, branchId, _) = await EnsureBranchesAsync();
        var productId = $"sku-iso-{Guid.NewGuid():N}";
        await SeedStockAsync(tenantId, branchId, productId, onHand: 7);

        using (var scope = _postgres.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
            var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
            var otherTenant = new Tenant(ids.NewId(), "other", "Other", DateTimeOffset.UtcNow);
            var otherBranch = new Branch(ids.NewId(), otherTenant.Id, "Other Main");
            db.Add(otherTenant);
            db.Add(otherBranch);
            db.Add(new StockItem(ids.NewId(), otherTenant.Id, otherBranch.Id, productId, 99, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        var list = await SendAuthorizedAsync(
            HttpMethod.Get,
            $"/inventory/stock?productId={productId}",
            login.AccessToken);
        var body = await list.Content.ReadFromJsonAsync<ListStockItemsResult>(JsonOptions);
        body!.Items.Should().OnlyContain(x => x.OnHand == 7);
        body.Items.Should().NotContain(x => x.OnHand == 99);
    }

    private async Task<AuthTokens> LoginAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/auth/login",
            new LoginRequest("acme", "admin@acme.test", IdentitySeedDefaults.KnownInsecureDemoPassword));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<AuthTokens>(JsonOptions))!;
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(
        HttpMethod method,
        string url,
        string accessToken,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request);
    }

    private async Task<StockItemSummary> GetStockAsync(string accessToken, Guid branchId, string productId)
    {
        var list = await SendAuthorizedAsync(
            HttpMethod.Get,
            $"/inventory/stock?branchId={branchId}&productId={productId}",
            accessToken);
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await list.Content.ReadFromJsonAsync<ListStockItemsResult>(JsonOptions);
        return body!.Items.Single();
    }

    private async Task<(Guid TenantId, Guid SourceBranchId, Guid DestinationBranchId)> EnsureBranchesAsync()
    {
        using var scope = _postgres.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BinexusDbContext>();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var tenant = await db.Set<Tenant>().IgnoreQueryFilters().SingleAsync(x => x.Slug == "acme");
        var branches = await db.Set<Branch>().IgnoreQueryFilters().Where(x => x.TenantId == tenant.Id).ToListAsync();
        var source = branches.Single(x => x.Name == "Main");
        var destination = branches.FirstOrDefault(x => x.Name == "Secondary");
        if (destination is null)
        {
            destination = new Branch(ids.NewId(), tenant.Id, "Secondary");
            db.Add(destination);
            await db.SaveChangesAsync();
        }

        return (tenant.Id, source.Id, destination.Id);
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
