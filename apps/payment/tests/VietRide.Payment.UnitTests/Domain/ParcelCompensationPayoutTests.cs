using FluentAssertions;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;

namespace VietRide.Payment.UnitTests.Domain;

public sealed class ParcelCompensationPayoutTests
{
    [Fact]
    public void Create_RequiresPositiveAwardAndFreezesTenantSnapshot()
    {
        var claimId = Guid.NewGuid();
        var parcelId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var beneficiaryId = Guid.NewGuid();

        var payout = ParcelCompensationPayout.Create(
            claimId,
            parcelId,
            tripId,
            operatorId,
            beneficiaryId,
            6_000_000);

        payout.ClaimId.Should().Be(claimId);
        payout.ParcelId.Should().Be(parcelId);
        payout.TripId.Should().Be(tripId);
        payout.OperatorId.Should().Be(operatorId);
        payout.BeneficiaryUserId.Should().Be(beneficiaryId);
        payout.AmountVnd.Should().Be(6_000_000);
        payout.Status.Should().Be(ParcelCompensationPayoutStatus.PENDING);
    }

    [Fact]
    public void PaidPayout_CannotReturnToFundingPending()
    {
        var payout = ParcelCompensationPayout.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            6_000_000);
        payout.MarkPaid(
            ParcelCompensationFundingSource.OPERATOR_WALLET,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);

        var action = payout.MarkFundingPending;

        action.Should().Throw<InvalidOperationException>();
    }
}
