using System.Text.Json;
using Binexus.Modules.Inventory.Contracts;
using Binexus.Modules.Sales.Application;
using Binexus.Modules.Sales.Domain;
using Binexus.Platform.Dispatching;
using Binexus.Platform.Features.Contracts;
using Binexus.Platform.Ids;
using Binexus.Platform.Messaging;
using Binexus.Platform.Persistence;
using Binexus.Platform.Tenancy;
using Binexus.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using static Binexus.Modules.Sales.Infrastructure.SalesCommandSupport;

namespace Binexus.Modules.Sales.Infrastructure;

public sealed class SalesFeatureGate(ITenantFeatureService features)
{
    public async Task EnsureEnabledAsync(Guid tenantId, CancellationToken ct)
    {
        if (!await features.IsEnabledAsync(tenantId, FeatureKey.PosRetail, ct))
        {
            throw new SalesDomainException(SalesError.FeatureDisabled, "Feature \"POS_RETAIL\" is not enabled for tenant.");
        }
    }
}

public sealed class SalesQueryService(
    BinexusDbContext db,
    ICurrentTenant currentTenant,
    SalesFeatureGate featureGate) : ISalesQueryService
{
    public Task<Result<GetCurrentSalesSessionResult>> GetCurrentAsync(string terminalId, Guid? branchId, CancellationToken ct) =>
        Capture(() => GetCurrentCoreAsync(terminalId, branchId, ct));

    public Task<Result<SalesSessionSummary>> GetByIdAsync(Guid sessionId, CancellationToken ct) =>
        Capture(() => GetByIdCoreAsync(sessionId, ct));

    public Task<Result<SalesSessionSummary>> GetByOpenOperationKeyAsync(string operationKey, CancellationToken ct) =>
        Capture(() => GetByOpenOperationKeyCoreAsync(operationKey, ct));

    public Task<Result<TicketSummary>> GetSaleByOperationKeyAsync(string operationKey, CancellationToken ct) =>
        Capture(() => GetSaleByOperationKeyCoreAsync(operationKey, ct));

    public Task<Result<TicketSummary>> GetSaleByIdAsync(Guid saleId, CancellationToken ct) =>
        Capture(() => GetSaleByIdCoreAsync(saleId, ct));

    private async Task<GetCurrentSalesSessionResult> GetCurrentCoreAsync(string terminalId, Guid? branchId, CancellationToken ct)
    {
        var context = Require(currentTenant);
        EnsurePosRole(context);
        await featureGate.EnsureEnabledAsync(context.TenantId, ct);

        if (branchId is null && context.BranchId is null)
        {
            return new GetCurrentSalesSessionResult(null);
        }

        var resolvedBranchId = branchId ?? context.BranchId!.Value;
        var terminal = SalesSession.NormalizeTerminal(terminalId);
        var session = await db.Set<SalesSession>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.TenantId == context.TenantId
                    && x.BranchId == resolvedBranchId
                    && x.TerminalId == terminal
                    && x.Status == SalesSessionStatus.Open,
                ct);
        return new GetCurrentSalesSessionResult(session is null ? null : ToSummary(session));
    }

    private async Task<SalesSessionSummary> GetByIdCoreAsync(Guid sessionId, CancellationToken ct)
    {
        var context = Require(currentTenant);
        EnsurePosRole(context);
        await featureGate.EnsureEnabledAsync(context.TenantId, ct);
        var session = await LoadSessionAsync(db, context.TenantId, sessionId, tracking: false, ct);
        return ToSummary(session);
    }

    private async Task<SalesSessionSummary> GetByOpenOperationKeyCoreAsync(string operationKey, CancellationToken ct)
    {
        var context = Require(currentTenant);
        var prefixed = OperationKey("sales-session-open", context.TenantId, operationKey);
        var session = await db.Set<SalesSession>()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == context.TenantId && x.OpenOperationKey == prefixed, ct)
            ?? throw new SalesDomainException(SalesError.SessionNotFound, "Sales session not found.");
        return ToSummary(session);
    }

    private async Task<TicketSummary> GetSaleByOperationKeyCoreAsync(string operationKey, CancellationToken ct)
    {
        var context = Require(currentTenant);
        var prefixed = OperationKey("sales-create", context.TenantId, operationKey);
        var sale = await db.Set<Sale>()
            .AsNoTracking()
            .Include(x => x.Lines)
            .Include(x => x.PaymentCaptures)
            .SingleOrDefaultAsync(x => x.TenantId == context.TenantId && x.OperationKey == prefixed, ct)
            ?? throw new SalesDomainException(SalesError.InvalidSale, "Sale not found.");
        return ToTicket(sale);
    }

    private async Task<TicketSummary> GetSaleByIdCoreAsync(Guid saleId, CancellationToken ct)
    {
        var context = Require(currentTenant);
        var sale = await db.Set<Sale>()
            .AsNoTracking()
            .Include(x => x.Lines)
            .Include(x => x.PaymentCaptures)
            .SingleOrDefaultAsync(x => x.TenantId == context.TenantId && x.Id == saleId, ct)
            ?? throw new SalesDomainException(SalesError.InvalidSale, "Sale not found.");
        return ToTicket(sale);
    }

    private static async Task<Result<T>> Capture<T>(Func<Task<T>> action)
    {
        try
        {
            return ResultFactory.Ok(await action());
        }
        catch (SalesDomainException ex)
        {
            return ResultFactory.Fail<T>(SalesErrorMapping.ToDomainError(ex));
        }
    }
}

