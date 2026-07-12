using Binexus.SharedKernel.Abstractions;

namespace Binexus.Modules.Sales.Domain;

public enum SalesSessionStatus
{
    Open,
    Closed,
}

public enum SaleStatus
{
    Completed,
}

public enum PaymentCaptureMethod
{
    Cash,
    Card,
    Transfer,
}

public static class SalesConstants
{
    public const string WalkInCustomerLabel = "walk-in";
    public const int MinTerminalLength = 1;
    public const int MaxTerminalLength = 50;
}

public sealed class SalesSession : ITenantScoped
{
    private SalesSession()
    {
    }

    public SalesSession(
        Guid id,
        Guid tenantId,
        Guid branchId,
        string terminalId,
        int openingFloatCents,
        string currency,
        Guid openedByUserId,
        DateTimeOffset now,
        string openOperationKey)
    {
        if (id == Guid.Empty) throw new SalesDomainException(SalesError.InvalidSession, "Session id is required.");
        if (tenantId == Guid.Empty) throw new SalesDomainException(SalesError.InvalidSession, "Tenant is required.");
        if (branchId == Guid.Empty) throw new SalesDomainException(SalesError.InvalidSession, "Branch is required.");
        if (openedByUserId == Guid.Empty) throw new SalesDomainException(SalesError.InvalidSession, "Opened-by user is required.");
        if (string.IsNullOrWhiteSpace(openOperationKey)) throw new SalesDomainException(SalesError.InvalidSession, "Open operation key is required.");

        var terminal = NormalizeTerminal(terminalId);
        if (openingFloatCents < 0) throw new SalesDomainException(SalesError.InvalidSession, "openingFloatCents must be a non-negative integer.");
        var normalizedCurrency = NormalizeCurrency(currency);

        Id = id;
        TenantId = tenantId;
        BranchId = branchId;
        TerminalId = terminal;
        Status = SalesSessionStatus.Open;
        OpeningFloatCents = openingFloatCents;
        Currency = normalizedCurrency;
        OpenedByUserId = openedByUserId;
        OpenedAtUtc = now;
        OpenOperationKey = openOperationKey;
        CreatedAtUtc = UpdatedAtUtc = now;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public string TerminalId { get; private set; } = string.Empty;
    public SalesSessionStatus Status { get; private set; }
    public int OpeningFloatCents { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public Guid OpenedByUserId { get; private set; }
    public DateTimeOffset OpenedAtUtc { get; private set; }
    public Guid? ClosedByUserId { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }
    public int? ExpectedClosingCents { get; private set; }
    public int? DeclaredClosingCents { get; private set; }
    public int? DiscrepancyCents { get; private set; }
    public string? DiscrepancyReason { get; private set; }
    public string? CloseNotes { get; private set; }
    public string OpenOperationKey { get; private set; } = string.Empty;
    public string? CloseOperationKey { get; private set; }
    public uint Version { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void Close(
        Guid closedByUserId,
        int expectedClosingCents,
        int declaredClosingCents,
        string? discrepancyReason,
        string? notes,
        DateTimeOffset now,
        string closeOperationKey)
    {
        if (closedByUserId == Guid.Empty) throw new SalesDomainException(SalesError.InvalidSession, "Closed-by user is required.");
        if (string.IsNullOrWhiteSpace(closeOperationKey)) throw new SalesDomainException(SalesError.InvalidSession, "Close operation key is required.");
        if (Status != SalesSessionStatus.Open)
        {
            throw new SalesDomainException(SalesError.SessionAlreadyClosed, "Sales session is already closed.");
        }

        if (declaredClosingCents < 0)
        {
            throw new SalesDomainException(SalesError.InvalidClose, "declaredClosingCents must be a non-negative integer.");
        }

        var discrepancy = declaredClosingCents - expectedClosingCents;
        Status = SalesSessionStatus.Closed;
        ClosedByUserId = closedByUserId;
        ClosedAtUtc = now;
        ExpectedClosingCents = expectedClosingCents;
        DeclaredClosingCents = declaredClosingCents;
        DiscrepancyCents = discrepancy;
        DiscrepancyReason = discrepancy == 0 ? null : discrepancyReason?.Trim();
        CloseNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        CloseOperationKey = closeOperationKey;
        UpdatedAtUtc = now;
    }

    public static string NormalizeTerminal(string terminalId)
    {
        var terminal = terminalId?.Trim() ?? string.Empty;
        if (terminal.Length is < SalesConstants.MinTerminalLength or > SalesConstants.MaxTerminalLength)
        {
            throw new SalesDomainException(
                SalesError.InvalidSession,
                $"terminalId must be between {SalesConstants.MinTerminalLength} and {SalesConstants.MaxTerminalLength} characters.");
        }

        return terminal;
    }

    public static string NormalizeCurrency(string currency)
    {
        var normalized = currency?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalized.Length != 3 || normalized.Any(c => c is < 'A' or > 'Z'))
        {
            throw new SalesDomainException(SalesError.InvalidSession, "currency must be a 3-letter ISO code.");
        }

        return normalized;
    }
}

public sealed class Sale : ITenantScoped
{
    private readonly List<SaleLine> _lines = [];
    private readonly List<PaymentCapture> _paymentCaptures = [];

    private Sale()
    {
    }

    public Sale(
        Guid id,
        Guid tenantId,
        Guid sessionId,
        Guid branchId,
        string terminalId,
        string currency,
        Guid cashierUserId,
        IEnumerable<SaleLine> lines,
        IEnumerable<PaymentCapture> payments,
        DateTimeOffset now,
        string? operationKey)
    {
        if (id == Guid.Empty) throw new SalesDomainException(SalesError.InvalidSale, "Sale id is required.");
        if (tenantId == Guid.Empty) throw new SalesDomainException(SalesError.InvalidSale, "Tenant is required.");
        if (sessionId == Guid.Empty) throw new SalesDomainException(SalesError.InvalidSale, "Session is required.");
        if (branchId == Guid.Empty) throw new SalesDomainException(SalesError.InvalidSale, "Branch is required.");
        if (cashierUserId == Guid.Empty) throw new SalesDomainException(SalesError.InvalidSale, "Cashier is required.");

        var materializedLines = lines.ToArray();
        var materializedPayments = payments.ToArray();
        if (materializedLines.Length == 0)
        {
            throw new SalesDomainException(SalesError.InvalidSale, "At least one line is required.");
        }

        if (materializedPayments.Length == 0)
        {
            throw new SalesDomainException(SalesError.InvalidSale, "payments must include at least one capture.");
        }

        foreach (var payment in materializedPayments)
        {
            if (payment.TenantId != tenantId || payment.SaleId != id || payment.SessionId != sessionId)
            {
                throw new SalesDomainException(
                    SalesError.InvalidPayment,
                    "Payment captures must share the sale tenant, sale id, and session id.");
            }
        }

        var total = 0;
        try
        {
            checked
            {
                foreach (var line in materializedLines)
                {
                    total += line.LineTotalCents;
                }
            }
        }
        catch (OverflowException)
        {
            throw new SalesDomainException(SalesError.InvalidSale, "Sale total overflowed.");
        }

        ValidatePayments(materializedPayments, total, currency);

        Id = id;
        TenantId = tenantId;
        SessionId = sessionId;
        BranchId = branchId;
        TerminalId = terminalId;
        CustomerLabel = SalesConstants.WalkInCustomerLabel;
        Status = SaleStatus.Completed;
        TotalCents = total;
        Currency = SalesSession.NormalizeCurrency(currency);
        CashierUserId = cashierUserId;
        CreatedAtUtc = now;
        OperationKey = operationKey;
        _lines.AddRange(materializedLines);
        _paymentCaptures.AddRange(materializedPayments);
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid BranchId { get; private set; }
    public string TerminalId { get; private set; } = string.Empty;
    public string CustomerLabel { get; private set; } = SalesConstants.WalkInCustomerLabel;
    public SaleStatus Status { get; private set; }
    public int TotalCents { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public Guid CashierUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public string? OperationKey { get; private set; }
    public IReadOnlyCollection<SaleLine> Lines => _lines;
    public IReadOnlyCollection<PaymentCapture> PaymentCaptures => _paymentCaptures;

    public static void ValidatePayments(IReadOnlyList<PaymentCapture> payments, int totalCents, string currency)
    {
        var sum = 0;
        try
        {
            checked
            {
                foreach (var payment in payments)
                {
                    if (payment.AmountCents <= 0)
                    {
                        throw new SalesDomainException(SalesError.InvalidPayment, "Each payment amountCents must be a positive integer.");
                    }

                    if (!string.Equals(payment.Currency, SalesSession.NormalizeCurrency(currency), StringComparison.Ordinal))
                    {
                        throw new SalesDomainException(SalesError.InvalidPayment, "Payment currency must match the sale currency.");
                    }

                    sum += payment.AmountCents;
                }
            }
        }
        catch (OverflowException)
        {
            throw new SalesDomainException(SalesError.InvalidPayment, "Payment sum overflowed.");
        }

        if (sum != totalCents)
        {
            throw new SalesDomainException(
                SalesError.PaymentSumMismatch,
                $"Payment captures must sum to ticket total ({totalCents} cents); received {sum} cents.");
        }
    }

    public static PaymentCaptureMethod ParsePaymentMethod(string method)
    {
        var normalized = method?.Trim().ToUpperInvariant() ?? string.Empty;
        return normalized switch
        {
            "CASH" => PaymentCaptureMethod.Cash,
            "CARD" => PaymentCaptureMethod.Card,
            "TRANSFER" => PaymentCaptureMethod.Transfer,
            "CREDIT" => throw new SalesDomainException(
                SalesError.CreditNotSupported,
                "CREDIT payment is not supported in POS walk-in sales (deferred to 5.3)."),
            _ => throw new SalesDomainException(SalesError.InvalidPayment, $"Invalid payment method: {method}"),
        };
    }
}

public sealed class SaleLine : ITenantScoped
{
    private SaleLine()
    {
    }

    public SaleLine(
        Guid id,
        Guid tenantId,
        Guid saleId,
        string productId,
        string productName,
        int quantity,
        int unitPriceCents)
    {
        if (id == Guid.Empty) throw new SalesDomainException(SalesError.InvalidSale, "Sale line id is required.");
        if (tenantId == Guid.Empty) throw new SalesDomainException(SalesError.InvalidSale, "Tenant is required.");
        if (saleId == Guid.Empty) throw new SalesDomainException(SalesError.InvalidSale, "Sale is required.");
        if (string.IsNullOrWhiteSpace(productId) || productId.Trim().Length > 256)
        {
            throw new SalesDomainException(SalesError.InvalidSale, "Each line requires productId and productName.");
        }

        if (string.IsNullOrWhiteSpace(productName) || productName.Trim().Length > 256)
        {
            throw new SalesDomainException(SalesError.InvalidSale, "Each line requires productId and productName.");
        }

        if (quantity < 1) throw new SalesDomainException(SalesError.InvalidSale, "quantity must be a positive integer.");
        if (unitPriceCents < 0) throw new SalesDomainException(SalesError.InvalidSale, "unitPriceCents must be a non-negative integer.");

        int lineTotal;
        try
        {
            checked
            {
                lineTotal = quantity * unitPriceCents;
            }
        }
        catch (OverflowException)
        {
            throw new SalesDomainException(SalesError.InvalidSale, "Line total overflowed.");
        }

        Id = id;
        TenantId = tenantId;
        SaleId = saleId;
        ProductId = productId.Trim();
        ProductName = productName.Trim();
        Quantity = quantity;
        UnitPriceCents = unitPriceCents;
        LineTotalCents = lineTotal;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SaleId { get; private set; }
    public string ProductId { get; private set; } = string.Empty;
    public string ProductName { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public int UnitPriceCents { get; private set; }
    public int LineTotalCents { get; private set; }
}

public sealed class PaymentCapture : ITenantScoped
{
    private PaymentCapture()
    {
    }

    public PaymentCapture(
        Guid id,
        Guid tenantId,
        Guid saleId,
        Guid sessionId,
        PaymentCaptureMethod method,
        int amountCents,
        string currency,
        DateTimeOffset capturedAtUtc)
    {
        if (id == Guid.Empty) throw new SalesDomainException(SalesError.InvalidPayment, "Payment id is required.");
        if (tenantId == Guid.Empty) throw new SalesDomainException(SalesError.InvalidPayment, "Tenant is required.");
        if (saleId == Guid.Empty) throw new SalesDomainException(SalesError.InvalidPayment, "Sale is required.");
        if (sessionId == Guid.Empty) throw new SalesDomainException(SalesError.InvalidPayment, "Session is required.");
        if (amountCents <= 0) throw new SalesDomainException(SalesError.InvalidPayment, "Each payment amountCents must be a positive integer.");

        Id = id;
        TenantId = tenantId;
        SaleId = saleId;
        SessionId = sessionId;
        Method = method;
        AmountCents = amountCents;
        Currency = SalesSession.NormalizeCurrency(currency);
        CapturedAtUtc = capturedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SaleId { get; private set; }
    public Guid SessionId { get; private set; }
    public PaymentCaptureMethod Method { get; private set; }
    public int AmountCents { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DateTimeOffset CapturedAtUtc { get; private set; }
}

public static class SalesError
{
    public const string InvalidSession = "INVALID_SALES_SESSION";
    public const string InvalidSale = "INVALID_SALE";
    public const string InvalidPayment = "INVALID_PAYMENT";
    public const string InvalidClose = "INVALID_CLOSE";
    public const string InvalidBranch = "INVALID_BRANCH";
    public const string SessionNotFound = "SALES_SESSION_NOT_FOUND";
    public const string SessionAlreadyOpen = "SALES_SESSION_ALREADY_OPEN";
    public const string SessionAlreadyClosed = "SALES_SESSION_ALREADY_CLOSED";
    public const string SessionNotOpen = "SALES_SESSION_NOT_OPEN";
    public const string PaymentSumMismatch = "PAYMENT_SUM_MISMATCH";
    public const string CreditNotSupported = "CREDIT_NOT_SUPPORTED";
    public const string InsufficientStock = "INSUFFICIENT_STOCK";
    public const string FeatureDisabled = "FEATURE_DISABLED";
    public const string Forbidden = "FORBIDDEN";
    public const string DiscrepancyForbidden = "DISCREPANCY_CLOSE_FORBIDDEN";
    public const string DiscrepancyReasonRequired = "DISCREPANCY_REASON_REQUIRED";
    public const string SessionCashCurrencyMismatch = "SESSION_CASH_CURRENCY_MISMATCH";
    public const string IdempotencyKeyReused = "IDEMPOTENCY_KEY_REUSED";
    public const string CurrencyMismatch = "SALE_CURRENCY_MISMATCH";
}

public sealed class SalesDomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
