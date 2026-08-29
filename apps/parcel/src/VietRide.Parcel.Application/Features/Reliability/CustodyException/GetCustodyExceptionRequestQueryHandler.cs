using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Reliability.CustodyException;

public sealed class GetCustodyExceptionRequestQueryHandler
    : IRequestHandler<GetCustodyExceptionRequestQuery, ReportCustodyExceptionResponse>
{
    private readonly IParcelCustodyExceptionRequestRepository _requests;
    private readonly IParcelReliabilityRepository _reliability;
    private readonly ITripServiceClient _trips;

    public GetCustodyExceptionRequestQueryHandler(
        IParcelCustodyExceptionRequestRepository requests,
        IParcelReliabilityRepository reliability,
        ITripServiceClient trips)
    {
        _requests = requests;
        _reliability = reliability;
        _trips = trips;
    }

    public async Task<ReportCustodyExceptionResponse> Handle(
        GetCustodyExceptionRequestQuery query,
        CancellationToken cancellationToken)
    {
        var request = await _requests.GetLatestByParcelAsync(query.ParcelId, cancellationToken);
        if (request is null || request.OperatorId != query.OperatorId)
            throw new CodedNotFoundException(
                "PARCEL_CUSTODY_EXCEPTION_REQUEST_NOT_FOUND",
                "Custody exception approval request was not found.");
        if (!string.Equals(query.ReviewerRole, "DRIVER", StringComparison.Ordinal))
            throw new ForbiddenException("FORBIDDEN", "Only a Driver can review this report.");

        var authorization = await _trips.AuthorizeCrewForTripAsync(
            request.TripId,
            query.ReviewerUserId,
            query.OperatorId,
            "DRIVER",
            cancellationToken);
        if (authorization.Kind == TripCrewAuthorizationOutcomeKind.TransportError)
            throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                authorization.ErrorMessage ?? "Trip service is unavailable.");
        if (authorization.Kind != TripCrewAuthorizationOutcomeKind.Authorized)
            throw new ForbiddenException("FORBIDDEN", "Only the assigned Driver can review this report.");

        var incident = await _reliability.GetIncidentAsync(request.IncidentId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_INCIDENT_NOT_FOUND", "Parcel incident was not found.");
        var actions = request.Status == ParcelCustodyExceptionRequestStatus.PENDING_APPROVAL
            ? new[] { "APPROVE", "REJECT" }
            : Array.Empty<string>();
        return CustodyExceptionResponseMapper.Map(request, incident, actions);
    }
}