public sealed class OpenSalesSessionHandler(
    BinexusDbContext db,
    ICurrentTenant currentTenant,
    SalesFeatureGate featureGate,
    IIdGenerator ids,
    TimeProvider clock) : ICommandHandler<OpenSalesSessionCommand>
{
    public Task<Result> HandleAsync(OpenSalesSessionCommand command, CancellationToken cancellationToken) => Capture(async () =>
    {
        var context = Require(currentTenant);
        EnsurePosRole(context);
        await featureGate.EnsureEnabledAsync(context.TenantId, cancellationToken);

        var operationKey = OperationKey("sales-session-open", context.TenantId, command.OperationKey);
        var existing = await db.Set<SalesSession>()
            .SingleOrDefaultAsync(x => x.TenantId == context.TenantId && x.OpenOperationKey == operationKey, cancellationToken);
        if (existing is not null)
        {
            if (!MatchesOpenPayload(existing, command.Request, context.BranchId))
            {
                throw new SalesDomainException(SalesError.IdempotencyKeyReused, "Idempotency-Key was already used with a different payload.");
            }

            return Result.Success();
        }

        var branchId = command.Request.BranchId ?? context.BranchId
            ?? throw new SalesDomainException(SalesError.InvalidSession, "branchId is required to open a sales session.");
        await EnsureBranchBelongsToTenantAsync(db, context.TenantId, branchId, cancellationToken);

        var terminal = SalesSession.NormalizeTerminal(command.Request.TerminalId);
        var currency = SalesSession.NormalizeCurrency(string.IsNullOrWhiteSpace(command.Request.Currency) ? "MXN" : command.Request.Currency!);
        if (command.Request.OpeningFloatCents < 0)
        {
            throw new SalesDomainException(SalesError.InvalidSession, "openingFloatCents must be a non-negative integer.");
        }

        var openConflict = await db.Set<SalesSession>().AnyAsync(
            x => x.TenantId == context.TenantId
                && x.BranchId == branchId
                && x.TerminalId == terminal
                && x.Status == SalesSessionStatus.Open,
            cancellationToken);
        if (openConflict)
        {
            throw new SalesDomainException(
                SalesError.SessionAlreadyOpen,
                $"Terminal {terminal} already has an OPEN sales session on this branch.");
        }

        var now = clock.GetUtcNow();
        var session = new SalesSession(
            command.SessionId,
            context.TenantId,
            branchId,
            terminal,
            command.Request.OpeningFloatCents,
            currency,
            Actor(context),
            now,
            operationKey);
        db.Add(session);
        Record(
            db,
            ids,
            context.TenantId,
            "SALES_SESSION_OPENED",
            new
            {
                sessionId = session.Id,
                branchId = session.BranchId,
                terminalId = session.TerminalId,
                openingFloatCents = session.OpeningFloatCents,
                currency = session.Currency,
                openedBy = session.OpenedByUserId,
            },
            command.CorrelationId,
            clock);
        return Result.Success();
    });
}

