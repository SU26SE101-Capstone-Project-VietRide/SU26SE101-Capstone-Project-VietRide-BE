using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Reliability.Claims;
using VietRide.Parcel.Domain.Entities;

namespace VietRide.Parcel.UnitTests.Features.Reliability;

public sealed class HandleParcelCompensationStatusTests
{
    [Fact]
    public async Task FundingPendingAfterPaidAppeal_IsIgnoredAsAStaleEvent()
    {
        var claim = CreatePaidClaim();
        var appeal = ParcelClaimAppeal.Submit(
            claim,
            "New evidence.",
            claim.BeneficiaryUserId,
            DateTimeOffset.UtcNow,
            Guid.NewGuid());
        appeal.BeginReview();
        appeal.ApproveAdjustment(
            20_000_000,
            10_000_000,
            0,
            "Adjustment approved.",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);
        appeal.MarkPaid(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var reliability = Substitute.For<IParcelReliabilityRepository>();
        reliability.GetClaimByIdAsync(appeal.Id, Arg.Any<CancellationToken>())
            .Returns((ParcelClaim?)null);
        reliability.GetClaimAppealByIdForUpdateAsync(appeal.Id, Arg.Any<CancellationToken>())
            .Returns(appeal);
        var handler = new HandleParcelCompensationStatusCommandHandler(reliability);

        var handled = await handler.Handle(
            new HandleParcelCompensationStatusCommand(
                appeal.Id,
                Guid.NewGuid(),
                "FUNDING_PENDING",
                DateTimeOffset.UtcNow.AddMinutes(1)),
            CancellationToken.None);

        handled.Should().BeTrue();
        await reliability.DidNotReceive().UpdateClaimAppealAsync(
            Arg.Any<ParcelClaimAppeal>(),
            Arg.Any<CancellationToken>());
    }

    private static ParcelClaim CreatePaidClaim()
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
            12_000_000,
            50,
            30_000_000,
            6_000_000,
            0,
            "Approved.",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);
        claim.MarkPaid(Guid.NewGuid(), DateTimeOffset.UtcNow);
        return claim;
    }
}
