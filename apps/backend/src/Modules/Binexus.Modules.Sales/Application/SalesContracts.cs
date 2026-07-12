using Binexus.Modules.Sales.Domain;
using Binexus.SharedKernel.Abstractions;
using Binexus.SharedKernel.Results;

namespace Binexus.Modules.Sales.Application;

public sealed record SalesSessionSummary(
    Guid Id,
    Guid BranchId,
    string TerminalId,
    string Status,
    int OpeningFloatCents,
    string Currency,
    Guid OpenedByUserId,
    DateTimeOffset OpenedAt,
    Guid? ClosedByUserId,
    DateTimeOffset? ClosedAt,
    int? ExpectedClosingCents,
    int? DeclaredClosingCents,
    int? DiscrepancyCents,
    string? DiscrepancyReason,
    string? CloseNotes);

public sealed record TicketLineSummary(
    string ProductId,
    string ProductName,
    int Quantity,
    int UnitPriceCents,
    int LineTotalCents);

public sealed record PaymentCaptureSummary(
    Guid Id,
    string Method,
    int AmountCents,
    string Currency,
    DateTimeOffset CapturedAt);

public sealed record TicketSummary(
    Guid Id,
    Guid SessionId,
    Guid BranchId,
    string TerminalId,
    string CustomerLabel,
    string Status,
    int TotalCents,
    string Currency,
    Guid CashierUserId,
    DateTimeOffset CreatedAt,
    IReadOnlyList<TicketLineSummary> Lines,
    IReadOnlyList<PaymentCaptureSummary> PaymentCaptures);

public sealed record OpenSalesSessionRequest(
    Guid? BranchId,
    string TerminalId,
    int OpeningFloatCents,
    string? Currency);

public sealed record OpenSalesSessionResult(SalesSessionSummary Session);

public sealed record GetCurrentSalesSessionResult(SalesSessionSummary? Session);

public sealed record CreateSaleLineRequest(
    string ProductId,
    string ProductName,
    int Quantity,
    int UnitPriceCents);

public sealed record CreateSalePaymentRequest(string Method, int AmountCents);

public sealed record CreateSaleRequest(
    IReadOnlyList<CreateSaleLineRequest> Lines,
    string? Currency,
    IReadOnlyList<CreateSalePaymentRequest> Payments);

public sealed record CreateSaleResult(TicketSummary Ticket);

public sealed record CloseSalesSessionRequest(
    int DeclaredClosingCents,
    string? Notes,
    string? DiscrepancyReason);

public sealed record CloseSalesSessionResult(SalesSessionSummary Session);

public sealed record OpenSalesSessionCommand(
    Guid SessionId,
    OpenSalesSessionRequest Request,
    string OperationKey,
    string? CorrelationId) : ITransactionalCommand;

public sealed record CreateSaleCommand(
    Guid SessionId,
    Guid SaleId,
    CreateSaleRequest Request,
    string OperationKey,
    string? CorrelationId) : ITransactionalCommand;

public sealed record CloseSalesSessionCommand(
    Guid SessionId,
    CloseSalesSessionRequest Request,
    string OperationKey,
    string? CorrelationId) : ITransactionalCommand;

public interface ISalesQueryService
{
    Task<Result<GetCurrentSalesSessionResult>> GetCurrentAsync(string terminalId, Guid? branchId, CancellationToken ct);

    Task<Result<SalesSessionSummary>> GetByIdAsync(Guid sessionId, CancellationToken ct);

    Task<Result<SalesSessionSummary>> GetByOpenOperationKeyAsync(string operationKey, CancellationToken ct);

    Task<Result<TicketSummary>> GetSaleByOperationKeyAsync(string operationKey, CancellationToken ct);

    Task<Result<TicketSummary>> GetSaleByIdAsync(Guid saleId, CancellationToken ct);
}

public static class SalesErrorMapping
{
    public static DomainError ToDomainError(SalesDomainException ex) =>
        ex.Code switch
        {
            SalesError.SessionNotFound => DomainError.NotFound(ex.Code, ex.Message),
            SalesError.SessionAlreadyOpen
                or SalesError.IdempotencyKeyReused => DomainError.Conflict(ex.Code, ex.Message),
            SalesError.FeatureDisabled
                or SalesError.Forbidden
                or SalesError.DiscrepancyForbidden => DomainError.Forbidden(ex.Code, ex.Message),
            _ => DomainError.Validation(ex.Code, ex.Message),
        };
}
