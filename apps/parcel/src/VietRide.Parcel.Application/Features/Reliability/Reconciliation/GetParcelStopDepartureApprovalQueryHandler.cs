using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Reliability.Reconciliation;

public sealed class GetParcelStopDepartureApprovalQueryHandler
    : IRequestHandler<GetParcelStopDepartureApprovalQuery, ParcelStopDepartureApprovalResponse>
{
    private readonly IParcelStopDepartureApprovalRepository _requests;
    private readonly ITripServiceClient _trips;

    public GetParcelStopDepartureApprovalQueryHandler(
        IParcelStopDepartureApprovalRepository requests,
        ITripServiceClient trips)
    {
        _requests = requests;
        _trips = trips;
    }

    public async Task<ParcelStopDepartureApprovalResponse> Handle(
        GetParcelStopDepartureApprovalQuery query,
        CancellationToken cancellationToken)
    {
        var request = await _requests.GetByIdAsync(query.RequestId, cancellationToken);
        if (request is null || request.OperatorId != query.OperatorId)
            throw new CodedNotFoundException(
                "PARCEL_STOP_DEPARTURE_APPROVAL_NOT_FOUND",
                "Stop departure approval request was not found.");

        if (query.Role == "DRIVER")
        {
            var authorization = await _trips.AuthorizeCrewForTripAsync(
                request.TripId,
                query.UserId,
                query.OperatorId,
                "DRIVER",
                cancellationToken);
            if (authorization.Kind != TripCrewAuthorizationOutcomeKind.Authorized)
                throw new ForbiddenException(
                    "FORBIDDEN",
                    "Only the assigned Driver can view this stop departure request.");
        }

        return ParcelStopDepartureApprovalMapper.Map(request);
    }
}
