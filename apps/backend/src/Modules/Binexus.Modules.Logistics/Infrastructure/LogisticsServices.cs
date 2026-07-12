using System.Text.Json;
using Binexus.Platform.Features.Contracts;
using Binexus.Modules.Logistics.Application;
using Binexus.Modules.Logistics.Domain;
using Binexus.Modules.Orders.Contracts;
using Binexus.Platform.Dispatching;
using Binexus.Platform.Ids;
using Binexus.Platform.Messaging;
using Binexus.Platform.Persistence;
using Binexus.Platform.Tenancy;
using Binexus.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static Binexus.Modules.Logistics.Infrastructure.LogisticsCommandSupport;

namespace Binexus.Modules.Logistics.Infrastructure;

public sealed class LogisticsQueryService(BinexusDbContext db, ICurrentTenant currentTenant) : ILogisticsQueryService
{
    public Task<Result<ListDeliveryRouteCandidatesResult>> ListCandidatesAsync(ListDeliveryRouteCandidatesQuery query, CancellationToken ct) =>
        Capture(() => ListCandidatesCoreAsync(query, ct));

    public Task<Result<ListDeliveryRoutesResult>> ListRoutesAsync(ListDeliveryRoutesQuery query, CancellationToken ct) =>
        Capture(() => ListRoutesCoreAsync(query, ct));

    public Task<Result<DeliveryRouteSummary>> GetRouteAsync(Guid deliveryRouteId, CancellationToken ct) =>
        Capture(() => GetRouteCoreAsync(deliveryRouteId, ct));

    public Task<Result<DeliveryRouteSummary>> GetRouteByCreationOperationKeyAsync(string operationKey, CancellationToken ct) =>
        Capture(() => GetRouteByCreationOperationKeyCoreAsync(operationKey, ct));

    public Task<Result<ListDeliveryRouteStopsResult>> ListStopsAsync(Guid deliveryRouteId, CancellationToken ct) =>
        Capture(() => ListStopsCoreAsync(deliveryRouteId, ct));

    public Task<Result<DeliveryRouteStopSummary>> GetStopAsync(Guid deliveryRouteStopId, CancellationToken ct) =>
        Capture(() => GetStopCoreAsync(deliveryRouteStopId, ct));

