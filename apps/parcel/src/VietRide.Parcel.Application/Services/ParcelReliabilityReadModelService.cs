using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Abstractions.Services;
using VietRide.Parcel.Application.Features.Reliability.ReadModels;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.Application.Services;

public sealed class ParcelReliabilityReadModelService : IParcelReliabilityReadModelService
{
    private readonly IParcelReliabilityRepository _reliability;
    private readonly ITripServiceClient _trips;
    private readonly IIdentityServiceClient _identity;

    public ParcelReliabilityReadModelService(
        IParcelReliabilityRepository reliability,
        ITripServiceClient trips,
        IIdentityServiceClient identity)
    {
        _reliability = reliability;
        _trips = trips;
        _identity = identity;
    }

    public async Task<IReadOnlyDictionary<Guid, ParcelScreenReadModel>> BuildAsync(
        IReadOnlyCollection<ParcelEntity> parcels,
        Guid? viewerUserId,
        bool includeClaim,
        CancellationToken cancellationToken = default)
    {
        var items = parcels.GroupBy(parcel => parcel.Id).Select(group => group.First()).ToArray();
        if (items.Length == 0)
            return new Dictionary<Guid, ParcelScreenReadModel>();
        if (items.Length > 100)
            throw new ArgumentOutOfRangeException(nameof(parcels), "At most 100 parcels can be enriched at once.");

        var parcelIds = items.Select(parcel => parcel.Id).ToArray();
        var tripTask = _trips.GetTripSummariesAsync(
            items.SelectMany(parcel => parcel.TransferTargetTripId.HasValue
                    ? new[] { parcel.TripId, parcel.TransferTargetTripId.Value }
                    : new[] { parcel.TripId })
                .Distinct()
                .ToArray(),
            cancellationToken);
        var operatorTask = _identity.GetOperatorsAsync(
            items.Select(parcel => parcel.OperatorId).Distinct().ToArray(),
            cancellationToken);

        // EF Core DbContext is scoped and does not permit concurrent queries. Keep same-DB
        // projection reads sequential while the independent HTTP batches run in parallel.
        var currentByParcel = (await _reliability.ListCurrentCustodiesAsync(parcelIds, cancellationToken))
            .ToDictionary(current => current.ParcelId);
        var incidentByParcel = (await _reliability.ListActiveIncidentsByParcelsAsync(parcelIds, cancellationToken))
            .ToDictionary(incident => incident.ParcelId);
        var claimByParcel = includeClaim
            ? (await _reliability.ListLatestClaimsByParcelsAsync(parcelIds, cancellationToken))
                .ToDictionary(claim => claim.ParcelId)
            : new Dictionary<Guid, ParcelClaim>();
        var tripOutcome = await tripTask;
        var tripsById = tripOutcome.Kind == TripSummaryBatchOutcomeKind.Success
            ? tripOutcome.Summaries.ToDictionary(trip => trip.TripId)
            : new Dictionary<Guid, TripSummarySnapshot>();
        var operatorOutcome = await operatorTask;
        var operatorsById = operatorOutcome.Kind == IdentityOperatorBatchOutcomeKind.Success
            ? operatorOutcome.Operators.ToDictionary(operatorTenant => operatorTenant.OperatorId)
            : new Dictionary<Guid, IdentityOperatorSummary>();

        var result = new Dictionary<Guid, ParcelScreenReadModel>(items.Length);
        foreach (var parcel in items)
        {
            currentByParcel.TryGetValue(parcel.Id, out var current);
            incidentByParcel.TryGetValue(parcel.Id, out var incident);
            claimByParcel.TryGetValue(parcel.Id, out var claim);
            tripsById.TryGetValue(parcel.TripId, out var trip);
            TripSummarySnapshot? forwardingTrip = null;
            if (parcel.TransferTargetTripId.HasValue)
                tripsById.TryGetValue(parcel.TransferTargetTripId.Value, out forwardingTrip);
            operatorsById.TryGetValue(parcel.OperatorId, out var operatorTenant);
            var canSeeClaim = includeClaim
                && (!viewerUserId.HasValue || viewerUserId.Value == parcel.SenderUserId);
            var visibleClaim = canSeeClaim ? claim : null;
            var now = DateTimeOffset.UtcNow;
            var tripResponse = MapTrip(parcel, trip);
            result.Add(parcel.Id, new ParcelScreenReadModel(
                new ReliabilityParcelSummaryResponse(
                    parcel.Id,
                    parcel.ParcelCode,
                    parcel.Status.ToString(),
                    parcel.Description,
                    parcel.PhotoUrl,
                    parcel.Quantity,
                    parcel.DeclaredValueVnd),
                new ReliabilityOperatorResponse(
                    parcel.OperatorId,
                    operatorTenant?.OperatorName,
                    operatorTenant?.LogoUrl,
                    operatorTenant?.ContactPhone),
                tripResponse,
                forwardingTrip is null
                    ? null
                    : MapTripForId(parcel.TransferTargetTripId!.Value, forwardingTrip),
                MapDropoff(parcel, tripResponse),
                new ParcelReliabilitySummaryResponse(
                    MapCustody(current),
                    MapIncident(incident, now),
                    MapClaim(visibleClaim, parcel, now),
                    incident?.SearchDeadline,
                    ParcelReliabilityActionResolver.Passenger(
                        parcel,
                        incident,
                        visibleClaim,
                        viewerUserId == parcel.SenderUserId))));
        }

        return result;
    }

