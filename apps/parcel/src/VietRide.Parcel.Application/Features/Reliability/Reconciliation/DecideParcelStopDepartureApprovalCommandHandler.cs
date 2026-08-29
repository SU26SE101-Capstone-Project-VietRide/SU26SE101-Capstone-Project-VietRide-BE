using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Reliability.Reconciliation;

public sealed class DecideParcelStopDepartureApprovalCommandHandler
    : IRequestHandler<DecideParcelStopDepartureApprovalCommand, ParcelStopDepartureApprovalResponse>
{
    private readonly IParcelStopDepartureApprovalRepository _requests;
    private readonly ITripServiceClient _trips;
    private readonly IClock _clock;

    public DecideParcelStopDepartureApprovalCommandHandler(
        IParcelStopDepartureApprovalRepository requests,
        ITripServiceClient trips,
        IClock clock)
    {
        _requests = requests;
        _trips = trips;
        _clock = clock;
    }

    public async Task<ParcelStopDepartureApprovalResponse> Handle(
        DecideParcelStopDepartureApprovalCommand command,
        CancellationToken cancellationToken)
    {
        var request = await _requests.GetByIdForUpdateAsync(command.RequestId, cancellationToken);
        if (request is null || request.OperatorId != command.OperatorId)
            throw new CodedNotFoundException(
                "PARCEL_STOP_DEPARTURE_APPROVAL_NOT_FOUND",
                "Stop departure approval request was not found.");
        if (request.Status != ParcelStopDepartureApprovalStatus.PENDING_APPROVAL)
            throw new CodedConflictException(
                "PARCEL_STOP_DEPARTURE_ALREADY_DECIDED",
                "Stop departure approval request has already been decided.");

        if (command.ReviewerRole == "DRIVER")
        {
            var authorization = await _trips.AuthorizeCrewForTripAsync(
                request.TripId,
                command.ReviewerUserId,
                command.OperatorId,
                "DRIVER",
                cancellationToken);
            if (authorization.Kind != TripCrewAuthorizationOutcomeKind.Authorized)
                throw new ForbiddenException(
                    "FORBIDDEN",
                    "Only the assigned Driver can review this stop departure request.");
        }

        if (command.Decision == "APPROVE")
            request.Approve(command.ReviewerUserId, command.ReviewerRole, command.Note, _clock.UtcNow);
        else
            request.Reject(command.ReviewerUserId, command.ReviewerRole, command.Note, _clock.UtcNow);

        return ParcelStopDepartureApprovalMapper.Map(request);
    }
}
