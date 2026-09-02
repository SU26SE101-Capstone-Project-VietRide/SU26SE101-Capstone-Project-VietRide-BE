using FluentAssertions;
using VietRide.Payment.Application.Features.Management;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Application.Services;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;

namespace VietRide.Payment.UnitTests.Features.Management;

public sealed class WalletReconciliationTests
{
    [Fact]
    public void Calculate_ClampsEachTripAndExcludesTerminalSettlements()
    {
        var operatorId = Guid.NewGuid();
        var awaitingTripId = Guid.NewGuid();
        var negativeTripId = Guid.NewGuid();
        var pendingTripId = Guid.NewGuid();
        var eligibleTripId = Guid.NewGuid();
        var settledTripId = Guid.NewGuid();
        var cancelledTripId = Guid.NewGuid();
        var projections = new[]
        {
            Projection(operatorId, awaitingTripId, 100),
            Projection(operatorId, negativeTripId, -30),
            Projection(operatorId, pendingTripId, 200),
            Projection(operatorId, eligibleTripId, 300),
            Projection(operatorId, settledTripId, 400),
            Projection(operatorId, cancelledTripId, 500),
        };
        var markers = new[]
        {
            new WalletReconciliationSettlementMarker(operatorId, pendingTripId, OperatorTripSettlementStatus.PENDING_HOLD),
            new WalletReconciliationSettlementMarker(operatorId, eligibleTripId, OperatorTripSettlementStatus.ELIGIBLE),
            new WalletReconciliationSettlementMarker(operatorId, settledTripId, OperatorTripSettlementStatus.SETTLED),
            new WalletReconciliationSettlementMarker(operatorId, cancelledTripId, OperatorTripSettlementStatus.CANCELLED),
        };

        var result = WalletReconciliationCalculator.Calculate(projections, markers)
            .Should().ContainSingle().Which;

        result.OperatorId.Should().Be(operatorId);
        result.AwaitingTripCompletionPayableVnd.Should().Be(100);
        result.PendingHoldPayableVnd.Should().Be(200);
        result.EligibleForSettlementVnd.Should().Be(300);
        result.OutstandingPayableVnd.Should().Be(600);
    }

    [Fact]
    public void Aggregate_EqualsSumOfOperatorReconciliationsOnSameFixture()
    {
        var firstOperatorId = Guid.NewGuid();
        var secondOperatorId = Guid.NewGuid();
        var firstEligibleTripId = Guid.NewGuid();
        var secondPendingTripId = Guid.NewGuid();
        var projections = new[]
        {
            Projection(firstOperatorId, Guid.NewGuid(), 100),
            Projection(firstOperatorId, firstEligibleTripId, 250),
            Projection(secondOperatorId, secondPendingTripId, 400),
            Projection(secondOperatorId, Guid.NewGuid(), -50),
        };
        var markers = new[]
        {
            new WalletReconciliationSettlementMarker(
                firstOperatorId,
                firstEligibleTripId,
                OperatorTripSettlementStatus.ELIGIBLE),
            new WalletReconciliationSettlementMarker(
                secondOperatorId,
                secondPendingTripId,
                OperatorTripSettlementStatus.PENDING_HOLD),
        };

        var operators = WalletReconciliationCalculator.Calculate(projections, markers);
        var admin = WalletReconciliationCalculator.Aggregate(operators);

        admin.OutstandingOperatorPayableVnd.Should().Be(
            operators.Sum(item => item.OutstandingPayableVnd));
        admin.EligibleForSettlementVnd.Should().Be(
            operators.Sum(item => item.EligibleForSettlementVnd));
        admin.AwaitingTripCompletionVnd.Should().Be(100);
        admin.PendingHoldVnd.Should().Be(400);
        admin.EligibleForSettlementVnd.Should().Be(250);
        admin.EligibleOperatorCount.Should().Be(1);
    }

    [Fact]
    public void PlatformLink_Create_AllowsZeroButRejectsNegativeAllocation()
    {
        var transactionId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var referenceId = Guid.NewGuid();

        var link = PlatformWalletTransactionLink.Create(
            transactionId,
            PlatformWalletTransactionLinkType.BOOKING,
            125_000,
            operatorId,
            tripId,
            referenceId,
            "VR-20260902-ABC123");

        link.PlatformWalletTransactionId.Should().Be(transactionId);
        link.OperatorId.Should().Be(operatorId);
        link.TripId.Should().Be(tripId);
        link.ReferenceId.Should().Be(referenceId);
        link.ReferenceCode.Should().Be("VR-20260902-ABC123");
        link.AllocatedAmount.Should().Be(125_000);

        var zeroAllocation = PlatformWalletTransactionLink.Create(
            transactionId,
            PlatformWalletTransactionLinkType.BOOKING,
            0,
            operatorId,
            tripId,
            referenceId,
            null);
        zeroAllocation.AllocatedAmount.Should().Be(0);

        var invalid = () => PlatformWalletTransactionLink.Create(
            transactionId,
            PlatformWalletTransactionLinkType.BOOKING,
            -1,
            operatorId,
            tripId,
            referenceId,
            null);
        invalid.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void PaymentContextLinks_PreserveBookingGroupAllocationAndTaxonomy()
    {
        var first = new PaymentAllocationV1(
            Guid.NewGuid(), "BOOKING", Guid.NewGuid(), Guid.NewGuid(), 100_000, 10_000, 5_000, "VR-A");
        var second = new PaymentAllocationV1(
            Guid.NewGuid(), "BOOKING", Guid.NewGuid(), Guid.NewGuid(), 200_000, 20_000, 10_000, "VR-B");

        var links = PlatformWalletLinkFactory.FromPaymentContext(
            new PaymentContextV1(1, [first, second]));

        links.Should().HaveCount(2);
        links.Sum(item => item.AllocatedAmount).Should().Be(255_000);
        links.Select(item => item.ReferenceCode).Should().Equal("VR-A", "VR-B");
        FinancialTaxonomy.Platform(PlatformWalletTransactionRef.BOOKING_PAYMENT_HOLD)
            .Should().Be(("TICKET", "CUSTOMER_FUNDS_HELD"));
        FinancialTaxonomy.OperatorWallet(OperatorWalletTransactionRef.SUBSCRIPTION_PAYMENT)
            .Should().Be(("SUBSCRIPTION", "PLATFORM_SERVICE_PAYMENT"));
    }

    private static TripFinancialProjection Projection(Guid operatorId, Guid tripId, long netAmount)
        => new(operatorId, tripId, 0, 0, 0, 0, 0, 0, netAmount, true);
}