    private async Task<ListDeliveryRouteCandidatesResult> ListCandidatesCoreAsync(ListDeliveryRouteCandidatesQuery query, CancellationToken ct)
    {
        var tenantId = RequireTenant(currentTenant);
        var limit = Math.Clamp(query.Limit ?? 50, 1, 100);
        var source = db.Set<DeliveryRouteCandidate>().AsNoTracking().Where(x => x.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            source = source.Where(x => x.Status == LogisticsPersistedEnums.ParseCandidateStatus(query.Status.Trim().ToUpperInvariant()));
        }

        if (query.BranchId is { } branchId)
        {
            source = source.Where(x => x.BranchId == branchId);
        }

        source = await ApplyCursorAsync(source, query.Cursor, tenantId, db.Set<DeliveryRouteCandidate>(), ct);
        var rows = await source.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).Take(limit + 1).ToListAsync(ct);
        var items = rows.Take(limit).Select(ToSummary).ToArray();
        return new(items, rows.Count > limit ? items[^1].Id.ToString() : null);
    }

    private async Task<ListDeliveryRoutesResult> ListRoutesCoreAsync(ListDeliveryRoutesQuery query, CancellationToken ct)
    {
        var tenantId = RequireTenant(currentTenant);
        var limit = Math.Clamp(query.Limit ?? 50, 1, 100);
        var source = db.Set<DeliveryRoute>().AsNoTracking().Include(x => x.Stops).Where(x => x.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            source = source.Where(x => x.Status == LogisticsPersistedEnums.ParseRouteStatus(query.Status.Trim().ToUpperInvariant()));
        }

        if (query.BranchId is { } branchId)
        {
            source = source.Where(x => x.BranchId == branchId);
        }

        source = await ApplyCursorAsync(source, query.Cursor, tenantId, db.Set<DeliveryRoute>(), ct);
        var rows = await source.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).Take(limit + 1).ToListAsync(ct);
        var items = rows.Take(limit).Select(ToSummary).ToArray();
        return new(items, rows.Count > limit ? items[^1].Id.ToString() : null);
    }

    private async Task<DeliveryRouteSummary> GetRouteCoreAsync(Guid deliveryRouteId, CancellationToken ct)
    {
        var tenantId = RequireTenant(currentTenant);
        var route = await db.Set<DeliveryRoute>().AsNoTracking().Include(x => x.Stops).SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == deliveryRouteId, ct)
            ?? throw new LogisticsDomainException(LogisticsError.DeliveryRouteNotFound, "Delivery route not found.");
        return ToSummary(route);
    }

    private async Task<DeliveryRouteSummary> GetRouteByCreationOperationKeyCoreAsync(string operationKey, CancellationToken ct)
    {
        var tenantId = RequireTenant(currentTenant);
        var prefixed = OperationKey("logistics-route-create", tenantId, operationKey);
        var route = await db.Set<DeliveryRoute>().AsNoTracking().Include(x => x.Stops).SingleOrDefaultAsync(x => x.TenantId == tenantId && x.CreationOperationKey == prefixed, ct)
            ?? throw new LogisticsDomainException(LogisticsError.DeliveryRouteNotFound, "Delivery route not found.");
        return ToSummary(route);
    }

    private async Task<ListDeliveryRouteStopsResult> ListStopsCoreAsync(Guid deliveryRouteId, CancellationToken ct)
    {
        var tenantId = RequireTenant(currentTenant);
        var routeExists = await db.Set<DeliveryRoute>().AsNoTracking().AnyAsync(x => x.TenantId == tenantId && x.Id == deliveryRouteId, ct);
        if (!routeExists)
        {
            throw new LogisticsDomainException(LogisticsError.DeliveryRouteNotFound, "Delivery route not found.");
        }

        var stops = await db.Set<DeliveryRouteStop>().AsNoTracking().Where(x => x.TenantId == tenantId && x.DeliveryRouteId == deliveryRouteId).OrderBy(x => x.Sequence).ToListAsync(ct);
        return new(stops.Select(ToSummary).ToArray());
    }

    private async Task<DeliveryRouteStopSummary> GetStopCoreAsync(Guid deliveryRouteStopId, CancellationToken ct)
    {
        var tenantId = RequireTenant(currentTenant);
        var stop = await db.Set<DeliveryRouteStop>().AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == deliveryRouteStopId, ct)
            ?? throw new LogisticsDomainException(LogisticsError.DeliveryStopNotFound, "Delivery route stop not found.");
        return ToSummary(stop);
    }

    private static async Task<IQueryable<T>> ApplyCursorAsync<T>(IQueryable<T> source, string? cursor, Guid tenantId, DbSet<T> set, CancellationToken ct)
        where T : class, Binexus.SharedKernel.Abstractions.ITenantScoped
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return source;
        }

        if (!Guid.TryParse(cursor, out var cursorId))
        {
            throw new LogisticsDomainException(LogisticsError.InvalidCursor, "Invalid cursor.");
        }

        var cursorRow = await set.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && EF.Property<Guid>(x, "Id") == cursorId, ct)
            ?? throw new LogisticsDomainException(LogisticsError.InvalidCursor, "Invalid cursor.");
        var createdAt = EF.Property<DateTimeOffset>(cursorRow, "CreatedAtUtc");
        return source.Where(x => EF.Property<DateTimeOffset>(x, "CreatedAtUtc") < createdAt
            || (EF.Property<DateTimeOffset>(x, "CreatedAtUtc") == createdAt && EF.Property<Guid>(x, "Id").CompareTo(cursorId) < 0));
    }

    private static async Task<Result<T>> Capture<T>(Func<Task<T>> action)
    {
        try
        {
            return ResultFactory.Ok(await action());
        }
        catch (LogisticsDomainException ex)
        {
            return ResultFactory.Fail<T>(LogisticsErrorMapping.ToDomainError(ex));
        }
    }

    internal static DeliveryRouteCandidateSummary ToSummary(DeliveryRouteCandidate candidate) =>
        new(candidate.Id, candidate.BranchId, candidate.OrderId, LogisticsPersistedEnums.ToApi(candidate.Status), candidate.DeliveryRouteId, candidate.CreatedAtUtc, candidate.UpdatedAtUtc);

    internal static DeliveryRouteSummary ToSummary(DeliveryRoute route) =>
        new(route.Id, route.BranchId, LogisticsPersistedEnums.ToApi(route.Status), route.DriverUserId, route.PlannedDate, route.DispatchedAtUtc, route.CompletedAtUtc, route.CreatedAtUtc, route.UpdatedAtUtc, route.Stops.Count);

    internal static DeliveryRouteStopSummary ToSummary(DeliveryRouteStop stop) =>
        new(stop.Id, stop.DeliveryRouteId, stop.OrderId, stop.Sequence, LogisticsPersistedEnums.ToApi(stop.Status), stop.FailureReason is null ? null : LogisticsPersistedEnums.ToApi(stop.FailureReason.Value), stop.FailureNotes, stop.DeliveredAtUtc, stop.FailedAtUtc);
}

public sealed class CreateDeliveryRouteHandler(BinexusDbContext db, ICurrentTenant currentTenant, IIdGenerator ids, TimeProvider clock) : ICommandHandler<CreateDeliveryRouteCommand>
{
    public Task<Result> HandleAsync(CreateDeliveryRouteCommand command, CancellationToken cancellationToken) => Capture(async () =>
    {
        var context = Require(currentTenant);
        var operationKey = OperationKey("logistics-route-create", context.TenantId, command.OperationKey);
        if (await db.Set<DeliveryRoute>().AnyAsync(x => x.TenantId == context.TenantId && x.CreationOperationKey == operationKey, cancellationToken))
        {
            return Result.Success();
        }

        var branchId = command.Request.BranchId ?? context.BranchId ?? throw new LogisticsDomainException(LogisticsError.InvalidDeliveryRoute, "branchId is required.");
        var route = new DeliveryRoute(command.DeliveryRouteId, context.TenantId, branchId, command.Request.PlannedDate, clock.GetUtcNow(), operationKey);
        db.Add(route);
        Record(db, ids, context.TenantId, "DELIVERY_ROUTE_CREATED", new { deliveryRouteId = route.Id, branchId = route.BranchId }, command.CorrelationId, clock);
        return Result.Success();
    });
}

