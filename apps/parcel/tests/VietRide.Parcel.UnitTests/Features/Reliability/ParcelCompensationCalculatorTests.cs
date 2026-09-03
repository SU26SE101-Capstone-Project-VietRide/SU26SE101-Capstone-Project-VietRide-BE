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
            noProofFallbackMultiplier: 2);

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
            noProofFallbackMultiplier: 2);

        award.AssessedLossVnd.Should().Be(4_000_000);
        award.CargoAwardVnd.Should().Be(2_000_000);
        award.FreightRefundVnd.Should().Be(75_000);
    }

    [Fact]
    public void Calculate_WithoutProof_DeclaredLiabilityIsNotAnAward()
    {
        var award = ParcelCompensationCalculator.Calculate(
            ParcelClaimProofStatus.NO_PROOF,
            provenDirectLossVnd: null,
            declaredValueVnd: 50_000_000,
            freightCollectedVnd: 20_000_000,
            alreadyRefundedVnd: 0,
            compensationRatePercent: 50,
            policyCapVnd: 30_000_000,
            noProofFallbackMultiplier: 2);

        award.AssessedLossVnd.Should().BeNull();
        award.DeclaredLiabilityVnd.Should().Be(25_000_000);
        award.FallbackAmountVnd.Should().BeNull();
        award.CargoAwardVnd.Should().Be(0);
        award.TotalAwardVnd.Should().Be(20_000_000);
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
            noProofFallbackMultiplier: 2);

        award.CalculationBasis.Should().Be("NO_VERIFIED_PROOF_FREIGHT_ONLY");
        award.CargoAwardVnd.Should().Be(0);
        award.FreightRefundVnd.Should().Be(150_000);
        award.TotalAwardVnd.Should().Be(150_000);
    }

    [Fact]
    public void Calculate_WithoutDeclaredValue_RefundsFreightOnly()
    {
        var award = ParcelCompensationCalculator.Calculate(
            ParcelClaimProofStatus.NO_PROOF,
            provenDirectLossVnd: null,
            declaredValueVnd: null,
            freightCollectedVnd: 10_000_000,
            alreadyRefundedVnd: 0,
            compensationRatePercent: 50,
            policyCapVnd: 30_000_000,
            noProofFallbackMultiplier: 2);

        award.DeclaredLiabilityVnd.Should().BeNull();
        award.FallbackAmountVnd.Should().BeNull();
        award.CalculationBasis.Should().Be("NO_VERIFIED_PROOF_FREIGHT_ONLY");
        award.CargoAwardVnd.Should().Be(0);
        award.FreightRefundVnd.Should().Be(10_000_000);
        award.TotalAwardVnd.Should().Be(10_000_000);
    }

    [Fact]
    public void Calculate_RefundedFreightAndRounding_UseFinancialRules()
    {
        var award = ParcelCompensationCalculator.Calculate(
            ParcelClaimProofStatus.VERIFIED,
            provenDirectLossVnd: 3,
            declaredValueVnd: 3,
            freightCollectedVnd: 100,
            alreadyRefundedVnd: 100,
            compensationRatePercent: 50,
            policyCapVnd: 30_000_000,
            noProofFallbackMultiplier: 2);

        award.DeclaredLiabilityVnd.Should().Be(2);
        award.CargoAwardVnd.Should().Be(2);
        award.FreightRefundVnd.Should().Be(0);
    }

    [Fact]
    public void Calculate_UnusedLegacyMultiplier_CannotOverflowFreightOnlyAward()
    {
        var award = ParcelCompensationCalculator.Calculate(
            ParcelClaimProofStatus.NO_PROOF,
            provenDirectLossVnd: null,
            declaredValueVnd: null,
            freightCollectedVnd: long.MaxValue,
            alreadyRefundedVnd: 0,
            compensationRatePercent: 50,
            policyCapVnd: long.MaxValue,
            noProofFallbackMultiplier: int.MaxValue);

        award.FallbackAmountVnd.Should().BeNull();
        award.CargoAwardVnd.Should().Be(0);
        award.TotalAwardVnd.Should().Be(long.MaxValue);
    }

    public static IEnumerable<object?[]> UnverifiedAwardCases()
    {
        foreach (var proof in new[] { ParcelClaimProofStatus.NO_PROOF, ParcelClaimProofStatus.UNVERIFIED })
            foreach (var declaration in new long?[] { null, 0, 200_000, 10_000_000, long.MaxValue })
                foreach (var multiplier in new[] { 1, 2, 3, 4, int.MaxValue })
                    foreach (var refunded in new long[] { 0, 50_000, 150_000, 200_000 })
                        yield return [proof, declaration, multiplier, refunded];
    }

    [Theory]
    [MemberData(nameof(UnverifiedAwardCases))]
    public void Calculate_Unverified_DeclarationAndLegacyMultiplierNeverCreateCargoAward(
        ParcelClaimProofStatus proof,
        long? declaredValue,
        int multiplier,
        long refunded)
    {
        var award = ParcelCompensationCalculator.Calculate(
            proof, null, declaredValue, 150_000, refunded, 50, 30_000_000, multiplier);

        award.CalculationBasis.Should().Be("NO_VERIFIED_PROOF_FREIGHT_ONLY");
        award.AssessedLossVnd.Should().BeNull();
        award.FallbackAmountVnd.Should().BeNull();
        award.CargoAwardVnd.Should().Be(0);
        award.FreightRefundVnd.Should().Be(Math.Max(150_000 - refunded, 0));
        award.TotalAwardVnd.Should().Be(award.FreightRefundVnd);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(200_000L)]
    [InlineData(10_000_000L)]
    [InlineData(long.MaxValue)]
    public void Calculate_Verified_InflatingDeclarationBeyondProvenLossDoesNotIncreaseAward(long? declaration)
    {
        var award = ParcelCompensationCalculator.Calculate(
            ParcelClaimProofStatus.VERIFIED, 200_000, declaration, 150_000, 0, 50, 30_000_000, 4);

        award.AssessedLossVnd.Should().Be(200_000);
        award.CargoAwardVnd.Should().Be(100_000);
        award.TotalAwardVnd.Should().Be(250_000);
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
