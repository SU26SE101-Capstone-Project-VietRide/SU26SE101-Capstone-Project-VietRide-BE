using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Reliability.Reconciliation;

public sealed class GetParcelStopDepartureClearanceQueryHandler
    : IRequestHandler<GetParcelStopDepartureClearanceQuery, ParcelStopDepartureClearanceResponse>
{
    private readonly IParcelRepository _parcels;
    private readonly IParcelReliabilityRepository _reliability;
    private readonly IParcelStopDepartureApprovalRepository _requests;

    public GetParcelStopDepartureClearanceQueryHandler(
        IParcelRepository parcels,
        IParcelReliabilityRepository reliability,
        IParcelStopDepartureApprovalRepository requests)
    {
        _parcels = parcels;
        _reliability = reliability;
        _requests = requests;
    }

    public async Task<ParcelStopDepartureClearanceResponse> Handle(
        GetParcelStopDepartureClearanceQuery query,
        CancellationToken cancellationToken)
    {
        var manifest = await _parcels.ListDropoffManifestByTripAndStopAsync(
            query.TripId,
            query.StopId,
            cancellationToken);
        if (manifest.Any(parcel => parcel.OperatorId != query.OperatorId))
            throw new ForbiddenException("FORBIDDEN", "Parcel manifest does not belong to this operator.");

        var expectedIds = manifest.Select(parcel => parcel.Id).ToHashSet();
        var allEvents = await _reliability.ListCustodyEventsByParcelsAsync(expectedIds, cancellationToken);
        var resolvedIds = allEvents
            .Where(item => item.TripId == query.TripId
                && item.ActualLocationType == ParcelCustodyLocationType.ROUTE_STOP
                && item.ActualLocationId == query.StopId
                && item.EventType is ParcelCustodyEventType.UNLOADED
                    or ParcelCustodyEventType.MANUAL_CUSTODY_EXCEPTION)
            .Select(item => item.ParcelId)
            .ToHashSet();
        var unresolved = expectedIds.Except(resolvedIds).OrderBy(id => id).ToArray();
        if (unresolved.Length == 0)
            return new(query.TripId, query.StopId, query.OperatorId, "CLEAR", [], null, null, null);

        var request = await _requests.GetLatestByTripStopAsync(
            query.TripId,
            query.StopId,
            cancellationToken);
        var approved = request is not null
            && request.OperatorId == query.OperatorId
            && request.Status == ParcelStopDepartureApprovalStatus.APPROVED
            && ParcelStopDepartureApprovalMapper.Matches(request, unresolved);
        var matchingPending = request is not null
            && request.OperatorId == query.OperatorId
            && request.Status == ParcelStopDepartureApprovalStatus.PENDING_APPROVAL
            && ParcelStopDepartureApprovalMapper.Matches(request, unresolved);
        return new(
            query.TripId,
            query.StopId,
            query.OperatorId,
            approved ? "APPROVED_OVERRIDE" : "BLOCKED_PENDING_APPROVAL",
            unresolved,
            approved || matchingPending ? request!.Id : null,
            approved ? request!.ReviewedByUserId : null,
            approved ? request!.ReviewedAt : null);
    }
}