public sealed class AssignOrdersToDeliveryRouteHandler(BinexusDbContext db, ICurrentTenant currentTenant, IIdGenerator ids, TimeProvider clock) : ICommandHandler<AssignOrdersToDeliveryRouteCommand>
{
    public Task<Result> HandleAsync(AssignOrdersToDeliveryRouteCommand command, CancellationToken cancellationToken) => Capture(async () =>
    {
        var context = Require(currentTenant);
        var route = await LoadRouteAsync(db, context.TenantId, command.DeliveryRouteId, cancellationToken);
        var operationKey = OperationKey("logistics-route-assign", context.TenantId, command.OperationKey);
        if (route.AssignOperationKey == operationKey)
        {
            return Result.Success();
        }

        var orderIds = command.Request.OrderIds.Distinct().ToArray();
        if (orderIds.Length == 0)
        {
            throw new LogisticsDomainException(LogisticsError.InvalidDeliveryRoute, "At least one order is required.");
        }

        var candidates = await db.Set<DeliveryRouteCandidate>()
            .Where(x => x.TenantId == context.TenantId && orderIds.Contains(x.OrderId))
            .ToListAsync(cancellationToken);
        if (candidates.Count != orderIds.Length)
        {
            throw new LogisticsDomainException(LogisticsError.CandidateNotFound, "One or more route candidates were not found.");
        }

        var nextSequence = route.Stops.Count == 0 ? 1 : route.Stops.Max(x => x.Sequence) + 1;
        var now = clock.GetUtcNow();
        foreach (var candidate in candidates.OrderBy(x => Array.IndexOf(orderIds, x.OrderId)))
        {
            if (candidate.BranchId != route.BranchId)
            {
                throw new LogisticsDomainException(LogisticsError.BranchMismatch, "Candidate branch must match route branch.");
            }

            candidate.Assign(route.Id, now);
            if (route.Stops.Any(x => x.OrderId == candidate.OrderId))
            {
                throw new LogisticsDomainException(LogisticsError.OrderAlreadyAssigned, "Order is already assigned to this route.");
            }

            db.Add(new DeliveryRouteStop(ids.NewId(), context.TenantId, route.BranchId, route.Id, candidate.OrderId, nextSequence++, now));
        }

        route.RecordAssignment(now, operationKey);
        Record(db, ids, context.TenantId, "DELIVERY_ROUTE_ASSIGNED", new { deliveryRouteId = route.Id, orderIds }, command.CorrelationId, clock);
        return Result.Success();
    });
}

public sealed class DispatchDeliveryRouteHandler(BinexusDbContext db, ICurrentTenant currentTenant, IOrderFulfillmentApi orders, IIdGenerator ids, TimeProvider clock) : ICommandHandler<DispatchDeliveryRouteCommand>
{
    public Task<Result> HandleAsync(DispatchDeliveryRouteCommand command, CancellationToken cancellationToken) => Capture(async () =>
    {
        var context = Require(currentTenant);
        var route = await LoadRouteAsync(db, context.TenantId, command.DeliveryRouteId, cancellationToken);
        var operationKey = OperationKey("logistics-route-dispatch", context.TenantId, command.OperationKey);
        // Same key → idempotent. Different keys when already DISPATCHED → success no-op (skip MarkOutForDelivery).
        if (route.DispatchOperationKey == operationKey || route.Status == DeliveryRouteStatus.Dispatched)
        {
            return Result.Success();
        }

        route.Dispatch(command.Request.DriverUserId, clock.GetUtcNow(), operationKey);
        var orderIds = route.Stops.OrderBy(x => x.Sequence).Select(x => x.OrderId).ToArray();
        var moved = await orders.MarkOutForDeliveryAsync(new OrderFulfillmentBatchRequest(
            context.TenantId,
            orderIds,
            context.UserId,
            ParseCorrelationId(command.CorrelationId),
            ids.NewId(),
            "delivery route dispatched",
            "logistics.dispatch-route"), cancellationToken);
        if (moved.Outcome is OrderFulfillmentOutcome.NotFound or OrderFulfillmentOutcome.NoLongerApplicable or OrderFulfillmentOutcome.ConcurrencyConflict)
        {
            return Result.Failure(DomainError.Conflict(moved.Code ?? "ORDER_FULFILLMENT_REJECTED", moved.Message ?? "Orders rejected route dispatch."));
        }

        Record(db, ids, context.TenantId, "DELIVERY_ROUTE_DISPATCHED", new { deliveryRouteId = route.Id, orderIds, driverUserId = route.DriverUserId }, command.CorrelationId, clock);
        return Result.Success();
    });
}

