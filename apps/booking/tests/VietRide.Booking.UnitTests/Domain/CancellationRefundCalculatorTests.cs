using FluentAssertions;
using VietRide.Booking.Domain.Services;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.UnitTests.Domain;

public class CancellationRefundCalculatorTests
{
    private static readonly CancellationPolicyTier[] DefaultPolicy =
    [
        new(1, 100),
        new(2, 50),
        new(24, 10)
    ];

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 50_000)]
    [InlineData(24, 90_000)]
    [InlineData(25, 100_000)]
    public void CalculateRefundAmount_AtTierBoundaries_UsesFirstMatchingTier(
        decimal hoursToDeparture,
        long expectedRefundAmount)
    {
        var refundAmount = CancellationRefundCalculator.CalculateRefundAmount(
            Money.FromRaw(100_000),
            hoursToDeparture,
            DefaultPolicy,
            refundOverride: false);

        refundAmount.Amount.Should().Be(expectedRefundAmount);
    }

    [Fact]
    public void CalculateRefundAmount_WhenRefundOverride_ReturnsFullRefund()
    {
        var refundAmount = CancellationRefundCalculator.CalculateRefundAmount(
            Money.FromRaw(100_000),
            hoursToDeparture: 1,
            DefaultPolicy,
            refundOverride: true);

        refundAmount.Amount.Should().Be(100_000);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void CalculateRefundAmount_WhenPolicyIsMissing_ReturnsFullRefund(int? policySize)
    {
        var policy = policySize is null ? null : Array.Empty<CancellationPolicyTier>();

        var refundAmount = CancellationRefundCalculator.CalculateRefundAmount(
            Money.FromRaw(100_000),
            hoursToDeparture: 1,
            policy,
            refundOverride: false);

        refundAmount.Amount.Should().Be(100_000);
    }

    [Fact]
    public void CalculateRefundAmount_RoundsToNearestDongAwayFromZero()
    {
        var policy = new[] { new CancellationPolicyTier(1, 33.333m) };

        var refundAmount = CancellationRefundCalculator.CalculateRefundAmount(
            Money.FromRaw(1),
            hoursToDeparture: 1,
            policy,
            refundOverride: false);

        refundAmount.Amount.Should().Be(1);
    }

    [Fact]
    public void CalculateRefundAmount_WhenPaidAmountIsZero_ReturnsZero()
    {
        var refundAmount = CancellationRefundCalculator.CalculateRefundAmount(
            Money.FromRaw(0),
            hoursToDeparture: 1,
            DefaultPolicy,
            refundOverride: true);

        refundAmount.Amount.Should().Be(0);
    }
}
