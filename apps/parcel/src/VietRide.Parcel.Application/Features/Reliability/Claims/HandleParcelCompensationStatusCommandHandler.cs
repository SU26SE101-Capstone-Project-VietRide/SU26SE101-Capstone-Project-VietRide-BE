using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Features.Reliability.Claims;

public sealed class HandleParcelCompensationStatusCommandHandler
    : IRequestHandler<HandleParcelCompensationStatusCommand, bool>
{
    private readonly IParcelReliabilityRepository _reliability;

    public HandleParcelCompensationStatusCommandHandler(IParcelReliabilityRepository reliability)
    {
        _reliability = reliability;
    }

    public async Task<bool> Handle(
        HandleParcelCompensationStatusCommand command,
        CancellationToken cancellationToken)
    {
        var claim = await _reliability.GetClaimByIdAsync(command.ClaimId, cancellationToken);
        if (claim is null)
            return false;
        if (string.Equals(command.Status, "PAID", StringComparison.OrdinalIgnoreCase))
        {
            if (claim.Status == ParcelClaimStatus.PAID)
                return true;
            claim.MarkPaid(command.PayoutId, command.OccurredAt);
        }
        else if (string.Equals(command.Status, "FUNDING_PENDING", StringComparison.OrdinalIgnoreCase))
        {
            if (claim.Status == ParcelClaimStatus.FUNDING_PENDING)
                return true;
            claim.MarkFundingPending();
        }
        else
        {
            return false;
        }

        await _reliability.UpdateClaimAsync(claim, cancellationToken);
        return true;
    }
}