public sealed class LogisticsProofUploadService(
    BinexusDbContext db,
    ICurrentTenant currentTenant,
    IObjectStorage storage,
    IIdGenerator ids,
    IOptions<LogisticsStorageOptions> options,
    TimeProvider clock) : ILogisticsProofUploadService
{
    public async Task<Result<DeliveryProofUploadResult>> CreateAsync(
        Guid deliveryRouteStopId,
        CreateDeliveryProofUploadRequest request,
        string? operationKey,
        CancellationToken ct)
    {
        try
        {
            var context = Require(currentTenant);
            var stop = await db.Set<DeliveryRouteStop>().AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == context.TenantId && x.Id == deliveryRouteStopId, ct)
                ?? throw new LogisticsDomainException(LogisticsError.DeliveryStopNotFound, "Delivery route stop not found.");
            var route = await db.Set<DeliveryRoute>().AsNoTracking().SingleAsync(x => x.TenantId == context.TenantId && x.Id == stop.DeliveryRouteId, ct);
            if (route.Status != DeliveryRouteStatus.Dispatched || stop.Status != DeliveryRouteStopStatus.Planned)
            {
                throw new LogisticsDomainException(LogisticsError.StopNotPlanned, "Proof uploads require a planned stop on a dispatched route.");
            }

            var kind = ParseProofKind(request.Kind);
            ValidateUpload(request, options.Value);
            var ttl = options.Value.ClampPresignTtl();
            var contentType = request.ContentType.Trim();

            if (!string.IsNullOrWhiteSpace(operationKey))
            {
                var prefixed = OperationKey("logistics-proof-upload", context.TenantId, operationKey);
                var existing = await db.Set<DeliveryProofUploadIntent>()
                    .SingleOrDefaultAsync(x => x.TenantId == context.TenantId && x.OperationKey == prefixed, ct);
                if (existing is not null)
                {
                    if (!existing.MatchesPayload(stop.Id, kind, contentType, request.SizeBytes))
                    {
                        throw new LogisticsDomainException(LogisticsError.IdempotencyKeyReused, "Idempotency-Key was reused with a different payload.");
                    }

                    var replay = await storage.PresignPutAsync(new PresignPutObjectRequest(existing.ObjectKey, contentType, request.SizeBytes, ttl), ct);
                    return ResultFactory.Ok(new DeliveryProofUploadResult(existing.ObjectKey, replay.UploadUrl, replay.ExpiresAt));
                }

                var objectKey = BuildProofObjectKey(context.TenantId, stop.Id, kind, ids.NewId(), contentType);
                var presigned = await storage.PresignPutAsync(new PresignPutObjectRequest(objectKey, contentType, request.SizeBytes, ttl), ct);
                db.Add(new DeliveryProofUploadIntent(
                    ids.NewId(),
                    context.TenantId,
                    prefixed,
                    stop.Id,
                    kind,
                    contentType,
                    request.SizeBytes,
                    objectKey,
                    presigned.ExpiresAt,
                    clock.GetUtcNow()));
                await db.SaveChangesAsync(ct);
                return ResultFactory.Ok(new DeliveryProofUploadResult(objectKey, presigned.UploadUrl, presigned.ExpiresAt));
            }

            var freshKey = BuildProofObjectKey(context.TenantId, stop.Id, kind, ids.NewId(), contentType);
            var fresh = await storage.PresignPutAsync(new PresignPutObjectRequest(freshKey, contentType, request.SizeBytes, ttl), ct);
            return ResultFactory.Ok(new DeliveryProofUploadResult(freshKey, fresh.UploadUrl, fresh.ExpiresAt));
        }
        catch (LogisticsDomainException ex)
        {
            return ResultFactory.Fail<DeliveryProofUploadResult>(LogisticsErrorMapping.ToDomainError(ex));
        }
    }
}

/// <summary>
/// Verifies proof objects outside the ConfirmDelivery PG transaction (TOCTOU mitigation: HeadObject before short TX).
/// </summary>
public sealed class LogisticsProofObjectVerifier(IObjectStorage storage) : ILogisticsProofObjectVerifier
{
    public Task EnsureProofObjectsExistAsync(Guid tenantId, Guid stopId, DeliveryProofRequest? proof, CancellationToken ct) =>
        LogisticsCommandSupport.EnsureProofObjectsExistAsync(tenantId, stopId, proof, storage, ct);
}

public sealed class ConfirmDeliveryHandler(BinexusDbContext db, ICurrentTenant currentTenant, IOrderFulfillmentApi orders, IIdGenerator ids, TimeProvider clock) : ICommandHandler<ConfirmDeliveryCommand>
{
    public async Task<Result> HandleAsync(ConfirmDeliveryCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var context = Require(currentTenant);
            // Proof ExistsAsync/HeadObject must run in the endpoint before DispatchAsync — not here.
            return await ConfirmCoreAsync(context, command, cancellationToken);
        }
        catch (LogisticsDomainException ex)
        {
            return Result.Failure(LogisticsErrorMapping.ToDomainError(ex));
        }
    }

    private async Task<Result> ConfirmCoreAsync(TenantContext context, ConfirmDeliveryCommand command, CancellationToken ct)
    {
        var routeAndStop = await LoadRouteAndStopAsync(db, context.TenantId, command.DeliveryRouteStopId, ct);
        var route = routeAndStop.Route;
        var stop = routeAndStop.Stop;
        var operationKey = OperationKey("logistics-stop-confirm", context.TenantId, command.OperationKey);
        if (stop.CompletionOperationKey == operationKey || stop.Status == DeliveryRouteStopStatus.Delivered)
        {
            return Result.Success();
        }

        if (route.Status != DeliveryRouteStatus.Dispatched)
        {
            throw new LogisticsDomainException(LogisticsError.DeliveryRouteNotDispatched, "Route is not dispatched.");
        }

        // Re-validate key scope inside TX (reject other-stop keys); existence was checked outside TX.
        if (command.Request.Proof is { } proofKeys)
        {
            foreach (var key in new[] { proofKeys.PhotoObjectKey, proofKeys.SignatureObjectKey }.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                ValidateProofObjectKey(context.TenantId, stop.Id, key!);
            }
        }

        stop.Confirm(clock.GetUtcNow(), operationKey);
        if (command.Request.Proof is { } proof)
        {
            db.Add(new DeliveryProof(ids.NewId(), context.TenantId, stop.Id, proof.PhotoObjectKey, proof.SignatureObjectKey, proof.Recipient, proof.Notes, proof.Latitude, proof.Longitude, clock.GetUtcNow()));
        }

        route.CompleteIfTerminal(clock.GetUtcNow());
        var moved = await orders.MarkDeliveredAsync(RequestFor(context, stop.OrderId, ids.NewId(), command.CorrelationId, "delivery confirmed", "logistics.confirm-delivery"), ct);
        if (moved.Outcome is OrderFulfillmentOutcome.NotFound or OrderFulfillmentOutcome.NoLongerApplicable or OrderFulfillmentOutcome.ConcurrencyConflict)
        {
            return Result.Failure(DomainError.Conflict(moved.Code ?? "ORDER_FULFILLMENT_REJECTED", moved.Message ?? "Orders rejected delivery confirmation."));
        }

        Record(db, ids, context.TenantId, "DELIVERY_CONFIRMED", new { deliveryRouteId = route.Id, deliveryRouteStopId = stop.Id, orderId = stop.OrderId }, command.CorrelationId, clock);
        return Result.Success();
    }
}

