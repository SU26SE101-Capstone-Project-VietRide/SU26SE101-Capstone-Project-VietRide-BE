using System.Text.Json;
using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed class ArriveTripStopCommandHandler
    : IRequestHandler<ArriveTripStopCommand, ArriveTripStopResponse>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITripRepository trips;
    private readonly ITripStopRepository tripStops;
    private readonly IIntegrationEventOutbox outbox;
    private readonly IClock clock;

    public ArriveTripStopCommandHandler(
        ITripRepository trips,
        ITripStopRepository tripStops,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        this.trips = trips;
        this.tripStops = tripStops;
        this.outbox = outbox;
        this.clock = clock;
    }

    public async Task<ArriveTripStopResponse> Handle(
        ArriveTripStopCommand request,
        CancellationToken cancellationToken)
    {
        var trip = await trips.GetForUpdateAsync(request.TripId, cancellationToken)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");

        EnsureAssignedCrew(trip, request.ActorUserId);

        var lockedStops = await tripStops.AcquireByTripAsync(request.TripId, cancellationToken);
        var stop = lockedStops.SingleOrDefault(candidate => candidate.StopId == request.StopId)
            ?? throw new CodedNotFoundException("TRIP_STOP_NOT_FOUND", "Trip stop was not found.");

        if (stop.Status != TripStopStatus.PENDING)
        {
            throw new CodedConflictException(
                "TRIP_STOP_ALREADY_FINALIZED",
                "Trip stop has already been arrived or skipped.");
        }

        if (trip.Status != TripStatus.IN_PROGRESS)
        {
            throw new CodedValidationException(
                "TRIP_NOT_IN_PROGRESS",
                "Trip must be in progress before a stop arrival can be recorded.");
        }

        TripStopSequenceGuard.EnsureCanArriveStop(lockedStops, stop);

        var now = clock.UtcNow;
        stop.MarkArrived(now);
        var integrationEvent = new TripStopArrivedIntegrationEvent(
            trip.Id,
            stop.StopId,
            trip.OperatorId,
            request.ActorUserId,
            now);
        await outbox.EnqueueAsync(
            integrationEvent.EventType,
            JsonSerializer.Serialize(integrationEvent, JsonOptions),
            cancellationToken);

        return new ArriveTripStopResponse(
            trip.Id,
            stop.StopId,
            stop.Status.ToString(),
            now);
    }

    private static void EnsureAssignedCrew(Domain.Entities.Trip trip, Guid actorUserId)
    {
        if (trip.DriverUserId != actorUserId && trip.AssistantUserId != actorUserId)
        {
            throw new ForbiddenException(
                "FORBIDDEN",
                "Only the assigned driver or assistant can record an arrival for this trip.");
        }
    }
}
