using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Incidents.ReportIncident;

public sealed class ReportIncidentCommandHandler
    : IRequestHandler<ReportIncidentCommand, ReportIncidentResponse>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ITripRepository _trips;
    private readonly IIncidentRepository _incidents;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;

    public ReportIncidentCommandHandler(
        ITripRepository trips,
        IIncidentRepository incidents,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        _trips = trips;
        _incidents = incidents;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<ReportIncidentResponse> Handle(
        ReportIncidentCommand request,
        CancellationToken cancellationToken)
    {
        var trip = await _trips.GetForUpdateAsync(request.TripId, cancellationToken)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");

        if (trip.DriverUserId != request.ReporterUserId
            && trip.AssistantUserId != request.ReporterUserId)
        {
            throw new ForbiddenException(
                "FORBIDDEN",
                "Only the assigned driver or assistant can report an incident for this trip.");
        }

        if (trip.Status != TripStatus.IN_PROGRESS)
        {
            throw new CodedValidationException(
                "TRIP_NOT_IN_PROGRESS",
                "Trip must be in progress before an incident can be reported.");
        }

        var category = Enum.Parse<IncidentCategory>(request.Category, ignoreCase: false);
        var now = _clock.UtcNow;
        var incident = Incident.Create(
            trip.Id,
            request.ReporterUserId,
            category,
            request.Description,
            request.PhotoUrls,
            request.Latitude,
            request.Longitude,
            now);
        await _incidents.AddAsync(incident, cancellationToken);

        var integrationEvent = new IncidentReportedIntegrationEvent(
            incident.Id,
            incident.TripId,
            trip.OperatorId,
            incident.ReportedByUserId,
            incident.Category.ToString(),
            incident.Description,
            incident.PhotoUrls,
            incident.Latitude,
            incident.Longitude,
            incident.ReportedAt);
        await _outbox.EnqueueAsync(
            integrationEvent.EventType,
            JsonSerializer.Serialize(integrationEvent, JsonOptions),
            cancellationToken);

        return new ReportIncidentResponse(
            incident.Id,
            incident.TripId,
            incident.ReportedByUserId,
            incident.Category.ToString(),
            incident.Description,
            incident.PhotoUrls,
            incident.Latitude,
            incident.Longitude,
            incident.ReportedAt);
    }
}
