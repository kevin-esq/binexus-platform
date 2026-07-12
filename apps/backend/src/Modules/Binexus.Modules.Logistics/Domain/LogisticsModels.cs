using Binexus.SharedKernel.Abstractions;

namespace Binexus.Modules.Logistics.Domain;

public enum DeliveryRouteCandidateStatus
{
    Ready,
    Assigned,
    Cancelled,
}

public enum DeliveryRouteStatus
{
    Planned,
    Dispatched,
    Completed,
    Cancelled,
}

public enum DeliveryRouteStopStatus
{
    Planned,
    Delivered,
    Failed,
    Skipped,
}

public enum DeliveryFailureReason
{
    NoRecipient,
    WrongAddress,
    Refused,
    Damaged,
    Other,
}

public sealed class DeliveryRouteCandidate : ITenantScoped
{
    private DeliveryRouteCandidate() { }

    public DeliveryRouteCandidate(Guid id, Guid tenantId, Guid orderId, Guid branchId, Guid createdFromEventId, DateTimeOffset now)
    {
        if (id == Guid.Empty) throw new LogisticsDomainException(LogisticsError.InvalidCandidate, "Candidate id is required.");
        if (tenantId == Guid.Empty) throw new LogisticsDomainException(LogisticsError.InvalidCandidate, "Tenant is required.");
        if (orderId == Guid.Empty) throw new LogisticsDomainException(LogisticsError.InvalidCandidate, "Order is required.");
        if (branchId == Guid.Empty) throw new LogisticsDomainException(LogisticsError.InvalidCandidate, "Branch is required.");
        if (createdFromEventId == Guid.Empty) throw new LogisticsDomainException(LogisticsError.InvalidCandidate, "Created event is required.");
        Id = id;
        TenantId = tenantId;
        OrderId = orderId;
        BranchId = branchId;
        CreatedFromEventId = createdFromEventId;
        Status = DeliveryRouteCandidateStatus.Ready;
        CreatedAtUtc = UpdatedAtUtc = now;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid BranchId { get; private set; }
    public DeliveryRouteCandidateStatus Status { get; private set; }
    public Guid CreatedFromEventId { get; private set; }
    public Guid? DeliveryRouteId { get; private set; }
    public uint Version { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Strategy A (Nest parity): READY candidates only refresh branch on ORDER_READY.</summary>
    public void UpdateBranch(Guid branchId, DateTimeOffset now)
    {
        if (Status != DeliveryRouteCandidateStatus.Ready)
        {
            throw new LogisticsDomainException(LogisticsError.InvalidCandidate, "Only READY candidates can update branch.");
        }

        if (branchId == Guid.Empty) throw new LogisticsDomainException(LogisticsError.InvalidCandidate, "Branch is required.");
        BranchId = branchId;
        UpdatedAtUtc = now;
    }

    /// <summary>Strategy A (Nest parity): ASSIGNED → READY on requeue (clear route, new event id).</summary>
    public void Reopen(Guid branchId, Guid eventId, DateTimeOffset now)
    {
        if (branchId == Guid.Empty) throw new LogisticsDomainException(LogisticsError.InvalidCandidate, "Branch is required.");
        BranchId = branchId;
        CreatedFromEventId = eventId;
        DeliveryRouteId = null;
        Status = DeliveryRouteCandidateStatus.Ready;
        UpdatedAtUtc = now;
    }

    public void Assign(Guid deliveryRouteId, DateTimeOffset now)
    {
        if (Status != DeliveryRouteCandidateStatus.Ready)
        {
            throw new LogisticsDomainException(LogisticsError.CandidateNotReady, "Candidate is not ready for assignment.");
        }

        DeliveryRouteId = deliveryRouteId;
        Status = DeliveryRouteCandidateStatus.Assigned;
        UpdatedAtUtc = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        Status = DeliveryRouteCandidateStatus.Cancelled;
        DeliveryRouteId = null;
        UpdatedAtUtc = now;
    }
}

/// <summary>
/// Route lifecycle. <see cref="DeliveryRouteStatus.Cancelled"/> is reserved — no public Cancel writer in this slice.
/// </summary>
public sealed class DeliveryRoute : ITenantScoped
{
    private readonly List<DeliveryRouteStop> _stops = [];

    private DeliveryRoute() { }

    public DeliveryRoute(Guid id, Guid tenantId, Guid branchId, DateOnly? plannedDate, DateTimeOffset now, string operationKey)
    {
        if (id == Guid.Empty) throw new LogisticsDomainException(LogisticsError.InvalidDeliveryRoute, "Delivery route id is required.");
        if (tenantId == Guid.Empty) throw new LogisticsDomainException(LogisticsError.InvalidDeliveryRoute, "Tenant is required.");
        if (branchId == Guid.Empty) throw new LogisticsDomainException(LogisticsError.InvalidDeliveryRoute, "Branch is required.");
        if (string.IsNullOrWhiteSpace(operationKey)) throw new LogisticsDomainException(LogisticsError.InvalidDeliveryRoute, "Operation key is required.");
        Id = id;
        TenantId = tenantId;
        BranchId = branchId;
        PlannedDate = plannedDate;
        Status = DeliveryRouteStatus.Planned;
        CreatedAtUtc = UpdatedAtUtc = now;
        CreationOperationKey = operationKey;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public DeliveryRouteStatus Status { get; private set; }
    public Guid? DriverUserId { get; private set; }
    public DateOnly? PlannedDate { get; private set; }
    public DateTimeOffset? DispatchedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public string CreationOperationKey { get; private set; } = string.Empty;
    public string? AssignOperationKey { get; private set; }
    public string? DispatchOperationKey { get; private set; }
    public string? CompletionOperationKey { get; private set; }
    public uint Version { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public IReadOnlyCollection<DeliveryRouteStop> Stops => _stops;

    public void AddStop(DeliveryRouteStop stop, DateTimeOffset now, string operationKey)
    {
        if (Status != DeliveryRouteStatus.Planned) throw new LogisticsDomainException(LogisticsError.DeliveryRouteNotPlanned, "Only planned routes can receive orders.");
        if (stop.BranchId != BranchId) throw new LogisticsDomainException(LogisticsError.BranchMismatch, "Stop branch must match route branch.");
        if (_stops.Any(x => x.OrderId == stop.OrderId)) throw new LogisticsDomainException(LogisticsError.OrderAlreadyAssigned, "Order is already assigned to this route.");
        _stops.Add(stop);
        AssignOperationKey = operationKey;
        UpdatedAtUtc = now;
    }

    public void RecordAssignment(DateTimeOffset now, string operationKey)
    {
        if (Status != DeliveryRouteStatus.Planned) throw new LogisticsDomainException(LogisticsError.DeliveryRouteNotPlanned, "Only planned routes can receive orders.");
        AssignOperationKey = operationKey;
        UpdatedAtUtc = now;
    }

    public void Dispatch(Guid driverUserId, DateTimeOffset now, string operationKey)
    {
        if (driverUserId == Guid.Empty) throw new LogisticsDomainException(LogisticsError.InvalidDeliveryRoute, "Driver is required.");
        if (Status == DeliveryRouteStatus.Dispatched) return;
        if (Status != DeliveryRouteStatus.Planned) throw new LogisticsDomainException(LogisticsError.DeliveryRouteNotPlanned, "Only planned routes can be dispatched.");
        if (_stops.Count == 0) throw new LogisticsDomainException(LogisticsError.RouteHasNoStops, "Route requires at least one stop before dispatch.");
        DriverUserId = driverUserId;
        Status = DeliveryRouteStatus.Dispatched;
        DispatchedAtUtc = now;
        DispatchOperationKey = operationKey;
        UpdatedAtUtc = now;
    }

    public void CompleteIfTerminal(DateTimeOffset now)
    {
        if (Status != DeliveryRouteStatus.Dispatched || _stops.Any(x => x.Status == DeliveryRouteStopStatus.Planned))
        {
            return;
        }

        Status = DeliveryRouteStatus.Completed;
        CompletedAtUtc = now;
        UpdatedAtUtc = now;
    }
}

/// <summary>
/// Stop lifecycle. <see cref="DeliveryRouteStopStatus.Skipped"/> is reserved — no public Skip writer in this slice.
/// </summary>
public sealed class DeliveryRouteStop : ITenantScoped
{
    private DeliveryRouteStop() { }

    public DeliveryRouteStop(Guid id, Guid tenantId, Guid branchId, Guid deliveryRouteId, Guid orderId, int sequence, DateTimeOffset now)
    {
        if (id == Guid.Empty) throw new LogisticsDomainException(LogisticsError.InvalidDeliveryStop, "Stop id is required.");
        if (tenantId == Guid.Empty) throw new LogisticsDomainException(LogisticsError.InvalidDeliveryStop, "Tenant is required.");
        if (branchId == Guid.Empty) throw new LogisticsDomainException(LogisticsError.InvalidDeliveryStop, "Branch is required.");
        if (deliveryRouteId == Guid.Empty) throw new LogisticsDomainException(LogisticsError.InvalidDeliveryStop, "Route is required.");
        if (orderId == Guid.Empty) throw new LogisticsDomainException(LogisticsError.InvalidDeliveryStop, "Order is required.");
        if (sequence <= 0) throw new LogisticsDomainException(LogisticsError.InvalidDeliveryStop, "Sequence must be positive.");
        Id = id;
        TenantId = tenantId;
        BranchId = branchId;
        DeliveryRouteId = deliveryRouteId;
        OrderId = orderId;
        Sequence = sequence;
        Status = DeliveryRouteStopStatus.Planned;
        CreatedAtUtc = UpdatedAtUtc = now;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid DeliveryRouteId { get; private set; }
    public Guid OrderId { get; private set; }
    public int Sequence { get; private set; }
    public DeliveryRouteStopStatus Status { get; private set; }
    public DeliveryFailureReason? FailureReason { get; private set; }
    public string? FailureNotes { get; private set; }
    public DateTimeOffset? DeliveredAtUtc { get; private set; }
    public DateTimeOffset? FailedAtUtc { get; private set; }
    public string? CompletionOperationKey { get; private set; }
    public string? FailureOperationKey { get; private set; }
    public uint Version { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void Confirm(DateTimeOffset now, string operationKey)
    {
        if (Status != DeliveryRouteStopStatus.Planned) throw new LogisticsDomainException(LogisticsError.StopNotPlanned, "Stop is not planned.");
        Status = DeliveryRouteStopStatus.Delivered;
        DeliveredAtUtc = now;
        CompletionOperationKey = operationKey;
        UpdatedAtUtc = now;
    }

    public void Fail(DeliveryFailureReason reason, string? notes, DateTimeOffset now, string operationKey)
    {
        if (Status != DeliveryRouteStopStatus.Planned) throw new LogisticsDomainException(LogisticsError.StopNotPlanned, "Stop is not planned.");
        Status = DeliveryRouteStopStatus.Failed;
        FailureReason = reason;
        FailureNotes = Normalize(notes, 512);
        FailedAtUtc = now;
        FailureOperationKey = operationKey;
        UpdatedAtUtc = now;
    }

    private static string? Normalize(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is null || normalized.Length <= maxLength ? normalized : throw new LogisticsDomainException(LogisticsError.InvalidDeliveryStop, "Value is too long.");
    }
}

/// <summary>
/// Delivery proof attachment. Object lifecycle: Presigned → UploadedExternally → Verified (ExistsAsync) → AttachedToDelivery (this row).
/// </summary>
public sealed class DeliveryProof : ITenantScoped
{
    private DeliveryProof() { }

    public DeliveryProof(Guid id, Guid tenantId, Guid deliveryRouteStopId, string? photoObjectKey, string? signatureObjectKey, string? recipient, string? notes, decimal? latitude, decimal? longitude, DateTimeOffset now)
    {
        if (id == Guid.Empty) throw new LogisticsDomainException(LogisticsError.InvalidDeliveryProof, "Proof id is required.");
        if (tenantId == Guid.Empty) throw new LogisticsDomainException(LogisticsError.InvalidDeliveryProof, "Tenant is required.");
        if (deliveryRouteStopId == Guid.Empty) throw new LogisticsDomainException(LogisticsError.InvalidDeliveryProof, "Stop is required.");
        Id = id;
        TenantId = tenantId;
        DeliveryRouteStopId = deliveryRouteStopId;
        PhotoObjectKey = Normalize(photoObjectKey, 512);
        SignatureObjectKey = Normalize(signatureObjectKey, 512);
        Recipient = Normalize(recipient, 256);
        Notes = Normalize(notes, 1024);
        Latitude = latitude;
        Longitude = longitude;
        CreatedAtUtc = now;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid DeliveryRouteStopId { get; private set; }
    public string? PhotoObjectKey { get; private set; }
    public string? SignatureObjectKey { get; private set; }
    public string? Recipient { get; private set; }
    public string? Notes { get; private set; }
    public decimal? Latitude { get; private set; }
    public decimal? Longitude { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    private static string? Normalize(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is null || normalized.Length <= maxLength ? normalized : throw new LogisticsDomainException(LogisticsError.InvalidDeliveryProof, "Proof value is too long.");
    }
}

public sealed class DeliveryRouteLiquidation : ITenantScoped
{
    private readonly List<DeliveryRouteLiquidationLine> _lines = [];

    private DeliveryRouteLiquidation() { }

    public DeliveryRouteLiquidation(Guid id, Guid tenantId, Guid deliveryRouteId, int expectedCents, int declaredCents, string currency, string? discrepancyReason, string? notes, Guid? actorId, DateTimeOffset now, string operationKey, IEnumerable<DeliveryRouteLiquidationLine> lines)
    {
        if (id == Guid.Empty) throw new LogisticsDomainException(LogisticsError.InvalidLiquidation, "Liquidation id is required.");
        if (deliveryRouteId == Guid.Empty) throw new LogisticsDomainException(LogisticsError.InvalidLiquidation, "Route is required.");
        if (expectedCents < 0 || declaredCents < 0) throw new LogisticsDomainException(LogisticsError.InvalidLiquidation, "Amounts cannot be negative.");
        if (expectedCents != declaredCents && string.IsNullOrWhiteSpace(discrepancyReason)) throw new LogisticsDomainException(LogisticsError.LiquidationDiscrepancyReasonRequired, "Discrepancy reason is required.");
        Id = id;
        TenantId = tenantId;
        DeliveryRouteId = deliveryRouteId;
        ExpectedCents = expectedCents;
        DeclaredCents = declaredCents;
        Currency = currency;
        DiscrepancyReason = Normalize(discrepancyReason, 512);
        Notes = Normalize(notes, 1024);
        LiquidatedByUserId = actorId;
        OperationKey = operationKey;
        CreatedAtUtc = now;
        _lines.AddRange(lines);
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid DeliveryRouteId { get; private set; }
    public int ExpectedCents { get; private set; }
    public int DeclaredCents { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string? DiscrepancyReason { get; private set; }
    public string? Notes { get; private set; }
    public Guid? LiquidatedByUserId { get; private set; }
    public string OperationKey { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public IReadOnlyCollection<DeliveryRouteLiquidationLine> Lines => _lines;

    private static string? Normalize(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is null || normalized.Length <= maxLength ? normalized : throw new LogisticsDomainException(LogisticsError.InvalidLiquidation, "Value is too long.");
    }
}

public sealed class DeliveryRouteLiquidationLine : ITenantScoped
{
    private DeliveryRouteLiquidationLine() { }

    public DeliveryRouteLiquidationLine(
        Guid id,
        Guid tenantId,
        Guid deliveryRouteLiquidationId,
        Guid deliveryRouteStopId,
        Guid orderId,
        int expectedCents,
        int declaredCents,
        string paymentMethod,
        bool included = true)
    {
        if (string.IsNullOrWhiteSpace(paymentMethod))
        {
            throw new LogisticsDomainException(LogisticsError.InvalidLiquidation, "Payment method snapshot is required.");
        }

        Id = id;
        TenantId = tenantId;
        DeliveryRouteLiquidationId = deliveryRouteLiquidationId;
        DeliveryRouteStopId = deliveryRouteStopId;
        OrderId = orderId;
        ExpectedCents = expectedCents;
        DeclaredCents = declaredCents;
        PaymentMethod = paymentMethod.Trim().ToUpperInvariant();
        Included = included;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid DeliveryRouteLiquidationId { get; private set; }
    public Guid DeliveryRouteStopId { get; private set; }
    public Guid OrderId { get; private set; }
    public int ExpectedCents { get; private set; }
    public int DeclaredCents { get; private set; }
    /// <summary>Payment method snapshot at liquidate time — do not re-query Orders for history.</summary>
    public string PaymentMethod { get; private set; } = string.Empty;
    public bool Included { get; private set; }
}

/// <summary>
/// Pending proof upload intent for optional Idempotency-Key on presign.
/// Same key + same payload → same objectKey; same key + different payload → IDEMPOTENCY_KEY_REUSED.
/// </summary>
public sealed class DeliveryProofUploadIntent : ITenantScoped
{
    private DeliveryProofUploadIntent() { }

    public DeliveryProofUploadIntent(
        Guid id,
        Guid tenantId,
        string operationKey,
        Guid stopId,
        string kind,
        string contentType,
        long sizeBytes,
        string objectKey,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        TenantId = tenantId;
        OperationKey = operationKey;
        StopId = stopId;
        Kind = kind;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        ObjectKey = objectKey;
        ExpiresAtUtc = expiresAtUtc;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string OperationKey { get; private set; } = string.Empty;
    public Guid StopId { get; private set; }
    public string Kind { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string ObjectKey { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public bool MatchesPayload(Guid stopId, string kind, string contentType, long sizeBytes) =>
        StopId == stopId
        && string.Equals(Kind, kind, StringComparison.Ordinal)
        && string.Equals(ContentType, contentType, StringComparison.OrdinalIgnoreCase)
        && SizeBytes == sizeBytes;
}

public static class LogisticsError
{
    public const string InvalidCandidate = "INVALID_DELIVERY_ROUTE_CANDIDATE";
    public const string CandidateNotFound = "DELIVERY_ROUTE_CANDIDATE_NOT_FOUND";
    public const string CandidateNotReady = "DELIVERY_ROUTE_CANDIDATE_NOT_READY";
    public const string InvalidDeliveryRoute = "INVALID_DELIVERY_ROUTE";
    public const string DeliveryRouteNotFound = "DELIVERY_ROUTE_NOT_FOUND";
    public const string DeliveryRouteNotPlanned = "DELIVERY_ROUTE_NOT_PLANNED";
    public const string DeliveryRouteNotDispatched = "DELIVERY_ROUTE_NOT_DISPATCHED";
    public const string DeliveryRouteNotCompleted = "DELIVERY_ROUTE_NOT_COMPLETED";
    public const string RouteHasNoStops = "DELIVERY_ROUTE_HAS_NO_STOPS";
    public const string OrderAlreadyAssigned = "ORDER_ALREADY_ASSIGNED_TO_ROUTE";
    public const string BranchMismatch = "DELIVERY_ROUTE_BRANCH_MISMATCH";
    public const string InvalidDeliveryStop = "INVALID_DELIVERY_ROUTE_STOP";
    public const string DeliveryStopNotFound = "DELIVERY_ROUTE_STOP_NOT_FOUND";
    public const string StopNotPlanned = "DELIVERY_ROUTE_STOP_NOT_PLANNED";
    public const string InvalidDeliveryProof = "INVALID_DELIVERY_PROOF";
    public const string ProofObjectNotFound = "DELIVERY_PROOF_OBJECT_NOT_FOUND";
    public const string InvalidProofObjectKey = "INVALID_DELIVERY_PROOF_OBJECT_KEY";
    public const string InvalidProofUpload = "INVALID_DELIVERY_PROOF_UPLOAD";
    public const string InvalidLiquidation = "INVALID_DELIVERY_ROUTE_LIQUIDATION";
    /// <summary>Operational kill switch <c>Features:LiquidationKillSwitch</c> is off.</summary>
    public const string LiquidationDisabled = "LIQUIDATION_DISABLED";
    /// <summary>Tenant commercial entitlement (TenantFeature LIQUIDATION) is off.</summary>
    public const string FeatureDisabled = "FEATURE_DISABLED";
    /// <summary>Caller role is not ADMIN/SUPER_ADMIN.</summary>
    public const string LiquidationForbidden = "LIQUIDATION_FORBIDDEN";
    public const string LiquidationAlreadyExists = "DELIVERY_ROUTE_ALREADY_LIQUIDATED";
    public const string LiquidationDiscrepancyReasonRequired = "LIQUIDATION_DISCREPANCY_REASON_REQUIRED";
    public const string IdempotencyKeyReused = "IDEMPOTENCY_KEY_REUSED";
    public const string InvalidCursor = "INVALID_CURSOR";
}

public sealed class LogisticsDomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