public sealed class ReportFailedDeliveryHandler(BinexusDbContext db, ICurrentTenant currentTenant, IOrderFulfillmentApi orders, IIdGenerator ids, TimeProvider clock) : ICommandHandler<ReportFailedDeliveryCommand>
{
    public Task<Result> HandleAsync(ReportFailedDeliveryCommand command, CancellationToken cancellationToken) => Capture(async () =>
    {
        var context = Require(currentTenant);
        var routeAndStop = await LoadRouteAndStopAsync(db, context.TenantId, command.DeliveryRouteStopId, cancellationToken);
        var route = routeAndStop.Route;
        var stop = routeAndStop.Stop;
        var operationKey = OperationKey("logistics-stop-fail", context.TenantId, command.OperationKey);
        if (stop.FailureOperationKey == operationKey || stop.Status == DeliveryRouteStopStatus.Failed)
        {
            return Result.Success();
        }

        if (route.Status != DeliveryRouteStatus.Dispatched)
        {
            throw new LogisticsDomainException(LogisticsError.DeliveryRouteNotDispatched, "Route is not dispatched.");
        }

        stop.Fail(LogisticsPersistedEnums.ParseFailureReason(command.Request.Reason.Trim().ToUpperInvariant()), command.Request.Notes, clock.GetUtcNow(), operationKey);
        route.CompleteIfTerminal(clock.GetUtcNow());
        var moved = await orders.MarkDeliveryAttemptFailedAsync(RequestFor(context, stop.OrderId, ids.NewId(), command.CorrelationId, "delivery attempt failed", "logistics.report-failed-delivery"), cancellationToken);
        if (moved.Outcome is OrderFulfillmentOutcome.NotFound or OrderFulfillmentOutcome.NoLongerApplicable or OrderFulfillmentOutcome.ConcurrencyConflict)
        {
            return Result.Failure(DomainError.Conflict(moved.Code ?? "ORDER_FULFILLMENT_REJECTED", moved.Message ?? "Orders rejected failed delivery."));
        }

        Record(db, ids, context.TenantId, "DELIVERY_FAILED", new { deliveryRouteId = route.Id, deliveryRouteStopId = stop.Id, orderId = stop.OrderId, reason = command.Request.Reason }, command.CorrelationId, clock);
        return Result.Success();
    });
}