    public static ReliabilityTripResponse MapTrip(ParcelEntity parcel, TripSummarySnapshot? trip)
    {
        if (trip is null)
        {
            var route = parcel.TripSnapshotRouteId.HasValue
                ? new ReliabilityRouteResponse(
                    parcel.TripSnapshotRouteId.Value,
                    parcel.TripSnapshotRouteName ?? "",
                    new ReliabilityLocationResponse(
                        "ORIGIN_STATION",
                        null,
                        parcel.TripSnapshotOriginStationName),
                    new ReliabilityLocationResponse(
                        "DESTINATION_STATION",
                        null,
                        parcel.TripSnapshotDestinationStationName))
                : null;
            var vehicle = parcel.TripSnapshotVehicleId.HasValue
                ? new ReliabilityVehicleResponse(
                    parcel.TripSnapshotVehicleId.Value,
                    parcel.TripSnapshotVehicleLicensePlate ?? "",
                    null)
                : null;
            return new ReliabilityTripResponse(parcel.TripId, null, null, null, route, vehicle, []);
        }

        return MapTrip(trip);
    }

    public static ReliabilityTripResponse MapTrip(TripSummarySnapshot trip)
        => new(
            trip.TripId,
            trip.Status,
            trip.DepartureAt,
            trip.ArrivalEstimate,
            new ReliabilityRouteResponse(
                trip.Route.RouteId,
                trip.Route.Name,
                new ReliabilityLocationResponse(
                    "ORIGIN_STATION",
                    trip.Route.OriginStationId == Guid.Empty ? null : trip.Route.OriginStationId,
                    trip.Route.OriginName),
                new ReliabilityLocationResponse(
                    "DESTINATION_STATION",
                    trip.Route.DestinationStationId == Guid.Empty ? null : trip.Route.DestinationStationId,
                    trip.Route.DestinationName)),
            new ReliabilityVehicleResponse(
                trip.Vehicle.VehicleId,
                trip.Vehicle.LicensePlate,
                trip.Vehicle.Status),
            trip.Stops.Select(stop => new ReliabilityTripStopResponse(
                stop.StopId,
                stop.Name,
                stop.OrderIndex,
                stop.EstimatedArrivalAt,
                stop.Status,
                stop.ActualArrivalAt,
                stop.ActualDepartureAt)).ToArray());

