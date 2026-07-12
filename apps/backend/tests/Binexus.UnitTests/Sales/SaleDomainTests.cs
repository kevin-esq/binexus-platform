using Binexus.Modules.Sales.Domain;
using FluentAssertions;

namespace Binexus.UnitTests.Sales;

public sealed class SaleDomainTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SessionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SaleId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid BranchId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid CashierId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-12T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void Sale_rejects_payment_capture_with_mismatched_session()
    {
        var line = new SaleLine(Guid.NewGuid(), TenantId, SaleId, "sku-1", "Water", 1, 1000);
        var payment = new PaymentCapture(
            Guid.NewGuid(),
            TenantId,
            SaleId,
            Guid.NewGuid(),
            PaymentCaptureMethod.Cash,
            1000,
            "MXN",
            Now);

        var act = () => new Sale(
            SaleId,
            TenantId,
            SessionId,
            BranchId,
            "POS-1",
            "MXN",
            CashierId,
            [line],
            [payment],
            Now,
            "op");

        act.Should().Throw<SalesDomainException>()
            .Which.Code.Should().Be(SalesError.InvalidPayment);
    }

    [Fact]
    public void Payment_captures_are_immutable_value_records_after_construction()
    {
        var payment = new PaymentCapture(
            Guid.NewGuid(),
            TenantId,
            SaleId,
            SessionId,
            PaymentCaptureMethod.Card,
            500,
            "MXN",
            Now);

        payment.Method.Should().Be(PaymentCaptureMethod.Card);
        payment.AmountCents.Should().Be(500);
        payment.GetType().GetProperty(nameof(PaymentCapture.AmountCents))!.GetSetMethod().Should().BeNull();
        payment.GetType().GetProperty(nameof(PaymentCapture.Method))!.GetSetMethod().Should().BeNull();
    }

    [Fact]
    public void ParsePaymentMethod_rejects_credit_and_unknown()
    {
        var credit = () => Sale.ParsePaymentMethod("CREDIT");
        credit.Should().Throw<SalesDomainException>().Which.Code.Should().Be(SalesError.CreditNotSupported);

        var unknown = () => Sale.ParsePaymentMethod("CRYPTO");
        unknown.Should().Throw<SalesDomainException>().Which.Code.Should().Be(SalesError.InvalidPayment);
    }

    [Fact]
    public void Sale_id_is_the_ticket_identity()
    {
        var line = new SaleLine(Guid.NewGuid(), TenantId, SaleId, "sku-1", "Water", 1, 1000);
        var payment = new PaymentCapture(
            Guid.NewGuid(),
            TenantId,
            SaleId,
            SessionId,
            PaymentCaptureMethod.Cash,
            1000,
            "MXN",
            Now);

        var sale = new Sale(
            SaleId,
            TenantId,
            SessionId,
            BranchId,
            "POS-1",
            "MXN",
            CashierId,
            [line],
            [payment],
            Now,
            "op");

        sale.Id.Should().Be(SaleId);
        sale.CustomerLabel.Should().Be(SalesConstants.WalkInCustomerLabel);
        sale.Status.Should().Be(SaleStatus.Completed);
    }

    [Fact]
    public void Opening_cash_discrepancy_uses_checked_arithmetic_on_close()
    {
        var session = new SalesSession(
            Guid.NewGuid(),
            TenantId,
            BranchId,
            "POS-1",
            openingFloatCents: 100,
            "MXN",
            CashierId,
            Now,
            "open-key");

        session.Close(CashierId, expectedClosingCents: 150, declaredClosingCents: 140, discrepancyReason: "short", notes: null, Now, "close-key");
        session.DiscrepancyCents.Should().Be(-10);
        session.ExpectedClosingCents.Should().Be(150);

        var overflow = () => session.Close(
            CashierId,
            expectedClosingCents: int.MaxValue,
            declaredClosingCents: int.MaxValue,
            discrepancyReason: null,
            notes: null,
            Now.AddSeconds(1),
            "close-2");
        // Already closed — second close fails before arithmetic.
        overflow.Should().Throw<SalesDomainException>().Which.Code.Should().Be(SalesError.SessionAlreadyClosed);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("this-terminal-id-is-way-too-long-for-the-fifty-character-limit-xxxxxxxx")]
    public void TerminalId_normalize_rejects_invalid_lengths(string terminal)
    {
        var act = () => SalesSession.NormalizeTerminal(terminal);
        act.Should().Throw<SalesDomainException>().Which.Code.Should().Be(SalesError.InvalidSession);
    }

    [Fact]
    public void TerminalId_normalize_trims()
    {
        SalesSession.NormalizeTerminal("  POS-1  ").Should().Be("POS-1");
    }
}
