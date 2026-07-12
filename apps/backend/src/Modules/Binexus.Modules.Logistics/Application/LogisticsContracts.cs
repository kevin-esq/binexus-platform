using Binexus.Modules.Logistics.Domain;
using Binexus.SharedKernel.Abstractions;
using Binexus.SharedKernel.Results;

namespace Binexus.Modules.Logistics.Application;

public sealed record DeliveryRouteCandidateSummary(Guid Id, Guid BranchId, Guid OrderId, string Status, Guid? DeliveryRouteId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record ListDeliveryRouteCandidatesQuery(string? Status, Guid? BranchId, int? Limit, string? Cursor);
public sealed record ListDeliveryRouteCandidatesResult(IReadOnlyList<DeliveryRouteCandidateSummary> Items, string? NextCursor);

public sealed record DeliveryRouteSummary(
    Guid Id,
    Guid BranchId,
    string Status,
    Guid? DriverUserId,
    DateOnly? PlannedDate,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int StopCount);

public sealed record DeliveryRouteStopSummary(
    Guid Id,
    Guid DeliveryRouteId,
    Guid OrderId,
    int Sequence,
    string Status,
    string? FailureReason,
    string? FailureNotes,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? FailedAt);

public sealed record ListDeliveryRoutesQuery(string? Status, Guid? BranchId, int? Limit, string? Cursor);
public sealed record ListDeliveryRoutesResult(IReadOnlyList<DeliveryRouteSummary> Items, string? NextCursor);
public sealed record ListDeliveryRouteStopsResult(IReadOnlyList<DeliveryRouteStopSummary> Items);

public sealed record CreateDeliveryRouteRequest(Guid? BranchId, DateOnly? PlannedDate);
public sealed record AssignOrdersRequest(IReadOnlyList<Guid> OrderIds);
public sealed record DispatchDeliveryRouteRequest(Guid DriverUserId);
public sealed record DeliveryProofRequest(string? PhotoObjectKey, string? SignatureObjectKey, string? Recipient, string? Notes, decimal? Latitude, decimal? Longitude);
public sealed record ConfirmDeliveryRequest(DeliveryProofRequest? Proof);
public sealed record ReportFailedDeliveryRequest(string Reason, string? Notes);
public sealed record CreateDeliveryProofUploadRequest(string Kind, string ContentType, long SizeBytes);
public sealed record DeliveryProofUploadResult(string ObjectKey, Uri UploadUrl, DateTimeOffset ExpiresAt);
public sealed record LiquidationLineRequest(Guid DeliveryRouteStopId, int DeclaredCents);
public sealed record LiquidateDeliveryRouteRequest(int DeclaredCents, string? Notes, string? DiscrepancyReason, IReadOnlyList<LiquidationLineRequest>? Lines);
public sealed record DeliveryRouteLiquidationSummary(Guid Id, Guid DeliveryRouteId, int ExpectedCents, int DeclaredCents, string Currency, DateTimeOffset CreatedAt);

public sealed record CreateDeliveryRouteCommand(Guid DeliveryRouteId, CreateDeliveryRouteRequest Request, string OperationKey, string? CorrelationId) : ITransactionalCommand;
public sealed record AssignOrdersToDeliveryRouteCommand(Guid DeliveryRouteId, AssignOrdersRequest Request, string OperationKey, string? CorrelationId) : ITransactionalCommand;
public sealed record DispatchDeliveryRouteCommand(Guid DeliveryRouteId, DispatchDeliveryRouteRequest Request, string OperationKey, string? CorrelationId) : ITransactionalCommand;
public sealed record ConfirmDeliveryCommand(Guid DeliveryRouteStopId, ConfirmDeliveryRequest Request, string OperationKey, string? CorrelationId) : ITransactionalCommand;
public sealed record ReportFailedDeliveryCommand(Guid DeliveryRouteStopId, ReportFailedDeliveryRequest Request, string OperationKey, string? CorrelationId) : ITransactionalCommand;
public sealed record LiquidateDeliveryRouteCommand(Guid DeliveryRouteId, LiquidateDeliveryRouteRequest Request, string OperationKey, string? CorrelationId) : ITransactionalCommand;

public interface ILogisticsQueryService
{
    Task<Result<ListDeliveryRouteCandidatesResult>> ListCandidatesAsync(ListDeliveryRouteCandidatesQuery query, CancellationToken ct);
    Task<Result<ListDeliveryRoutesResult>> ListRoutesAsync(ListDeliveryRoutesQuery query, CancellationToken ct);
    Task<Result<DeliveryRouteSummary>> GetRouteAsync(Guid deliveryRouteId, CancellationToken ct);
    Task<Result<DeliveryRouteSummary>> GetRouteByCreationOperationKeyAsync(string operationKey, CancellationToken ct);
    Task<Result<ListDeliveryRouteStopsResult>> ListStopsAsync(Guid deliveryRouteId, CancellationToken ct);
    Task<Result<DeliveryRouteStopSummary>> GetStopAsync(Guid deliveryRouteStopId, CancellationToken ct);
}

public interface IObjectStorage
{
    Task<PresignedPutObject> PresignPutAsync(PresignPutObjectRequest request, CancellationToken ct);
    Task<bool> ExistsAsync(string objectKey, CancellationToken ct);
}

public interface ILogisticsProofUploadService
{
    Task<Result<DeliveryProofUploadResult>> CreateAsync(
        Guid deliveryRouteStopId,
        CreateDeliveryProofUploadRequest request,
        string? operationKey,
        CancellationToken ct);
}

public interface ILogisticsProofObjectVerifier
{
    Task EnsureProofObjectsExistAsync(Guid tenantId, Guid stopId, DeliveryProofRequest? proof, CancellationToken ct);
}

public sealed record PresignPutObjectRequest(string ObjectKey, string ContentType, long SizeBytes, TimeSpan ExpiresIn);
public sealed record PresignedPutObject(Uri UploadUrl, DateTimeOffset ExpiresAt);

public static class LogisticsStorageProviders
{
    public const string Local = "Local";
    public const string MinIO = "MinIO";
}

public sealed class LogisticsStorageOptions
{
    public const string SectionName = "Logistics:Storage";

    /// <summary>Explicit provider: <c>Local</c> or <c>MinIO</c>. Never inferred from empty credentials.</summary>
    public string Provider { get; set; } = LogisticsStorageProviders.Local;

    /// <summary>
    /// Legacy single endpoint. Prefer <see cref="InternalEndpoint"/> + <see cref="PublicEndpoint"/> for MinIO.
    /// When Provider=Local, public API base for <c>/internal/dev-object-storage/...</c> URLs.
    /// </summary>
    public string Endpoint { get; set; } = "http://localhost:5102";

    /// <summary>
    /// S3 client endpoint for Head/Exists from the API container (e.g. <c>http://minio:9000</c>).
    /// Falls back to <see cref="Endpoint"/> when unset.
    /// </summary>
    public string? InternalEndpoint { get; set; }

    /// <summary>
    /// Browser-reachable base for presigned URLs (e.g. <c>http://localhost:9000</c>).
    /// Falls back to <see cref="InternalEndpoint"/> then <see cref="Endpoint"/>.
    /// </summary>
    public string? PublicEndpoint { get; set; }

    public string Bucket { get; set; } = "binexus";
    public string Region { get; set; } = "us-east-1";
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public TimeSpan PresignTtl { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan MinPresignTtl { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan MaxPresignTtl { get; set; } = TimeSpan.FromMinutes(30);
    public long MaxProofBytes { get; set; } = 10 * 1024 * 1024;

    public bool IsLocal =>
        string.Equals(Provider, LogisticsStorageProviders.Local, StringComparison.OrdinalIgnoreCase);

    public bool IsMinIO =>
        string.Equals(Provider, LogisticsStorageProviders.MinIO, StringComparison.OrdinalIgnoreCase);

    /// <summary>Resolved S3 ops endpoint (container DNS).</summary>
    public string ResolveInternalEndpoint() =>
        FirstNonEmpty(InternalEndpoint, Endpoint)
        ?? throw new InvalidOperationException("Logistics:Storage InternalEndpoint or Endpoint is required.");

    /// <summary>Resolved presign endpoint (browser / host).</summary>
    public string ResolvePublicEndpoint() =>
        FirstNonEmpty(PublicEndpoint, InternalEndpoint, Endpoint)
        ?? throw new InvalidOperationException("Logistics:Storage PublicEndpoint, InternalEndpoint, or Endpoint is required.");

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.TrimEnd('/');
            }
        }

        return null;
    }

    public TimeSpan ClampPresignTtl()
    {
        var min = MinPresignTtl <= TimeSpan.Zero ? TimeSpan.FromMinutes(1) : MinPresignTtl;
        var max = MaxPresignTtl < min ? min : MaxPresignTtl;
        if (PresignTtl < min) return min;
        if (PresignTtl > max) return max;
        return PresignTtl;
    }
}

