using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Reliability.CustodyException;

internal static class CustodyExceptionApprovalGuard
{
    public static async Task EnsureNotPendingAsync(
        IParcelCustodyExceptionRequestRepository requests,
        Guid incidentId,
        CancellationToken cancellationToken)
    {
        var request = await requests.GetByIncidentAsync(incidentId, cancellationToken);
        if (request?.Status == ParcelCustodyExceptionRequestStatus.PENDING_APPROVAL)
            throw new CodedConflictException(
                "PARCEL_CUSTODY_EXCEPTION_APPROVAL_REQUIRED",
                "The custody exception report must be approved before search or recovery actions can continue.");
    }
}
