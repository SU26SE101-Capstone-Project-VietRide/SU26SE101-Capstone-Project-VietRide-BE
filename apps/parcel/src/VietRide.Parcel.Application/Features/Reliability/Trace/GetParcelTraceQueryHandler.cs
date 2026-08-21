using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.Services;
using VietRide.Parcel.Application.Features.Reliability.ReadModels;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Reliability.Trace;

public sealed class GetParcelTraceQueryHandler
    : IRequestHandler<GetParcelTraceQuery, ParcelTraceResponse>
{
    private readonly IParcelRepository _parcels;
    private readonly IParcelReliabilityRepository _reliability;
    private readonly IParcelReliabilityReadModelService _screenModels;

    public GetParcelTraceQueryHandler(
        IParcelRepository parcels,
        IParcelReliabilityRepository reliability,
        IParcelReliabilityReadModelService screenModels)
    {
        _parcels = parcels;
        _reliability = reliability;
        _screenModels = screenModels;
    }

    public async Task<ParcelTraceResponse> Handle(
        GetParcelTraceQuery request,
        CancellationToken cancellationToken)
    {
        var parcel = await _parcels.GetByIdAsync(request.ParcelId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_NOT_FOUND", "Parcel was not found.");

        var authorized = request.UserId == parcel.SenderUserId
            || request.UserId == parcel.RecipientUserId
            || request.OperatorId == parcel.OperatorId;
        if (!authorized)
            throw new ForbiddenException("FORBIDDEN", "Caller is not authorized to view parcel tracking.");

        if (request.Limit is < 1 or > 100)
            throw new CodedValidationException("VALIDATION_ERROR", "limit must be between 1 and 100.");
        int? beforeSequence = null;
        if (!string.IsNullOrWhiteSpace(request.Cursor)
            && (!int.TryParse(request.Cursor, out var parsedCursor) || parsedCursor <= 0))
            throw new CodedValidationException("VALIDATION_ERROR", "cursor is invalid.");
        else if (!string.IsNullOrWhiteSpace(request.Cursor))
            beforeSequence = int.Parse(request.Cursor);

        var screens = await _screenModels.BuildAsync(
            [parcel],
            request.UserId,
            includeClaim: true,
            cancellationToken);
        // The read-model service and reliability repository share the scoped ParcelDbContext.
        // Keep same-database reads sequential; only the read-model's independent HTTP batches
        // run concurrently.
        var eventsPage = await _reliability.ListCustodyEventsPageAsync(
            parcel.Id,
            beforeSequence,
            request.Limit + 1,
            cancellationToken);
        var incidents = await _reliability.ListIncidentsByParcelAsync(parcel.Id, cancellationToken);
        var hasMore = eventsPage.Count > request.Limit;
        var events = eventsPage.Take(request.Limit).ToArray();
        var screen = screens[parcel.Id];

        return new ParcelTraceResponse(
            parcel.Id,
            parcel.ParcelCode,
            parcel.Status.ToString(),
            screen.Parcel,
            screen.Operator,
            screen.Trip,
            screen.DropoffLocation,
            screen.Reliability.CurrentCustody is null
                ? null
                : new ParcelCurrentCustodyResponse(
                    screen.Reliability.CurrentCustody.LastEventType,
                    screen.Reliability.CurrentCustody.LastConfirmedLocation.Type,
                    screen.Reliability.CurrentCustody.LastConfirmedLocation.Id,
                    screen.Reliability.CurrentCustody.LastConfirmedLocation.Name,
                    screen.Reliability.CurrentCustody.LastConfirmedAt,
                    screen.Reliability.CurrentCustody.CurrentTripId,
                    screen.Reliability.CurrentCustody.CurrentVehicleId,
                    screen.Reliability.CurrentCustody.TrackingConfidence),
            screen.Reliability.ActiveIncident,
            screen.ForwardingTrip,
            screen.Reliability.Claim,
            screen.Reliability.AvailableActions,
            new ParcelTraceTimelineResponse(events.Select(eventItem => new ParcelCustodyEventResponse(
                eventItem.Id,
                eventItem.EventType.ToString(),
                eventItem.TripId,
                eventItem.ExpectedLocationType?.ToString(),
                eventItem.ExpectedLocationId,
                eventItem.ActualLocationType?.ToString(),
                eventItem.ActualLocationId,
                eventItem.LocationSnapshot,
                eventItem.OccurredAt,
                eventItem.ActorRole,
                eventItem.Source,
                null,
                eventItem.Sequence)).ToArray(),
                hasMore ? events[^1].Sequence.ToString() : null),
            incidents.Select(incident => new ParcelIncidentResponse(
                incident.Id,
                incident.Type.ToString(),
                incident.Status.ToString(),
                incident.LastKnownLocation,
                incident.SearchDeadline,
                incident.CreatedAt,
                incident.ResolvedAt,
                incident.OperatorProcessBreach)).ToArray(),
            screen.Reliability.NextUpdateAt);
    }
}
