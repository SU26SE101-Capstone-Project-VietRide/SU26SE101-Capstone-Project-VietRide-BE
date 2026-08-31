using System.Text.Json;
using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Application.Features.Reliability.ApprovalRequests;

public sealed class ListParcelApprovalRequestsQueryHandler
    : IRequestHandler<ListParcelApprovalRequestsQuery, PagedResult<ParcelApprovalRequestListItem>>
{
    private readonly IParcelCustodyExceptionRequestRepository _custodyRequests;
    private readonly IParcelStopDepartureApprovalRepository _departureRequests;
    private readonly ITripServiceClient _trips;
    private readonly IParcelRepository _parcels;
    private readonly IParcelReliabilityRepository _reliability;

    public ListParcelApprovalRequestsQueryHandler(
        IParcelCustodyExceptionRequestRepository custodyRequests,
        IParcelStopDepartureApprovalRepository departureRequests,
        ITripServiceClient trips,
        IParcelRepository parcels,
        IParcelReliabilityRepository reliability)
    {
        _custodyRequests = custodyRequests;
        _departureRequests = departureRequests;
        _trips = trips;
        _parcels = parcels;
        _reliability = reliability;
    }

    public async Task<PagedResult<ParcelApprovalRequestListItem>> Handle(
        ListParcelApprovalRequestsQuery query,
        CancellationToken cancellationToken)
    {
        var includeCustody = query.Type is null
            || query.Type.Equals("CUSTODY_EXCEPTION", StringComparison.OrdinalIgnoreCase);
        var includeDeparture = query.Type is null
            || query.Type.Equals("STOP_DEPARTURE", StringComparison.OrdinalIgnoreCase);
        var custody = includeCustody
            ? await _custodyRequests.ListPendingByOperatorAsync(query.OperatorId, cancellationToken)
            : [];
        var departures = includeDeparture
            ? await _departureRequests.ListPendingByOperatorAsync(query.OperatorId, cancellationToken)
            : [];

        var tripIds = custody.Select(item => item.TripId)
            .Concat(departures.Select(item => item.TripId))
            .Distinct()
            .ToArray();
        var summaries = new List<TripSummarySnapshot>();
        foreach (var batch in tripIds.Chunk(100))
        {
            var outcome = await _trips.GetTripSummariesAsync(batch, cancellationToken);
            if (outcome.Kind != TripSummaryBatchOutcomeKind.Success)
                throw new ParcelDependencyUnavailableException(
                    "TRIP_SERVICE_UNAVAILABLE",
                    outcome.ErrorMessage ?? "Trip assignment context is unavailable.");
            summaries.AddRange(outcome.Summaries);
        }

        var assignedTripIds = summaries
            .Where(summary => summary.DriverUserId == query.DriverUserId)
            .Where(summary => !IsTerminal(summary.Status))
            .Select(summary => summary.TripId)
            .ToHashSet();
        var custodyParcelIds = custody.Select(item => item.ParcelId).Distinct().ToArray();
        var custodyIncidentIds = custody.Select(item => item.IncidentId).Distinct().ToArray();
        var parcels = new List<VietRide.Parcel.Domain.Entities.Parcel>();
        foreach (var batch in custodyParcelIds.Chunk(100))
            parcels.AddRange(await _parcels.ListByIdsAsync(batch, cancellationToken));
        var incidents = await _reliability.ListIncidentsByIdsAsync(
            custodyIncidentIds,
            cancellationToken);
        var parcelById = parcels.ToDictionary(parcel => parcel.Id);
        var incidentById = incidents.ToDictionary(incident => incident.Id);
        var items = custody
            .Where(item => assignedTripIds.Contains(item.TripId))
            .Where(item => IsCustodyRequestStillValid(item, parcelById, incidentById))
            .Select(item => new ParcelApprovalRequestListItem(
                item.Id,
                "CUSTODY_EXCEPTION",
                item.Status.ToString(),
                item.TripId,
                item.ParcelId,
                item.IncidentId,
                null,
                [],
                item.Reason,
                DeserializeStrings(item.EvidenceReferencesJson),
                item.ReportedByUserId,
                item.ReportedAt,
                null,
                "WHILE_PENDING_AND_CURRENT_TRIP_ASSIGNMENT",
                ["APPROVE", "REJECT"]))
            .Concat(departures
                .Where(item => assignedTripIds.Contains(item.TripId))
                .Select(item => new ParcelApprovalRequestListItem(
                    item.Id,
                    "STOP_DEPARTURE",
                    item.Status.ToString(),
                    item.TripId,
                    null,
                    null,
                    item.StopId,
                    DeserializeGuids(item.UnresolvedParcelIdsJson),
                    item.DepartureOverrideReason,
                    [],
                    item.RequestedByUserId,
                    item.RequestedAt,
                    null,
                    "WHILE_PENDING_AND_UNRESOLVED_SNAPSHOT_MATCHES_BEFORE_STOP_DEPARTURE",
                    ["APPROVE", "REJECT"])))
            .OrderByDescending(item => item.RequestedAt)
            .ThenBy(item => item.RequestId)
            .ToArray();

        var pageItems = items
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToArray();
        return PagedResult<ParcelApprovalRequestListItem>.Create(
            pageItems,
            query.Page,
            query.PageSize,
            items.LongLength);
    }

    private static bool IsTerminal(string status) =>
        status.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase)
        || status.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase);

    private static bool IsCustodyRequestStillValid(
        ParcelCustodyExceptionRequest request,
        IReadOnlyDictionary<Guid, VietRide.Parcel.Domain.Entities.Parcel> parcels,
        IReadOnlyDictionary<Guid, ParcelIncident> incidents)
        => parcels.TryGetValue(request.ParcelId, out var parcel)
            && parcel.TripId == request.TripId
            && parcel.Status == ParcelStatus.PENDING_OPERATOR_ACTION
            && parcel.PendingActionType == PendingActionType.CUSTODY_EXCEPTION
            && incidents.TryGetValue(request.IncidentId, out var incident)
            && incident.ParcelId == request.ParcelId
            && incident.Status is not (
                ParcelIncidentStatus.RESOLVED
                or ParcelIncidentStatus.CLOSED
                or ParcelIncidentStatus.LOST_CONFIRMED);

    private static IReadOnlyList<string> DeserializeStrings(string json)
        => JsonSerializer.Deserialize<string[]>(json) ?? [];

    private static IReadOnlyList<Guid> DeserializeGuids(string json)
        => JsonSerializer.Deserialize<Guid[]>(json) ?? [];
}