public sealed class LiquidateDeliveryRouteHandler(
    BinexusDbContext db,
    ICurrentTenant currentTenant,
    IOrderFulfillmentApi orders,
    IIdGenerator ids,
    TimeProvider clock,
    IOptions<LogisticsFeatureOptions> features,
    ITenantFeatureService tenantFeatures) : ICommandHandler<LiquidateDeliveryRouteCommand>
{
    public Task<Result> HandleAsync(LiquidateDeliveryRouteCommand command, CancellationToken cancellationToken) => Capture(async () =>
    {
        var context = Require(currentTenant);
        if (!features.Value.LiquidationKillSwitch)
        {
            throw new LogisticsDomainException(LogisticsError.LiquidationDisabled, "Liquidation is disabled by operational kill switch.");
        }

        if (!await tenantFeatures.IsEnabledAsync(context.TenantId, FeatureKey.Liquidation, cancellationToken))
        {
            throw new LogisticsDomainException(LogisticsError.FeatureDisabled, "LIQUIDATION is not enabled for this tenant.");
        }

        if (context.Role is not ("ADMIN" or "SUPER_ADMIN"))
        {
            throw new LogisticsDomainException(LogisticsError.LiquidationForbidden, "Route liquidation requires ADMIN or SUPER_ADMIN.");
        }

        var operationKey = OperationKey("logistics-route-liquidate", context.TenantId, command.OperationKey);
        if (await db.Set<DeliveryRouteLiquidation>().AnyAsync(x => x.TenantId == context.TenantId && (x.DeliveryRouteId == command.DeliveryRouteId || x.OperationKey == operationKey), cancellationToken))
        {
            throw new LogisticsDomainException(LogisticsError.LiquidationAlreadyExists, "Delivery route is already liquidated.");
        }

        var route = await LoadRouteAsync(db, context.TenantId, command.DeliveryRouteId, cancellationToken);
        if (route.Status != DeliveryRouteStatus.Completed)
        {
            throw new LogisticsDomainException(LogisticsError.DeliveryRouteNotCompleted, "Only completed routes can be liquidated.");
        }

        var deliveredStops = route.Stops.Where(x => x.Status == DeliveryRouteStopStatus.Delivered).ToArray();
        var facts = await orders.GetCashCollectionFactsAsync(context.TenantId, deliveredStops.Select(x => x.OrderId).ToArray(), cancellationToken);
        if (facts.MissingOrderIds.Count > 0)
        {
            return Result.Failure(DomainError.Conflict("ORDER_CASH_FACTS_MISSING", "One or more orders were not found."));
        }

        var cashFacts = facts.Facts.Where(x => x.PaymentMethod == "CASH").ToArray();
        var currency = cashFacts.Select(x => x.Currency).Distinct(StringComparer.Ordinal).SingleOrDefault() ?? "USD";
        var expected = cashFacts.Sum(x => x.TotalCents);
        var declaredByStop = (command.Request.Lines ?? []).ToDictionary(x => x.DeliveryRouteStopId, x => x.DeclaredCents);
        var liquidationId = ids.NewId();
        var lines = cashFacts.Select(fact =>
        {
            var stop = deliveredStops.Single(x => x.OrderId == fact.OrderId);
            return new DeliveryRouteLiquidationLine(
                ids.NewId(),
                context.TenantId,
                liquidationId,
                stop.Id,
                fact.OrderId,
                fact.TotalCents,
                declaredByStop.GetValueOrDefault(stop.Id, fact.TotalCents),
                fact.PaymentMethod,
                included: true);
        }).ToArray();
        var liquidation = new DeliveryRouteLiquidation(liquidationId, context.TenantId, route.Id, expected, command.Request.DeclaredCents, currency, command.Request.DiscrepancyReason, command.Request.Notes, context.UserId, clock.GetUtcNow(), operationKey, lines);
        db.Add(liquidation);

        var settled = await orders.SettleCodOrdersAsync(new SettleCodOrdersRequest(context.TenantId, cashFacts.Select(x => x.OrderId).ToArray(), context.UserId, ParseCorrelationId(command.CorrelationId), ids.NewId(), "route liquidated", "logistics.liquidate-route"), cancellationToken);
        if (settled.Outcome is OrderFulfillmentOutcome.NotFound or OrderFulfillmentOutcome.NoLongerApplicable or OrderFulfillmentOutcome.ConcurrencyConflict)
        {
            return Result.Failure(DomainError.Conflict(settled.Code ?? "ORDER_FULFILLMENT_REJECTED", settled.Message ?? "Orders rejected route liquidation."));
        }

        Record(db, ids, context.TenantId, "DELIVERY_ROUTE_LIQUIDATED", new { deliveryRouteId = route.Id, liquidationId, cashOrderIds = cashFacts.Select(x => x.OrderId).ToArray(), expectedCents = expected, declaredCents = command.Request.DeclaredCents }, command.CorrelationId, clock);
        return Result.Success();
    });
}

/// <summary>
/// Strategy A (Nest parity for ORDER_READY_FOR_DELIVERY_ROUTE):
/// idempotent on CreatedFromEventId; READY → branch only; ASSIGNED → Reopen READY; CANCELLED → skip (do not reopen).
/// </summary>
public sealed class OrderReadyForDeliveryRouteLogisticsProcessor(BinexusDbContext db, IIdGenerator ids, TimeProvider clock) : IIntegrationEventProcessor
{
    public const string Key = "logistics.order-ready-for-delivery-route";
    public string HandlerKey => Key;
    public string EventName => "ORDER_READY_FOR_DELIVERY_ROUTE";

    public async Task<IntegrationProcessOutcome> ProcessAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var payload = ParseOrderPayload(message);
        var now = clock.GetUtcNow();
        var existing = await db.Set<DeliveryRouteCandidate>().SingleOrDefaultAsync(x => x.TenantId == message.TenantId && x.OrderId == payload.OrderId, cancellationToken);
        if (existing is null)
        {
            db.Add(new DeliveryRouteCandidate(ids.NewId(), message.TenantId, payload.OrderId, payload.BranchId, message.Id, now));
            return IntegrationProcessOutcome.Processed;
        }

        if (existing.CreatedFromEventId == message.Id)
        {
            return IntegrationProcessOutcome.Processed;
        }

        if (existing.Status == DeliveryRouteCandidateStatus.Ready)
        {
            existing.UpdateBranch(payload.BranchId, now);
            return IntegrationProcessOutcome.Processed;
        }

        if (existing.Status == DeliveryRouteCandidateStatus.Assigned)
        {
            existing.Reopen(payload.BranchId, message.Id, now);
            return IntegrationProcessOutcome.Processed;
        }

        // CANCELLED → skip (Nest: no requeue)
        return IntegrationProcessOutcome.Processed;
    }
}

/// <summary>
/// Nest cancels the candidate only. Hardening (.NET divergence): also remove PLANNED stops from PLANNED routes
/// so a cancelled order cannot remain ready for dispatch. Historical stops on DISPATCHED/COMPLETED routes stay.
/// </summary>
public sealed class OrderCancelledLogisticsProcessor(BinexusDbContext db, TimeProvider clock) : IIntegrationEventProcessor
{
    public const string Key = "logistics.order-cancelled";
    public string HandlerKey => Key;
    public string EventName => "ORDER_CANCELLED";

