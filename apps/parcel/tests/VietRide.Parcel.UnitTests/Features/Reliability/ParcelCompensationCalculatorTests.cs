using FluentAssertions;
using VietRide.Parcel.Application.Features.Reliability.Claims;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

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
            ParcelClaimProofStatus.VERIFIED,
            provenDirectLossVnd: provenLoss,
            declaredValueVnd: 100_000_000,
            freightCollectedVnd: 120_000,
            alreadyRefundedVnd: 0,
            compensationRatePercent: rate,
            policyCapVnd: cap,
            noProofFallbackMultiplier: 4);

        award.CargoAwardVnd.Should().Be(expectedCargoAward);
        award.FreightRefundVnd.Should().Be(120_000);
        award.TotalAwardVnd.Should().Be(expectedCargoAward + 120_000);
    }

    [Fact]
    public void Calculate_WithDeclaredValue_AssessesNoMoreThanDeclaredValue()
    {
        var award = ParcelCompensationCalculator.Calculate(
            ParcelClaimProofStatus.VERIFIED,
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
    public void Calculate_WithoutProof_IsGuardedByDeclaredLiability()
    {
        var award = ParcelCompensationCalculator.Calculate(
            ParcelClaimProofStatus.NO_PROOF,
            provenDirectLossVnd: null,
            declaredValueVnd: 50_000_000,
            freightCollectedVnd: 10_000_000,
            alreadyRefundedVnd: 0,
            compensationRatePercent: 50,
            policyCapVnd: 30_000_000,
            noProofFallbackMultiplier: 4);

        award.AssessedLossVnd.Should().BeNull();
        award.DeclaredLiabilityVnd.Should().Be(25_000_000);
        award.FallbackAmountVnd.Should().Be(40_000_000);
        award.CargoAwardVnd.Should().Be(25_000_000);
    }

    [Theory]
    [InlineData(ParcelClaimProofStatus.NO_PROOF)]
    [InlineData(ParcelClaimProofStatus.UNVERIFIED)]
    public void Calculate_ThreeHundredThousandCase_DoesNotRewardMissingProof(
        ParcelClaimProofStatus proofStatus)
    {
        var award = ParcelCompensationCalculator.Calculate(
            proofStatus,
            provenDirectLossVnd: null,
            declaredValueVnd: 300_000,
            freightCollectedVnd: 150_000,
            alreadyRefundedVnd: 0,
            compensationRatePercent: 50,
            policyCapVnd: 30_000_000,
            noProofFallbackMultiplier: 4);

        award.CalculationBasis.Should().Be("NO_PROOF_FALLBACK");
        award.CargoAwardVnd.Should().Be(150_000);
        award.FreightRefundVnd.Should().Be(150_000);
        award.TotalAwardVnd.Should().Be(300_000);
    }

    [Fact]
    public void Calculate_WithoutDeclaredValue_PreservesFallbackPolicy()
    {
        var award = ParcelCompensationCalculator.Calculate(
            ParcelClaimProofStatus.NO_PROOF,
            provenDirectLossVnd: null,
            declaredValueVnd: null,
            freightCollectedVnd: 10_000_000,
            alreadyRefundedVnd: 0,
            compensationRatePercent: 50,
            policyCapVnd: 30_000_000,
            noProofFallbackMultiplier: 4);

        award.DeclaredLiabilityVnd.Should().BeNull();
        award.CargoAwardVnd.Should().Be(30_000_000);
    }

    [Fact]
    public void Calculate_RefundedFreightAndRounding_UseFinancialRules()
    {
        var award = ParcelCompensationCalculator.Calculate(
            ParcelClaimProofStatus.NO_PROOF,
            provenDirectLossVnd: null,
            declaredValueVnd: 3,
            freightCollectedVnd: 100,
            alreadyRefundedVnd: 100,
            compensationRatePercent: 50,
            policyCapVnd: 30_000_000,
            noProofFallbackMultiplier: 4);

        award.DeclaredLiabilityVnd.Should().Be(2);
        award.CargoAwardVnd.Should().Be(2);
        award.FreightRefundVnd.Should().Be(0);
    }

    [Fact]
    public void Calculate_FallbackOverflow_IsRejected()
    {
        var action = () => ParcelCompensationCalculator.Calculate(
            ParcelClaimProofStatus.NO_PROOF,
            provenDirectLossVnd: null,
            declaredValueVnd: null,
            freightCollectedVnd: long.MaxValue,
            alreadyRefundedVnd: 0,
            compensationRatePercent: 50,
            policyCapVnd: long.MaxValue,
            noProofFallbackMultiplier: 2);

        action.Should().Throw<CodedValidationException>();
    }

    [Fact]
    public void Calculate_TotalAwardOverflow_IsRejectedAsValidationError()
    {
        var action = () => ParcelCompensationCalculator.Calculate(
            ParcelClaimProofStatus.VERIFIED,
            provenDirectLossVnd: long.MaxValue,
            declaredValueVnd: null,
            freightCollectedVnd: long.MaxValue,
            alreadyRefundedVnd: 0,
            compensationRatePercent: 100,
            policyCapVnd: long.MaxValue,
            noProofFallbackMultiplier: 1);

        action.Should().Throw<CodedValidationException>();
    }
}