/// <summary>
/// Operational feature toggles. Commercial entitlements live in TenantFeature (ADR-0009).
/// </summary>
public sealed class LogisticsFeatureOptions
{
    public const string SectionName = "Features";

    /// <summary>
    /// Operational kill switch only (default <c>true</c> = allow). When <c>false</c>, liquidation is blocked for all tenants.
    /// Not a commercial entitlement — use TenantFeature LIQUIDATION for that.
    /// </summary>
    public bool LiquidationKillSwitch { get; init; } = true;
}

public static class LogisticsErrorMapping
{
    public static DomainError ToDomainError(LogisticsDomainException ex) =>
        ex.Code switch
        {
            LogisticsError.CandidateNotFound or LogisticsError.DeliveryRouteNotFound or LogisticsError.DeliveryStopNotFound => DomainError.NotFound(ex.Code, ex.Message),
            LogisticsError.CandidateNotReady
                or LogisticsError.DeliveryRouteNotPlanned
                or LogisticsError.DeliveryRouteNotDispatched
                or LogisticsError.DeliveryRouteNotCompleted
                or LogisticsError.RouteHasNoStops
                or LogisticsError.OrderAlreadyAssigned
                or LogisticsError.BranchMismatch
                or LogisticsError.StopNotPlanned
                or LogisticsError.ProofObjectNotFound
                or LogisticsError.LiquidationAlreadyExists
                or LogisticsError.LiquidationDiscrepancyReasonRequired
                or LogisticsError.IdempotencyKeyReused => DomainError.Conflict(ex.Code, ex.Message),
            LogisticsError.LiquidationDisabled
                or LogisticsError.FeatureDisabled
                or LogisticsError.LiquidationForbidden
                or "FORBIDDEN" => DomainError.Forbidden(ex.Code, ex.Message),
            _ => DomainError.Validation(ex.Code, ex.Message),
        };
}
