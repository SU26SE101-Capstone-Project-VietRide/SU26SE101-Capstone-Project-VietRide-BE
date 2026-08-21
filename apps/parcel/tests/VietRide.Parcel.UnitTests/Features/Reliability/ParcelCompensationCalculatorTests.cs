using FluentAssertions;
using VietRide.Parcel.Application.Features.Reliability.Claims;

namespace VietRide.Parcel.UnitTests.Features.Reliability;

public sealed class ParcelCompensationCalculatorTests
{
    [Theory]
    [InlineData(12_000_000, 50, 30_000_000, 6_000_000)]
    [InlineData(80_000_000, 50, 30_000_000, 30_000_000)]
    [InlineData(12_000_000, 70, 50_000_000, 8_400_000)]
    public void Calculate_WithProof_AppliesRateThenCap(
        long provenLoss,
        int rate,
        long cap,
        long expectedCargoAward)
    {
        var award = ParcelCompensationCalculator.Calculate(
            provenLoss,
            declaredValueVnd: 100_000_000,
            freightCollectedVnd: 120_000,
            alreadyRefundedVnd: 0,
            rate,
            cap,
            noProofFallbackMultiplier: 4);

        award.CargoAwardVnd.Should().Be(expectedCargoAward);
        award.FreightRefundVnd.Should().Be(120_000);
        award.TotalAwardVnd.Should().Be(expectedCargoAward + 120_000);
    }

    [Fact]
    public void Calculate_WithDeclaredValue_AssessesNoMoreThanDeclaredValue()
    {
        var award = ParcelCompensationCalculator.Calculate(
            provenDirectLossVnd: 12_000_000,
            declaredValueVnd: 4_000_000,
            freightCollectedVnd: 100_000,
            alreadyRefundedVnd: 25_000,
            compensationRatePercent: 50,
            policyCapVnd: 30_000_000,
            noProofFallbackMultiplier: 4);

        award.AssessedLossVnd.Should().Be(4_000_000);
        award.CargoAwardVnd.Should().Be(2_000_000);
        award.FreightRefundVnd.Should().Be(75_000);
    }

    [Fact]
    public void Calculate_WithoutProof_UsesFreightMultiplierAndCap()
    {
        var award = ParcelCompensationCalculator.Calculate(
            provenDirectLossVnd: null,
            declaredValueVnd: 50_000_000,
            freightCollectedVnd: 10_000_000,
            alreadyRefundedVnd: 0,
            compensationRatePercent: 50,
            policyCapVnd: 30_000_000,
            noProofFallbackMultiplier: 4);

        award.AssessedLossVnd.Should().BeNull();
        award.CargoAwardVnd.Should().Be(30_000_000);
    }
}