public sealed class CreateSaleHandler(
    BinexusDbContext db,
    ICurrentTenant currentTenant,
    SalesFeatureGate featureGate,
    IInventorySaleApi inventory,
    IIdGenerator ids,
    TimeProvider clock) : ICommandHandler<CreateSaleCommand>
{
    public Task<Result> HandleAsync(CreateSaleCommand command, CancellationToken cancellationToken) => Capture(async () =>
    {
        var context = Require(currentTenant);
        EnsurePosRole(context);
        await featureGate.EnsureEnabledAsync(context.TenantId, cancellationToken);

        var operationKey = OperationKey("sales-create", context.TenantId, command.OperationKey);
        var existing = await db.Set<Sale>()
            .Include(x => x.Lines)
            .Include(x => x.PaymentCaptures)
            .SingleOrDefaultAsync(x => x.TenantId == context.TenantId && x.OperationKey == operationKey, cancellationToken);
        if (existing is not null)
        {
            if (!MatchesCreatePayload(existing, command.Request))
            {
                throw new SalesDomainException(SalesError.IdempotencyKeyReused, "Idempotency-Key was already used with a different payload.");
            }

            return Result.Success();
        }

        await LockSessionForUpdateAsync(db, context.TenantId, command.SessionId, cancellationToken);
        var session = await LoadSessionAsync(db, context.TenantId, command.SessionId, tracking: true, cancellationToken);
        if (session.Status != SalesSessionStatus.Open)
        {
            throw new SalesDomainException(SalesError.SessionNotOpen, "Sales session must be OPEN to create a sale.");
        }

        if (context.BranchId is Guid jwtBranch && jwtBranch != session.BranchId)
        {
            throw new SalesDomainException(SalesError.InvalidBranch, "Sales session does not belong to the caller's branch.");
        }

        var currency = SalesSession.NormalizeCurrency(string.IsNullOrWhiteSpace(command.Request.Currency) ? "MXN" : command.Request.Currency!);
        if (!string.Equals(session.Currency, currency, StringComparison.Ordinal))
        {
            throw new SalesDomainException(SalesError.CurrencyMismatch, "Sale currency must match the session currency.");
        }

        if (command.Request.Lines is null || command.Request.Lines.Count == 0)
        {
            throw new SalesDomainException(SalesError.InvalidSale, "At least one line is required.");
        }

        if (command.Request.Payments is null || command.Request.Payments.Count == 0)
        {
            throw new SalesDomainException(SalesError.InvalidSale, "payments must include at least one capture.");
        }

        var now = clock.GetUtcNow();
        var lineEntities = new List<SaleLine>(command.Request.Lines.Count);
        foreach (var line in command.Request.Lines)
        {
            lineEntities.Add(new SaleLine(
                ids.NewId(),
                context.TenantId,
                command.SaleId,
                line.ProductId,
                line.ProductName,
                line.Quantity,
                line.UnitPriceCents));
        }

        var paymentEntities = new List<PaymentCapture>(command.Request.Payments.Count);
        foreach (var payment in command.Request.Payments)
        {
            paymentEntities.Add(new PaymentCapture(
                ids.NewId(),
                context.TenantId,
                command.SaleId,
                session.Id,
                Sale.ParsePaymentMethod(payment.Method),
                payment.AmountCents,
                currency,
                now));
        }

        var sale = new Sale(
            command.SaleId,
            context.TenantId,
            session.Id,
            session.BranchId,
            session.TerminalId,
            currency,
            Actor(context),
            lineEntities,
            paymentEntities,
            now,
            operationKey);

        var inventoryResult = await inventory.DecrementForSaleAsync(
            new InventorySaleDecrementRequest(
                context.TenantId,
                sale.Id,
                sale.Lines.Select(l => new InventorySaleLine(session.BranchId, l.Id, l.ProductId, l.Quantity)).ToArray()),
            cancellationToken);
        if (!inventoryResult.Succeeded)
        {
            var insufficient = string.Equals(inventoryResult.FailureCode, "INSUFFICIENT_STOCK", StringComparison.Ordinal);
            throw new SalesDomainException(
                insufficient ? SalesError.InsufficientStock : SalesError.InvalidSale,
                insufficient
                    ? "Insufficient stock for one or more sale lines."
                    : $"Inventory decrement failed ({inventoryResult.FailureCode}).");
        }

        db.Add(sale);

        Record(
            db,
            ids,
            context.TenantId,
            "SALE_CREATED",
            new
            {
                saleId = sale.Id,
                ticketId = sale.Id,
                sessionId = sale.SessionId,
                branchId = sale.BranchId,
                terminalId = sale.TerminalId,
                cashierId = sale.CashierUserId,
                customerLabel = sale.CustomerLabel,
                totalCents = sale.TotalCents,
                currency = sale.Currency,
                lines = sale.Lines.Select(l => new
                {
                    productId = l.ProductId,
                    productName = l.ProductName,
                    quantity = l.Quantity,
                    unitPriceCents = l.UnitPriceCents,
                    lineTotalCents = l.LineTotalCents,
                }),
                payments = sale.PaymentCaptures.Select(p => new
                {
                    method = SalesPersistedEnums.ToApi(p.Method),
                    amountCents = p.AmountCents,
                }),
            },
            command.CorrelationId,
            clock);

        foreach (var capture in sale.PaymentCaptures)
        {
            Record(
                db,
                ids,
                context.TenantId,
                "PAYMENT_REGISTERED",
                new
                {
                    paymentId = capture.Id,
                    saleId = sale.Id,
                    sessionId = sale.SessionId,
                    amountCents = capture.AmountCents,
                    currency = capture.Currency,
                    method = SalesPersistedEnums.ToApi(capture.Method),
                },
                command.CorrelationId,
                clock);
        }

        return Result.Success();
    });
}

