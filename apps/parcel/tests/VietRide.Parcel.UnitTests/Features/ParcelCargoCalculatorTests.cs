using FluentAssertions;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Parcel.UnitTests.Features;

public sealed class ParcelCargoCalculatorTests
{
    [Fact]
    public void CalculateTotalPrice_DoesNotCeilKilogramsOrFloorToThousands()
    {
        var result = ParcelCargoCalculator.CalculateTotalPrice(
            3.2m,
            Money.FromRaw(1_000),
            Money.Zero);

        result.Amount.Should().Be(3_200);
    }

    [Theory]
    [InlineData(1.2344, 1_234)]
    [InlineData(1.2345, 1_235)]
    public void CalculateTotalPrice_RoundsFractionalVndAwayFromZero(
        decimal chargeableWeightKg,
        long expected)
    {
        var result = ParcelCargoCalculator.CalculateTotalPrice(
            chargeableWeightKg,
            Money.FromRaw(1_000),
            Money.Zero);

        result.Amount.Should().Be(expected);
    }

    [Fact]
    public void CalculateTotalPrice_AppliesConfiguredMinimum()
    {
        var result = ParcelCargoCalculator.CalculateTotalPrice(
            3.2m,
            Money.FromRaw(1_000),
            Money.FromRaw(5_000));

        result.Amount.Should().Be(5_000);
    }

    [Theory]
    [InlineData(20_001, 4_000)]
    [InlineData(20_003, 4_001)]
    public void CalculatePercent_RoundsToNearestVnd(long amount, long expected)
    {
        ParcelCargoCalculator.CalculatePercent(Money.FromRaw(amount), 20m)
            .Amount.Should().Be(expected);
    }

    [Theory]
    [InlineData(20_000, 50_000, 0)]
    [InlineData(50_000, 20_000, 30_000)]
    public void CalculateDiscountedTotal_ClampsDiscountToGross(
        long gross,
        long discount,
        long expected)
    {
        ParcelCargoCalculator.CalculateDiscountedTotal(
                Money.FromRaw(gross),
                Money.FromRaw(discount))
            .Amount.Should().Be(expected);
    }

    [Theory]
    [InlineData(5, ParcelSizeCategory.SMALL)]
    [InlineData(5.01, ParcelSizeCategory.MEDIUM)]
    [InlineData(15, ParcelSizeCategory.MEDIUM)]
    [InlineData(15.01, ParcelSizeCategory.LARGE)]
    [InlineData(30, ParcelSizeCategory.LARGE)]
    [InlineData(30.01, ParcelSizeCategory.EXTRA_LARGE)]
    public void DeriveSizeCategory_UsesCanonicalChargeableWeightThresholds(
        decimal chargeableWeightKg,
        ParcelSizeCategory expected)
    {
        ParcelCargoCalculator.DeriveSizeCategory(chargeableWeightKg)
            .Should().Be(expected);
    }

    [Fact]
    public void CalculateSettlementDeadlines_PreservesMinimumFinalPaymentWindow()
    {
        var departureAt = new DateTimeOffset(2026, 7, 27, 10, 0, 0, TimeSpan.FromHours(7));

        var result = ParcelCargoCalculator.CalculateSettlementDeadlines(departureAt);

        result.LoadCutoffAt.Should().Be(departureAt.AddMinutes(-10));
        result.LatestCheckInAt.Should().Be(departureAt.AddMinutes(-30));
    }
}
