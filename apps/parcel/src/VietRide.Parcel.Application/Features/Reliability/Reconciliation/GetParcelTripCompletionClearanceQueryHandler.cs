using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Reliability.Reconciliation;

public sealed class GetParcelTripCompletionClearanceQueryHandler
    : IRequestHandler<GetParcelTripCompletionClearanceQuery, ParcelTripCompletionClearanceResponse>
{
    private readonly IParcelRepository _parcels;
    private readonly IParcelReliabilityRepository _reliability;

    public GetParcelTripCompletionClearanceQueryHandler(
        IParcelRepository parcels,
        IParcelReliabilityRepository reliability)
    {
        _parcels = parcels;
        _reliability = reliability;
    }

    public async Task<ParcelTripCompletionClearanceResponse> Handle(
        GetParcelTripCompletionClearanceQuery query,
        CancellationToken cancellationToken)
    {
        var manifest = await _parcels.ListTerminalDropoffManifestByTripAsync(
            query.TripId,
            cancellationToken);
        if (manifest.Any(parcel => parcel.OperatorId != query.OperatorId))
            throw new ForbiddenException("FORBIDDEN", "Trip Parcel manifest does not belong to this operator.");

        var unresolved = manifest.Where(parcel => parcel.Status is ParcelStatus.LOADED
                or ParcelStatus.IN_TRANSIT
                || (parcel.Status == ParcelStatus.PENDING_OPERATOR_ACTION
                    && parcel.PendingActionType == PendingActionType.CUSTODY_EXCEPTION))
            .ToArray();
        if (unresolved.Length == 0)
            return new ParcelTripCompletionClearanceResponse(
                query.TripId,
                query.OperatorId,
                "CLEAR",
                [],
                []);

        var incidents = await _reliability.ListActiveIncidentsByParcelsAsync(
            unresolved.Select(parcel => parcel.Id).ToArray(),
            cancellationToken);
        var acknowledged = incidents
            .Where(incident => incident.Type == ParcelIncidentType.UNSCANNED_HANDOFF
                && incident.Status is ParcelIncidentStatus.SEARCHING
                    or ParcelIncidentStatus.ESCALATED
                    or ParcelIncidentStatus.SEARCH_EXPIRED
                && incident.ExpectedLocation?.StartsWith(
                    "DESTINATION_STATION:",
                    StringComparison.OrdinalIgnoreCase) == true)
            .GroupBy(incident => incident.ParcelId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(incident => incident.CreatedAt).First());
        var allAcknowledged = unresolved.All(parcel => acknowledged.ContainsKey(parcel.Id));

        return new ParcelTripCompletionClearanceResponse(
            query.TripId,
            query.OperatorId,
            allAcknowledged ? "ACKNOWLEDGED_INCIDENTS" : "BLOCKED_RECONCILIATION",
            unresolved.Select(parcel => parcel.Id).ToArray(),
            allAcknowledged
                ? unresolved.Select(parcel => acknowledged[parcel.Id].Id).ToArray()
                : []);
    }
}
