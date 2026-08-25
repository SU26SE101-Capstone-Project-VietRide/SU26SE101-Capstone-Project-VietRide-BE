using FluentAssertions;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;

namespace VietRide.Payment.UnitTests.Domain;

public sealed class OperatorTripSettlementTests
{
    [Fact]
    public void CreatePending_GeneratesSettlementCodeAndSnapshotsTripCode()
    {
        var terminalAt = new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero);

        var settlement = OperatorTripSettlement.CreatePending(
            Guid.NewGuid(),
            Guid.NewGuid(),
            terminalAt,
            "TRIP-20260823-7K3M2QPX");

        settlement.SettlementCode.Should().MatchRegex("^STL-20260823-[0-9ABCDEFGHJKMNPQRSTVWXYZ]{8}$");
        settlement.TripCode.Should().Be("TRIP-20260823-7K3M2QPX");
    }

    [Fact]
    public void TripCodeSnapshot_CannotBeChangedToAnotherCode()
    {
        var settlement = OperatorTripSettlement.CreatePending(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero),
            "TRIP-20260823-7K3M2QPX");

        var action = () => settlement.SetTripCode("TRIP-20260823-4F8N2KQJ");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*immutable*");
        settlement.TripCode.Should().Be("TRIP-20260823-7K3M2QPX");
    }

    [Fact]
    public void BackfillBusinessCodes_IsIdempotentForExistingValues()
    {
        var settlement = OperatorTripSettlement.CreatePending(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero),
            "TRIP-20260823-7K3M2QPX");
        var originalSettlementCode = settlement.SettlementCode;

        settlement.BackfillBusinessCodes("TRIP-20260823-4F8N2KQJ");
        settlement.BackfillBusinessCodes("TRIP-20260823-4F8N2KQJ");

        settlement.SettlementCode.Should().Be(originalSettlementCode);
        settlement.TripCode.Should().Be("TRIP-20260823-7K3M2QPX");
    }

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
        settlement.CancelReason.Should().Be("NON_POSITIVE_NET_ENTITLEMENT");
    }

    [Fact]
    public void RefreshEligibility_SubstitutionMarker_PreservesExplicitCancelReason()
    {
        var terminalAt = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var settlement = OperatorTripSettlement.CreatePending(
            Guid.NewGuid(),
            Guid.NewGuid(),
            terminalAt);

        settlement.RefreshEligibility(
            0,
            terminalAt,
            "VEHICLE_SUBSTITUTION_REVENUE_RETAINED_ON_ORIGINAL_TRIP");

        settlement.Status.Should().Be(OperatorTripSettlementStatus.CANCELLED);
        settlement.CancelReason.Should()
            .Be("VEHICLE_SUBSTITUTION_REVENUE_RETAINED_ON_ORIGINAL_TRIP");
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