public sealed class CloseSalesSessionHandler(
    BinexusDbContext db,
    ICurrentTenant currentTenant,
    SalesFeatureGate featureGate,
    IIdGenerator ids,
    TimeProvider clock) : ICommandHandler<CloseSalesSessionCommand>
{
    public Task<Result> HandleAsync(CloseSalesSessionCommand command, CancellationToken cancellationToken) => Capture(async () =>
    {
        var context = Require(currentTenant);
        EnsurePosRole(context);
        await featureGate.EnsureEnabledAsync(context.TenantId, cancellationToken);

        var operationKey = OperationKey("sales-session-close", context.TenantId, command.OperationKey);
        await LockSessionForUpdateAsync(db, context.TenantId, command.SessionId, cancellationToken);
        var session = await LoadSessionAsync(db, context.TenantId, command.SessionId, tracking: true, cancellationToken);

        if (session.CloseOperationKey == operationKey)
        {
            if (!MatchesClosePayload(session, command.Request))
            {
                throw new SalesDomainException(SalesError.IdempotencyKeyReused, "Idempotency-Key was already used with a different payload.");
            }

            // Snapshot already frozen — do not recompute expected cash.
            return Result.Success();
        }

        if (session.Status != SalesSessionStatus.Open)
        {
            throw new SalesDomainException(SalesError.SessionAlreadyClosed, "Sales session is already closed.");
        }

        if (await db.Set<SalesSession>().AnyAsync(
                x => x.TenantId == context.TenantId && x.CloseOperationKey == operationKey,
                cancellationToken))
        {
            throw new SalesDomainException(SalesError.IdempotencyKeyReused, "Idempotency-Key was already used for a different operation.");
        }

        var expectedCents = await ComputeExpectedCashCentsAsync(db, session, cancellationToken);
        var declaredCents = command.Request.DeclaredClosingCents;
        if (declaredCents < 0)
        {
            throw new SalesDomainException(SalesError.InvalidClose, "declaredClosingCents must be a non-negative integer.");
        }

        var discrepancyCents = declaredCents - expectedCents;
        AssertCashDiscrepancyCloseAllowed(discrepancyCents != 0, context.Role, command.Request.DiscrepancyReason);

        var now = clock.GetUtcNow();
        session.Close(
            Actor(context),
            expectedCents,
            declaredCents,
            command.Request.DiscrepancyReason,
            command.Request.Notes,
            now,
            operationKey);

        Record(
            db,
            ids,
            context.TenantId,
            "SALES_SESSION_CLOSED",
            new
            {
                sessionId = session.Id,
                branchId = session.BranchId,
                terminalId = session.TerminalId,
                expectedClosingCents = session.ExpectedClosingCents,
                declaredClosingCents = session.DeclaredClosingCents,
                discrepancyCents = session.DiscrepancyCents,
                currency = session.Currency,
                closedBy = session.ClosedByUserId,
            },
            command.CorrelationId,
            clock);
        return Result.Success();
    });
}

internal static class SalesCommandSupport
{
    internal static TenantContext Require(ICurrentTenant tenant) =>
        tenant.Current ?? throw new SalesDomainException(SalesError.Forbidden, "Tenant context is required.");

    internal static Guid Actor(TenantContext context) =>
        context.UserId ?? throw new SalesDomainException(SalesError.Forbidden, "User context is required.");