    public async Task<IntegrationProcessOutcome> ProcessAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var payload = ParseOrderPayload(message);
        var existing = await db.Set<DeliveryRouteCandidate>().SingleOrDefaultAsync(x => x.TenantId == message.TenantId && x.OrderId == payload.OrderId, cancellationToken);
        existing?.Cancel(clock.GetUtcNow());

        var plannedStops = await db.Set<DeliveryRouteStop>()
            .Where(x => x.TenantId == message.TenantId
                && x.OrderId == payload.OrderId
                && x.Status == DeliveryRouteStopStatus.Planned)
            .ToListAsync(cancellationToken);
        if (plannedStops.Count > 0)
        {
            var routeIds = plannedStops.Select(x => x.DeliveryRouteId).Distinct().ToArray();
            var plannedRoutes = await db.Set<DeliveryRoute>()
                .Where(x => x.TenantId == message.TenantId
                    && routeIds.Contains(x.Id)
                    && x.Status == DeliveryRouteStatus.Planned)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
            var removable = plannedStops.Where(x => plannedRoutes.Contains(x.DeliveryRouteId)).ToArray();
            if (removable.Length > 0)
            {
                db.RemoveRange(removable);
            }
        }

        return IntegrationProcessOutcome.Processed;
    }
}

/// <summary>
/// Local storage double for Development/Testing when Provider=Local.
/// Presign records an upload intent (content-type + max size). <see cref="ExistsAsync"/> is true
/// for keys that were issued (browser PUT is not required for confirm in Local mode).
/// Upload URL points at the API <c>/internal/dev-object-storage/...</c> endpoint.
/// Overwrite policy: a second PUT for the same key is rejected (409) after the first successful PUT.
/// </summary>
public sealed class LocalObjectStorage(IOptions<LogisticsStorageOptions> options, TimeProvider clock) : IObjectStorage
{
    private readonly object _gate = new();
    private readonly Dictionary<string, LocalUploadIntent> _intents = new(StringComparer.Ordinal);

    public Task<PresignedPutObject> PresignPutAsync(PresignPutObjectRequest request, CancellationToken ct)
    {
        lock (_gate)
        {
            _intents[request.ObjectKey] = new LocalUploadIntent(
                request.ObjectKey,
                request.ContentType,
                request.SizeBytes,
                Uploaded: false);
        }

        // For Local, Endpoint is the API public base (e.g. http://localhost:5102), not MinIO.
        var endpoint = options.Value.Endpoint.TrimEnd('/');
        var uploadUrl = new Uri($"{endpoint}/internal/dev-object-storage/{EncodeObjectKeyPath(request.ObjectKey)}");
        return Task.FromResult(new PresignedPutObject(uploadUrl, clock.GetUtcNow().Add(request.ExpiresIn)));
    }

    public Task<bool> ExistsAsync(string objectKey, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(_intents.ContainsKey(objectKey));
        }
    }

    /// <summary>
    /// Validates and consumes a Local PUT against a previously issued Presign intent.
    /// </summary>
    public LocalPutAcceptance TryAcceptPut(string objectKey, string? contentType, long? contentLength)
    {
        lock (_gate)
        {
            if (!_intents.TryGetValue(objectKey, out var intent))
            {
                return LocalPutAcceptance.Unissued;
            }

            if (intent.Uploaded)
            {
                return LocalPutAcceptance.AlreadyUploaded;
            }

            if (!string.IsNullOrWhiteSpace(contentType)
                && !string.Equals(contentType.Split(';', 2)[0].Trim(), intent.ContentType, StringComparison.OrdinalIgnoreCase))
            {
                return LocalPutAcceptance.WrongContentType;
            }

            var maxBytes = Math.Min(
                intent.MaxSizeBytes > 0 ? intent.MaxSizeBytes : options.Value.MaxProofBytes,
                options.Value.MaxProofBytes > 0 ? options.Value.MaxProofBytes : 10 * 1024 * 1024);

            if (contentLength is > 0 and var length && length > maxBytes)
            {
                return LocalPutAcceptance.Oversized;
            }

            _intents[objectKey] = intent with { Uploaded = true };
            return LocalPutAcceptance.Accepted;
        }
    }

    /// <summary>Encode path segments so slashes remain path separators.</summary>
    internal static string EncodeObjectKeyPath(string objectKey) =>
        string.Join('/', objectKey.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));

    private sealed record LocalUploadIntent(string ObjectKey, string ContentType, long MaxSizeBytes, bool Uploaded);
}

public enum LocalPutAcceptance
{
    Accepted,
    Unissued,
    AlreadyUploaded,
    WrongContentType,
    Oversized,
}

public static class LogisticsCommandSupport
{
    internal static async Task<Result> Capture(Func<Task<Result>> action)
    {
        try
        {
            return await action();
        }
        catch (LogisticsDomainException ex)
        {
            return Result.Failure(LogisticsErrorMapping.ToDomainError(ex));
        }
    }

    internal static TenantContext Require(ICurrentTenant currentTenant) =>
        currentTenant.Current ?? throw new LogisticsDomainException("FORBIDDEN", "Tenant context is required.");

