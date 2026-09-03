using FluentAssertions;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.UnitTests.Domain;

public sealed class ParcelClaimAppealTests
{
    [Theory]
    [InlineData(ParcelClaimProofStatus.NO_PROOF)]
    [InlineData(ParcelClaimProofStatus.UNVERIFIED)]
    [InlineData((ParcelClaimProofStatus)999)]
    public void ClaimApproval_WithoutVerifiedProof_CannotBypassCalculator(ParcelClaimProofStatus proof)
    {
        var claim = ParcelClaim.Submit(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            50_000_000, 1, 50, 30_000_000, 4);
        claim.BeginReview();

        var action = () => claim.Approve(proof, null, 50, 30_000_000, 100_000, 150_000,
            "Attempted unverified cargo award.", Guid.NewGuid(), DateTimeOffset.UtcNow);

        action.Should().Throw<ArgumentException>();
        claim.Status.Should().Be(ParcelClaimStatus.UNDER_REVIEW);
        claim.ProofStatus.Should().BeNull();
        claim.TotalAwardVnd.Should().Be(0);
    }

    [Theory]
    [InlineData(ParcelClaimProofStatus.NO_PROOF)]
    [InlineData(ParcelClaimProofStatus.UNVERIFIED)]
    [InlineData((ParcelClaimProofStatus)999)]
    public void AppealApproval_WithoutVerifiedProof_CannotBypassCalculator(ParcelClaimProofStatus proof)
    {
        var claim = CreateRejectedClaim();
        var appeal = ParcelClaimAppeal.Submit(claim, "Please reconsider.", claim.BeneficiaryUserId,
            DateTimeOffset.UtcNow, Guid.NewGuid());
        appeal.BeginReview();

        var action = () => appeal.ApproveAdjustment(proof, null, 100_000, 150_000,
            "Attempted unverified cargo award.", Guid.NewGuid(), DateTimeOffset.UtcNow);

        action.Should().Throw<ArgumentException>();
        appeal.Status.Should().Be(ParcelClaimAppealStatus.UNDER_REVIEW);
        appeal.ProofStatus.Should().BeNull();
        appeal.SupplementaryAwardVnd.Should().Be(0);
    }

    [Fact]
    public void Submit_FromRejectedClaim_PreservesOriginalDecisionAndCreatesSeparateCase()
    {
        var claim = CreateRejectedClaim();
        var originalReason = claim.DecisionReason;

        var appeal = ParcelClaimAppeal.Submit(
            claim,
            "The sender supplied a corrected invoice.",
            claim.BeneficiaryUserId,
            DateTimeOffset.UtcNow,
            Guid.NewGuid());

        claim.Status.Should().Be(ParcelClaimStatus.REJECTED);
        claim.DecisionReason.Should().Be(originalReason);
        appeal.Status.Should().Be(ParcelClaimAppealStatus.SUBMITTED);
        appeal.OriginalClaimStatus.Should().Be(ParcelClaimStatus.REJECTED);
        appeal.OriginalTotalAwardVnd.Should().Be(0);
    }

    [Fact]
    public void ApproveAdjustment_FromPaidClaim_PaysOnlyPositiveDifference()
    {
        var claim = CreatePaidClaim(6_000_000);
        var appeal = ParcelClaimAppeal.Submit(
            claim,
            "New evidence changes the assessed direct loss.",
            claim.BeneficiaryUserId,
            DateTimeOffset.UtcNow,
            Guid.NewGuid());
        appeal.BeginReview();

        appeal.ApproveAdjustment(
            ParcelClaimProofStatus.VERIFIED,
            20_000_000,
            10_000_000,
            100_000,
            "Corrected evidence accepted.",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);

        appeal.Status.Should().Be(ParcelClaimAppealStatus.ADJUSTMENT_APPROVED);
        appeal.OriginalTotalAwardVnd.Should().Be(6_000_000);
        appeal.RevisedTotalAwardVnd.Should().Be(10_100_000);
        appeal.SupplementaryAwardVnd.Should().Be(4_100_000);
        claim.Status.Should().Be(ParcelClaimStatus.PAID);
        claim.TotalAwardVnd.Should().Be(6_000_000);
    }

    [Fact]
    public void ApproveAdjustment_WithoutHigherAward_IsRejected()
    {
        var claim = CreatePaidClaim(6_000_000);
        var appeal = ParcelClaimAppeal.Submit(
            claim,
            "Please review.",
            claim.BeneficiaryUserId,
            DateTimeOffset.UtcNow,
            Guid.NewGuid());
        appeal.BeginReview();

        var action = () => appeal.ApproveAdjustment(
            ParcelClaimProofStatus.VERIFIED,
            10_000_000,
            5_900_000,
            100_000,
            "No positive difference.",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);

        action.Should().Throw<ArgumentException>();
    }

    private static ParcelClaim CreateRejectedClaim()
    {
        var claim = ParcelClaim.Submit(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            20_000_000,
            1,
            50,
            30_000_000,
            4);
        claim.BeginReview();
        claim.Reject(
            ParcelClaimProofStatus.NO_PROOF,
            null,
            "Evidence did not match.",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);
        return claim;
    }

    private static ParcelClaim CreatePaidClaim(long totalAwardVnd)
    {
        var claim = ParcelClaim.Submit(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            20_000_000,
            1,
            50,
            30_000_000,
            4);
        claim.BeginReview();
        claim.Approve(
            ParcelClaimProofStatus.VERIFIED,
            12_000_000,
            50,
            30_000_000,
            totalAwardVnd,
            0,
            "Approved.",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);
        claim.MarkPaid(Guid.NewGuid(), DateTimeOffset.UtcNow);
        return claim;
    }
}