    internal static void EnsurePosRole(TenantContext context)
    {
        if (context.Role is not ("CASHIER" or "ADMIN" or "SUPER_ADMIN"))
        {
            throw new SalesDomainException(SalesError.Forbidden, "Sales requires CASHIER, ADMIN, or SUPER_ADMIN.");
        }
    }

    internal static void AssertCashDiscrepancyCloseAllowed(bool hasDiscrepancy, string? role, string? discrepancyReason)
    {
        if (!hasDiscrepancy)
        {
            return;
        }

        if (role is not ("ADMIN" or "SUPER_ADMIN"))
        {
            throw new SalesDomainException(
                SalesError.DiscrepancyForbidden,
                "Closing with a cash discrepancy requires ADMIN or SUPER_ADMIN role.");
        }

        if (string.IsNullOrWhiteSpace(discrepancyReason))
        {
            throw new SalesDomainException(
                SalesError.DiscrepancyReasonRequired,
                "discrepancyReason is required when declaredCents does not match expectedCents.");
        }
    }

    internal static async Task EnsureBranchBelongsToTenantAsync(
        BinexusDbContext db,
        Guid tenantId,
        Guid branchId,
        CancellationToken ct)
    {
        var exists = await db.Database
            .SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM branches WHERE id = {branchId} AND tenant_id = {tenantId}")
            .SingleAsync(ct);
        if (exists == 0)
        {
            throw new SalesDomainException(SalesError.InvalidBranch, "branchId does not belong to the current tenant.");
        }
    }

    /// <summary>
    /// Serializes create-sale / close per session row so concurrent sales queue instead of
    /// fighting on xmin via Touch, and a sale cannot commit after close without being visible to arqueo.
    /// </summary>
    internal static Task LockSessionForUpdateAsync(
        BinexusDbContext db,
        Guid tenantId,
        Guid sessionId,
        CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM sales_sessions WHERE id = {sessionId} AND tenant_id = {tenantId} FOR UPDATE",
            ct);