    internal static Guid RequireTenant(ICurrentTenant currentTenant) =>
        currentTenant.Current?.TenantId ?? throw new LogisticsDomainException("FORBIDDEN", "Tenant context is required.");

    internal static async Task<DeliveryRoute> LoadRouteAsync(BinexusDbContext db, Guid tenantId, Guid routeId, CancellationToken ct) =>
        await db.Set<DeliveryRoute>().Include(x => x.Stops).SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == routeId, ct)
        ?? throw new LogisticsDomainException(LogisticsError.DeliveryRouteNotFound, "Delivery route not found.");

    internal static async Task<(DeliveryRoute Route, DeliveryRouteStop Stop)> LoadRouteAndStopAsync(BinexusDbContext db, Guid tenantId, Guid stopId, CancellationToken ct)
    {
        var stop = await db.Set<DeliveryRouteStop>().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == stopId, ct)
            ?? throw new LogisticsDomainException(LogisticsError.DeliveryStopNotFound, "Delivery route stop not found.");
        var route = await LoadRouteAsync(db, tenantId, stop.DeliveryRouteId, ct);
        return (route, route.Stops.Single(x => x.Id == stop.Id));
    }

    internal static OrderFulfillmentRequest RequestFor(TenantContext context, Guid orderId, Guid causationId, string? correlationId, string reason, string source) =>
        new(context.TenantId, orderId, context.UserId, ParseCorrelationId(correlationId), causationId, reason, source);

    internal static void Record(BinexusDbContext db, IIdGenerator ids, Guid tenantId, string name, object payload, string? correlationId, TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        db.Add(new OutboxMessage
        {
            Id = ids.NewId(),
            TenantId = tenantId,
            EventName = name,
            PayloadJson = JsonSerializer.Serialize(payload),
            OccurredAtUtc = now,
            CreatedAtUtc = now,
            CorrelationId = correlationId,
        });
    }

    internal static string OperationKey(string prefix, Guid tenantId, string key) =>
        $"{prefix}:{tenantId:D}:{key}";

    internal static Guid? ParseCorrelationId(string? value) =>
        Guid.TryParse(value, out var correlationId) ? correlationId : null;

    public static string BuildProofObjectKey(Guid tenantId, Guid stopId, string kind, Guid objectId, string contentType) =>
        $"tenants/{tenantId:D}/delivery-proofs/{stopId:D}/{kind.ToLowerInvariant()}-{objectId:D}.{ExtensionFor(contentType)}";

    internal static async Task EnsureProofObjectsExistAsync(Guid tenantId, Guid stopId, DeliveryProofRequest? proof, IObjectStorage storage, CancellationToken ct)
    {
        if (proof is null)
        {
            return;
        }

        foreach (var key in new[] { proof.PhotoObjectKey, proof.SignatureObjectKey }.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            ValidateProofObjectKey(tenantId, stopId, key!);
            if (!await storage.ExistsAsync(key!, ct))
            {
                throw new LogisticsDomainException(LogisticsError.ProofObjectNotFound, "Proof object was not uploaded.");
            }
        }
    }

    public static void ValidateProofObjectKey(Guid tenantId, Guid stopId, string objectKey)
    {
        var prefix = $"tenants/{tenantId:D}/delivery-proofs/{stopId:D}/";
        if (!objectKey.StartsWith(prefix, StringComparison.Ordinal)
            || objectKey.Contains("..", StringComparison.Ordinal)
            || objectKey.Contains('\\', StringComparison.Ordinal)
            || objectKey.Split('/').Any(string.IsNullOrWhiteSpace))
        {
            throw new LogisticsDomainException(LogisticsError.InvalidProofObjectKey, "Proof object key is outside the tenant stop scope.");
        }
    }

    internal static string ParseProofKind(string kind) => kind.Trim().ToUpperInvariant() switch
    {
        "PHOTO" => "photo",
        "SIGNATURE" => "signature",
        _ => throw new LogisticsDomainException(LogisticsError.InvalidProofUpload, "Proof upload kind must be PHOTO or SIGNATURE."),
    };

    internal static void ValidateUpload(CreateDeliveryProofUploadRequest request, LogisticsStorageOptions options)
    {
        if (request.SizeBytes <= 0 || request.SizeBytes > options.MaxProofBytes)
        {
            throw new LogisticsDomainException(LogisticsError.InvalidProofUpload, "Proof upload size is outside the allowed range.");
        }

        _ = ExtensionFor(request.ContentType);
    }

    private static string ExtensionFor(string contentType) => contentType.Trim().ToLowerInvariant() switch
    {
        "image/jpeg" => "jpg",
        "image/png" => "png",
        "image/webp" => "webp",
        _ => throw new LogisticsDomainException(LogisticsError.InvalidProofUpload, "Proof upload content type is not allowed."),
    };

    internal static OrderEventPayload ParseOrderPayload(OutboxMessage message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message.PayloadJson);
            var root = doc.RootElement;
            return new(root.GetProperty("orderId").GetGuid(), root.GetProperty("branchId").GetGuid());
        }
        catch (Exception ex) when (ex is KeyNotFoundException or JsonException or InvalidOperationException)
        {
            throw new PermanentHandlerException("logistics.invalid_order_payload", ex.Message);
        }
    }

    internal sealed record OrderEventPayload(Guid OrderId, Guid BranchId);
}
