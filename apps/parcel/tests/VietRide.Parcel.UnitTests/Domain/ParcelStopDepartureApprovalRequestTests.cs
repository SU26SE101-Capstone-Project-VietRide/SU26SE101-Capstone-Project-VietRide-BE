using FluentAssertions;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.UnitTests.Domain;

public sealed class ParcelStopDepartureApprovalRequestTests
{
    [Fact]
    public void Create_DoesNotAcceptAClientSuppliedReviewer()
    {
        var request = ParcelStopDepartureApprovalRequest.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            $"[\"{Guid.NewGuid():D}\"]",
            "Unable to locate the Parcel before departure.",
            Guid.NewGuid(),
            "ASSISTANT",
            DateTimeOffset.UtcNow,
            Guid.NewGuid());

        request.Status.Should().Be(ParcelStopDepartureApprovalStatus.PENDING_APPROVAL);
        request.ReviewedByUserId.Should().BeNull();
        request.ReviewedByRole.Should().BeNull();
    }

    [Fact]
    public void Approve_FreezesReviewerAuditAndPreventsSecondDecision()
    {
        var request = ParcelStopDepartureApprovalRequest.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            $"[\"{Guid.NewGuid():D}\"]",
            "Unable to locate the Parcel before departure.",
            Guid.NewGuid(),
            "ASSISTANT",
            DateTimeOffset.UtcNow,
            Guid.NewGuid());
        var reviewer = Guid.NewGuid();

        request.Approve(reviewer, "DRIVER", "Vehicle sweep completed.", DateTimeOffset.UtcNow);

        request.Status.Should().Be(ParcelStopDepartureApprovalStatus.APPROVED);
        request.ReviewedByUserId.Should().Be(reviewer);
        var action = () => request.Reject(Guid.NewGuid(), "OPERATOR_ADMIN", null, DateTimeOffset.UtcNow);
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CancelAsSuperseded_RecordsSystemAuditWithoutImpersonatingRequester()
    {
        var request = ParcelStopDepartureApprovalRequest.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            $"[\"{Guid.NewGuid():D}\"]",
            "Unable to locate the Parcel before departure.",
            Guid.NewGuid(),
            "ASSISTANT",
            DateTimeOffset.UtcNow,
            Guid.NewGuid());

        request.CancelAsSuperseded(DateTimeOffset.UtcNow);

        request.Status.Should().Be(ParcelStopDepartureApprovalStatus.CANCELLED);
        request.ReviewedByUserId.Should().BeNull();
        request.ReviewedByRole.Should().Be("SYSTEM");
    }
}