    internal static async Task<SalesSession> LoadSessionAsync(
        BinexusDbContext db,
        Guid tenantId,
        Guid sessionId,
        bool tracking,
        CancellationToken ct)
    {
        var query = tracking ? db.Set<SalesSession>().AsQueryable() : db.Set<SalesSession>().AsNoTracking();
        return await query.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == sessionId, ct)
            ?? throw new SalesDomainException(SalesError.SessionNotFound, $"Sales session {sessionId} not found");
    }

    internal static async Task<int> ComputeExpectedCashCentsAsync(
        BinexusDbContext db,
        SalesSession session,
        CancellationToken ct)
    {
        var cashPayments = await db.Set<PaymentCapture>()
            .AsNoTracking()
            .Where(x => x.TenantId == session.TenantId && x.SessionId == session.Id && x.Method == PaymentCaptureMethod.Cash)
            .Select(x => new { x.AmountCents, x.Currency })
            .ToListAsync(ct);

        foreach (var payment in cashPayments)
        {
            if (!string.Equals(payment.Currency, session.Currency, StringComparison.Ordinal))
            {
                throw new SalesDomainException(
                    SalesError.SessionCashCurrencyMismatch,
                    "Cash payments in this session use multiple currencies.");
            }
        }

        try
        {
            checked
            {
                var cashSalesCents = 0;
                foreach (var payment in cashPayments)
                {
                    cashSalesCents += payment.AmountCents;
                }

                return session.OpeningFloatCents + cashSalesCents;
            }
        }
        catch (OverflowException)
        {
            throw new SalesDomainException(SalesError.InvalidClose, "Expected closing cash overflowed.");
        }
    }

    internal static bool MatchesOpenPayload(SalesSession existing, OpenSalesSessionRequest request, Guid? jwtBranchId)
    {
        var branchId = request.BranchId ?? jwtBranchId ?? existing.BranchId;
        var currency = SalesSession.NormalizeCurrency(string.IsNullOrWhiteSpace(request.Currency) ? "MXN" : request.Currency!);
        return existing.BranchId == branchId
            && string.Equals(existing.TerminalId, SalesSession.NormalizeTerminal(request.TerminalId), StringComparison.Ordinal)
            && existing.OpeningFloatCents == request.OpeningFloatCents
            && string.Equals(existing.Currency, currency, StringComparison.Ordinal);
    }

    internal static bool MatchesCreatePayload(Sale existing, CreateSaleRequest request)
    {
        var currency = SalesSession.NormalizeCurrency(string.IsNullOrWhiteSpace(request.Currency) ? "MXN" : request.Currency!);
        if (!string.Equals(existing.Currency, currency, StringComparison.Ordinal)
            || existing.Lines.Count != request.Lines.Count
            || existing.PaymentCaptures.Count != request.Payments.Count)
        {
            return false;
        }

        var existingLines = existing.Lines.OrderBy(x => x.ProductId).ThenBy(x => x.Quantity).ThenBy(x => x.UnitPriceCents).ToArray();
        var requestLines = request.Lines.OrderBy(x => x.ProductId).ThenBy(x => x.Quantity).ThenBy(x => x.UnitPriceCents).ToArray();
        for (var i = 0; i < existingLines.Length; i++)
        {
            if (!string.Equals(existingLines[i].ProductId, requestLines[i].ProductId.Trim(), StringComparison.Ordinal)
                || !string.Equals(existingLines[i].ProductName, requestLines[i].ProductName.Trim(), StringComparison.Ordinal)
                || existingLines[i].Quantity != requestLines[i].Quantity
                || existingLines[i].UnitPriceCents != requestLines[i].UnitPriceCents)
            {
                return false;
            }
        }

        var existingPayments = existing.PaymentCaptures.OrderBy(x => SalesPersistedEnums.ToApi(x.Method)).ThenBy(x => x.AmountCents).ToArray();
        var requestPayments = request.Payments
            .Select(p => (Method: Sale.ParsePaymentMethod(p.Method), p.AmountCents))
            .OrderBy(x => SalesPersistedEnums.ToApi(x.Method))
            .ThenBy(x => x.AmountCents)
            .ToArray();
        for (var i = 0; i < existingPayments.Length; i++)
        {
            if (existingPayments[i].Method != requestPayments[i].Method
                || existingPayments[i].AmountCents != requestPayments[i].AmountCents)
            {
                return false;
            }
        }

        return true;
    }

    internal static bool MatchesClosePayload(SalesSession existing, CloseSalesSessionRequest request)
    {
        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        var reason = string.IsNullOrWhiteSpace(request.DiscrepancyReason) ? null : request.DiscrepancyReason.Trim();
        if (existing.DeclaredClosingCents != request.DeclaredClosingCents
            || !string.Equals(existing.CloseNotes, notes, StringComparison.Ordinal))
        {
            return false;
        }

        if (existing.DiscrepancyCents is null or 0)
        {
            return true;
        }

        return string.Equals(existing.DiscrepancyReason, reason, StringComparison.Ordinal);
    }

    internal static SalesSessionSummary ToSummary(SalesSession session) => new(
        session.Id,
        session.BranchId,
        session.TerminalId,
        SalesPersistedEnums.ToApi(session.Status),
        session.OpeningFloatCents,
        session.Currency,
        session.OpenedByUserId,
        session.OpenedAtUtc,
        session.ClosedByUserId,
        session.ClosedAtUtc,
        session.ExpectedClosingCents,
        session.DeclaredClosingCents,
        session.DiscrepancyCents,
        session.DiscrepancyReason,
        session.CloseNotes);

    internal static TicketSummary ToTicket(Sale sale) => new(
        sale.Id,
        sale.SessionId,
        sale.BranchId,
        sale.TerminalId,
        sale.CustomerLabel,
        SalesPersistedEnums.ToApi(sale.Status),
        sale.TotalCents,
        sale.Currency,
        sale.CashierUserId,
        sale.CreatedAtUtc,
        sale.Lines.Select(l => new TicketLineSummary(l.ProductId, l.ProductName, l.Quantity, l.UnitPriceCents, l.LineTotalCents)).ToArray(),
        sale.PaymentCaptures.Select(p => new PaymentCaptureSummary(
            p.Id,
            SalesPersistedEnums.ToApi(p.Method),
            p.AmountCents,
            p.Currency,
            p.CapturedAtUtc)).ToArray());

    internal static string OperationKey(string prefix, Guid tenantId, string key) =>
        $"{prefix}:{tenantId:D}:{key}";

    internal static void Record(
        BinexusDbContext db,
        IIdGenerator ids,
        Guid tenantId,
        string name,
        object payload,
        string? correlationId,
        TimeProvider clock)
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

    internal static async Task<Result> Capture(Func<Task<Result>> action)
    {
        try
        {
            return await action();
        }
        catch (SalesDomainException ex)
        {
            return Result.Failure(SalesErrorMapping.ToDomainError(ex));
        }
    }
}
