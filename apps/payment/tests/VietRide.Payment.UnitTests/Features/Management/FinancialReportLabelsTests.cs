using FluentAssertions;
using VietRide.Payment.Application.Features.Management;
using VietRide.Payment.Application.Features.OperatorReports;
using VietRide.Payment.Domain.Enums;

namespace VietRide.Payment.UnitTests.Features.Management;

public sealed class FinancialReportLabelsTests
{
    [Fact]
    public void EveryLedgerEntryType_HasVietnameseLabel()
        => Enum.GetValues<OperatorLedgerEntryType>()
            .Select(value => PaymentReportLabels.EntryType(value.ToString()))
            .Should().OnlyContain(label => label != PaymentReportLabels.Unknown);

    [Fact]
    public void EveryLedgerReferenceType_HasVietnameseLabel()
        => Enum.GetValues<OperatorLedgerReferenceType>()
            .Select(value => FinancialReportLabels.ReferenceType(value.ToString()))
            .Should().OnlyContain(label => label != FinancialReportLabels.Unknown);

    [Fact]
    public void EveryPlatformReferenceType_HasVietnameseLabel()
        => Enum.GetValues<PlatformWalletTransactionRef>()
            .Select(value => FinancialReportLabels.ReferenceType(value.ToString()))
            .Should().OnlyContain(label => label != FinancialReportLabels.Unknown);

    [Fact]
    public void EveryOperatorWalletReferenceType_HasVietnameseLabel()
        => Enum.GetValues<OperatorWalletTransactionRef>()
            .Select(value => FinancialReportLabels.ReferenceType(value.ToString()))
            .Should().OnlyContain(label => label != FinancialReportLabels.Unknown);

    [Fact]
    public void EverySettlementStatusAndMethod_HasVietnameseLabel()
    {
        Enum.GetValues<OperatorTripSettlementStatus>()
            .Select(value => FinancialReportLabels.SettlementStatus(value.ToString()))
            .Should().OnlyContain(label => label != FinancialReportLabels.Unknown);
        Enum.GetValues<OperatorTripSettlementMethod>()
            .Select(value => FinancialReportLabels.SettlementMethod(value.ToString()))
            .Should().OnlyContain(label => label != FinancialReportLabels.Unknown);
    }

    [Fact]
    public void EveryAdjustmentReason_HasVietnameseDescription()
        => Enum.GetValues<OperatorLedgerAdjustmentReason>()
            .Select(value => PaymentReportLabels.Description("ADJUSTMENT", value.ToString(), null))
            .Should().OnlyContain(label => label != PaymentReportLabels.Unknown);
}