    private static ReliabilityTripResponse MapTripForId(Guid tripId, TripSummarySnapshot trip)
    {
        var mapped = MapTrip(trip);
        return mapped with { TripId = tripId };
    }

    public static ReliabilityLocationResponse MapDropoff(
        ParcelEntity parcel,
        ReliabilityTripResponse trip)
    {
        if (parcel.DropoffStopId.HasValue)
        {
            var stop = trip.Stops.FirstOrDefault(item => item.StopId == parcel.DropoffStopId.Value);
            return new ReliabilityLocationResponse(
                "ROUTE_STOP",
                parcel.DropoffStopId,
                stop?.Name,
                stop?.OrderIndex,
                stop?.EstimatedArrivalAt);
        }

        return trip.Route?.Destination
            ?? new ReliabilityLocationResponse(
                "DESTINATION_STATION",
                null,
                parcel.TripSnapshotDestinationStationName,
                Eta: trip.Eta);
    }

    public static ReliabilityCustodySummaryResponse? MapCustody(ParcelCurrentCustody? current)
        => current is null
            ? null
            : new ReliabilityCustodySummaryResponse(
                current.LastEventType.ToString(),
                new ReliabilityLocationResponse(
                    current.LastLocationType?.ToString(),
                    current.LastLocationId,
                    current.LastLocationSnapshot),
                current.LastConfirmedAt,
                current.CurrentTripId,
                current.CurrentVehicleId,
                current.TrackingConfidence.ToString(),
                current.TrackingConfidence != ParcelCustodyTrackingConfidence.CONFIRMED_SCAN);

    public static ReliabilityIncidentSummaryResponse? MapIncident(
        ParcelIncident? incident,
        DateTimeOffset now)
        => incident is null
            ? null
            : new ReliabilityIncidentSummaryResponse(
                incident.Id,
                incident.Type.ToString(),
                incident.Status.ToString(),
                incident.SearchDeadline,
                IsIncidentClosed(incident.Status) ? null : incident.SearchDeadline,
                IncidentSlaState(incident, now),
                incident.OperatorProcessBreach);

    public static ReliabilityClaimSummaryResponse? MapClaim(
        ParcelClaim? claim,
        ParcelEntity parcel,
        DateTimeOffset now)
    {
        if (claim is null)
            return null;
        DateTimeOffset? decisionDeadline = claim.Status is ParcelClaimStatus.SUBMITTED or ParcelClaimStatus.UNDER_REVIEW
            ? BusinessDayDeadline.Add(claim.CreatedAt, parcel.DecisionSlaBusinessDaysSnapshot)
            : null;
        DateTimeOffset? payoutDeadline = claim.Status == ParcelClaimStatus.APPROVED && claim.DecidedAt.HasValue
            ? BusinessDayDeadline.Add(claim.DecidedAt.Value, parcel.PayoutSlaBusinessDaysSnapshot)
            : null;
        var deadline = decisionDeadline ?? payoutDeadline;
        return new ReliabilityClaimSummaryResponse(
            claim.Id,
            claim.Status.ToString(),
            claim.TotalAwardVnd,
            decisionDeadline,
            payoutDeadline,
            deadline.HasValue ? (deadline < now ? "BREACHED" : "ON_TRACK") : null);
    }

    private static bool IsIncidentClosed(ParcelIncidentStatus status)
        => status is ParcelIncidentStatus.RESOLVED
            or ParcelIncidentStatus.CLOSED
            or ParcelIncidentStatus.LOST_CONFIRMED;

    private static string IncidentSlaState(ParcelIncident incident, DateTimeOffset now)
    {
        if (IsIncidentClosed(incident.Status))
            return "CLOSED";
        if (!incident.SearchDeadline.HasValue)
            return "NOT_STARTED";
        if (incident.SearchDeadline.Value < now)
            return "BREACHED";
        return incident.SearchDeadline.Value <= now.AddHours(2) ? "DUE_SOON" : "ON_TRACK";
    }
}
