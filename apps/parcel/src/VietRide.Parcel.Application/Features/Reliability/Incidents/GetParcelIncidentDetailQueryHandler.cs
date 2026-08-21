using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Reliability.Claims;
using VietRide.Parcel.Application.Features.Reliability.ReadModels;
using VietRide.Parcel.Application.Features.Reliability.Trace;
using VietRide.Parcel.Application.Services;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Reliability.Incidents;

public sealed class GetParcelIncidentDetailQueryHandler
    : IRequestHandler<GetParcelIncidentDetailQuery, ParcelIncidentDetailResponse>
{
    private readonly IParcelReliabilityRepository _reliability;
    private readonly IParcelRepository _parcels;
    private readonly ITripServiceClient _trips;
    private readonly IIdentityServiceClient _identity;
    private readonly IClock _clock;

    public GetParcelIncidentDetailQueryHandler(
        IParcelReliabilityRepository reliability,
        IParcelRepository parcels,
        ITripServiceClient trips,
        IIdentityServiceClient identity,
        IClock clock)
    {
        _reliability = reliability;
        _parcels = parcels;
        _trips = trips;
        _identity = identity;
        _clock = clock;
    }

    public async Task<ParcelIncidentDetailResponse> Handle(
        GetParcelIncidentDetailQuery request,
        CancellationToken cancellationToken)
    {
        var incident = await _reliability.GetIncidentAsync(request.IncidentId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_INCIDENT_NOT_FOUND", "Incident was not found.");
        if (incident.OperatorId != request.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Incident does not belong to this operator.");
        if (request.Limit is < 1 or > 100 || request.BeforeSequence is < 1)
            throw new CodedValidationException("VALIDATION_ERROR", "Invalid custody timeline cursor or limit.");
        // Incident mutations use ExecuteUpdate for transfer fields, so bypass the tracked
        // aggregate to return the committed forwarding state in the same response.
        var parcel = await _parcels.QueryNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == incident.ParcelId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_NOT_FOUND", "Parcel was not found.");
        var tasks = await _reliability.ListSearchTasksAsync(incident.Id, cancellationToken);
        var current = await _reliability.GetCurrentCustodyAsync(incident.ParcelId, cancellationToken);
        var custodyEvents = await _reliability.ListCustodyEventsPageAsync(
            incident.ParcelId,
            request.BeforeSequence,
            request.Limit,
            cancellationToken);
        var claim = await _reliability.GetClaimByIncidentAsync(incident.Id, cancellationToken);
        var claimResponse = claim is null
            ? null
            : await ParcelClaimResponseMapper.MapAsync(
                claim,
                _reliability,
                cancellationToken,
                parcel,
                incident,
                operatorView: true,
                now: _clock.UtcNow);

        var tripIds = new[] { parcel.TripId, parcel.TransferTargetTripId ?? Guid.Empty }
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        var tripOutcome = await _trips.GetTripSummariesAsync(tripIds, cancellationToken);
        var tripById = tripOutcome.Kind == TripSummaryBatchOutcomeKind.Success
            ? tripOutcome.Summaries.ToDictionary(trip => trip.TripId)
            : new Dictionary<Guid, TripSummarySnapshot>();
        tripById.TryGetValue(parcel.TripId, out var tripSnapshot);
        var trip = ParcelReliabilityReadModelService.MapTrip(parcel, tripSnapshot);
        ReliabilityTripResponse? forwardingTrip = null;
        ParcelTransitLeg? forwardingLeg = null;
        if (parcel.TransferTargetTripId.HasValue
            && tripById.TryGetValue(parcel.TransferTargetTripId.Value, out var targetTrip))
        {
            forwardingTrip = ParcelReliabilityReadModelService.MapTrip(targetTrip);
            forwardingLeg = await _reliability.GetTransitLegAsync(
                parcel.Id,
                parcel.TransferTargetTripId.Value,
                cancellationToken);
        }

        var userIds = tasks.Where(task => task.AssigneeId.HasValue).Select(task => task.AssigneeId!.Value)
            .Append(parcel.SenderUserId)
            .Concat(parcel.RecipientUserId.HasValue ? new[] { parcel.RecipientUserId.Value } : [])
            .Concat(incident.ReporterId.HasValue ? new[] { incident.ReporterId.Value } : [])
            .Distinct()
            .Take(100)
            .ToArray();
        var userOutcome = await _identity.GetUsersAsync(userIds, cancellationToken);
        var users = userOutcome.Kind == IdentityUserBatchOutcomeKind.Success
            ? userOutcome.Users.ToDictionary(user => user.Id)
            : new Dictionary<Guid, IdentityUserSummary>();
        var taskResponses = tasks.Select(task => AssignIncidentSearchTasksCommandHandler.Map(task) with
        {
            Assignee = task.AssigneeId.HasValue
                ? ListParcelIncidentsQueryHandler.MapUser(task.AssigneeId.Value, users, null)
                : null,
        }).ToArray();
        var timelineItems = custodyEvents.Select(eventItem => new ParcelIncidentCustodyEventResponse(
            eventItem.Id,
            eventItem.EventType.ToString(),
            eventItem.LegId,
            eventItem.TripId,
            eventItem.ExpectedLocationType?.ToString(),
            eventItem.ExpectedLocationId,
            eventItem.ActualLocationType?.ToString(),
            eventItem.ActualLocationId,
            eventItem.LocationSnapshot,
            eventItem.VehicleId,
            eventItem.ActorId,
            eventItem.ActorRole,
            eventItem.OccurredAt,
            eventItem.CreatedAt,
            eventItem.Source,
            DeserializeEvidenceReferences(eventItem.EvidenceReferencesJson),
            eventItem.Reason,
            eventItem.Sequence)).ToArray();
        return new ParcelIncidentDetailResponse(
            MarkIncidentFoundCommandHandler.ToItem(incident) with
            {
                Parcel = ListParcelIncidentsQueryHandler.MapParcel(parcel),
                Trip = trip,
                ExpectedDropoff = ParcelReliabilityReadModelService.MapDropoff(parcel, trip),
                LastCustody = ParcelReliabilityReadModelService.MapCustody(current),
                ClaimSummary = ParcelReliabilityReadModelService.MapClaim(claim, parcel, _clock.UtcNow),
                AvailableActions = ParcelReliabilityActionResolver.Operator(incident, claim, _clock.UtcNow),
            },
            taskResponses,
            incident.ExpectedLocation,
            incident.ResolutionCode,
            incident.ResolutionNote,
            incident.ResolvedAt,
            current is null
                ? null
                : new ParcelCurrentCustodyResponse(
                    current.LastEventType.ToString(),
                    current.LastLocationType?.ToString(),
                    current.LastLocationId,
                    current.LastLocationSnapshot,
                    current.LastConfirmedAt,
                    current.CurrentTripId,
                    current.CurrentVehicleId,
                    current.TrackingConfidence.ToString()),
            new ParcelIncidentCustodyTimelineResponse(
                timelineItems,
                timelineItems.Length == request.Limit
                    ? timelineItems[^1].Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : null),
            claimResponse,
            ListParcelIncidentsQueryHandler.MapParcel(parcel),
            ListParcelIncidentsQueryHandler.MapUser(parcel.SenderUserId, users, "SENDER"),
            parcel.RecipientUserId.HasValue
                ? ListParcelIncidentsQueryHandler.MapUser(parcel.RecipientUserId.Value, users, "RECIPIENT")
                : new OperatorUserSummaryResponse(
                    null,
                    parcel.RecipientName,
                    parcel.RecipientPhone.ToString(),
                    parcel.RecipientEmail,
                    null,
                    "RECIPIENT"),
            trip,
            ParcelReliabilityReadModelService.MapDropoff(parcel, trip),
            incident.ReporterId.HasValue
                ? ListParcelIncidentsQueryHandler.MapUser(incident.ReporterId.Value, users, incident.ReporterSource)
                : new OperatorUserSummaryResponse(null, null, null, null, null, incident.ReporterSource),
            forwardingTrip,
            ParcelReliabilityActionResolver.Operator(incident, claim, _clock.UtcNow),
            MapForwardingOperation(forwardingTrip, forwardingLeg));
    }

    private static IReadOnlyList<string> DeserializeEvidenceReferences(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static ParcelForwardingOperationResponse? MapForwardingOperation(
        ReliabilityTripResponse? targetTrip,
        ParcelTransitLeg? leg)
    {
        if (targetTrip is null)
            return null;
        var transferred = leg?.Status == ParcelTransitLegStatus.ACTIVE;
        return new ParcelForwardingOperationResponse(
            targetTrip,
            leg is null
                ? null
                : new ParcelTransitLegResponse(
                    leg.Id,
                    leg.TripId,
                    leg.Sequence,
                    leg.Status.ToString(),
                    leg.ExpectedOriginId,
                    leg.ExpectedDestinationId,
                    leg.ExpectedOriginName,
                    leg.ExpectedDestinationName,
                    leg.VehicleId,
                    leg.StartedAt,
                    leg.EndedAt),
            transferred ? "TRANSFERRED" : "AWAITING_CREW_CONFIRMATION",
            transferred ? "DELIVER_AT_EXPECTED_DROPOFF" : "CREW_CONFIRM_TRANSFER");
    }
}
