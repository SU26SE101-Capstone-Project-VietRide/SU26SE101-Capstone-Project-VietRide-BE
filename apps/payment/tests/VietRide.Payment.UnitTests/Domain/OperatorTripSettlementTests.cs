using FluentAssertions;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;

namespace VietRide.Payment.UnitTests.Domain;

public sealed class OperatorTripSettlementTests
{
    [Fact]
    public void RefreshEligibility_NetNonPositive_CancelsWithoutWalletMovement()
    {
        var terminalAt = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var settlement = OperatorTripSettlement.CreatePending(
            Guid.NewGuid(),
            Guid.NewGuid(),
            terminalAt);

        settlement.RefreshEligibility(0, terminalAt.AddDays(7));

        settlement.Status.Should().Be(OperatorTripSettlementStatus.CANCELLED);
        settlement.NetAmount.Should().Be(0);
        settlement.WalletTransactionId.Should().BeNull();
    }

    [Fact]
    public void FailureThenSuccess_PreservesHistoryAndClearsActiveFailure()
    {
        var terminalAt = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var eligibleAt = terminalAt.AddDays(7);
        var settlement = OperatorTripSettlement.CreatePending(
            Guid.NewGuid(),
            Guid.NewGuid(),
            terminalAt);
        settlement.RefreshEligibility(500_000, eligibleAt);
        settlement.RecordFailure("PLATFORM_WALLET_INSUFFICIENT_BALANCE", eligibleAt.AddHours(1));
        settlement.RecordFailure("PLATFORM_WALLET_INSUFFICIENT_BALANCE", eligibleAt.AddHours(2));

        settlement.MarkSettled(
            500_000,
            OperatorTripSettlementMethod.AUTO_WEEKLY,
            eligibleAt.AddDays(7),
            null,
            Guid.NewGuid());

        settlement.Status.Should().Be(OperatorTripSettlementStatus.SETTLED);
        settlement.SettlementFailureCount.Should().Be(2);
        settlement.LastSettlementFailureAt.Should().Be(eligibleAt.AddHours(2));
        settlement.ActiveFailureCode.Should().BeNull();
        settlement.FailureResolvedAt.Should().Be(eligibleAt.AddDays(7));
    }
}
