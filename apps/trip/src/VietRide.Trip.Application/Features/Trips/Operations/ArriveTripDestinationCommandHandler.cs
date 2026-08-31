using System.Text.Json;
using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed class ArriveTripDestinationCommandHandler
    : IRequestHandler<ArriveTripDestinationCommand, ArriveTripDestinationResponse>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITripRepository trips;
    private readonly ITripStopRepository tripStops;
    private readonly IRouteRepository routes;
    private readonly IIntegrationEventOutbox outbox;
    private readonly IClock clock;

    public ArriveTripDestinationCommandHandler(
        ITripRepository trips,
        ITripStopRepository tripStops,
        IRouteRepository routes,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        this.trips = trips;
        this.tripStops = tripStops;
        this.routes = routes;
        this.outbox = outbox;
        this.clock = clock;
    }

    public async Task<ArriveTripDestinationResponse> Handle(
        ArriveTripDestinationCommand request,
        CancellationToken cancellationToken)
    {
        var trip = await trips.GetForUpdateAsync(request.TripId, cancellationToken)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");

        EnsureAssignedCrew(trip, request.ActorUserId);

        if (trip.DestinationArrivedAt.HasValue)
        {
            throw new CodedConflictException(
                "TRIP_DESTINATION_ALREADY_ARRIVED",
                "Trip destination arrival has already been recorded.");
        }

        if (trip.Status != TripStatus.IN_PROGRESS)
        {
            throw new CodedValidationException(
                "TRIP_NOT_IN_PROGRESS",
                "Trip must be in progress before destination arrival can be recorded.");
        }

        var lockedStops = await tripStops.AcquireByTripAsync(trip.Id, cancellationToken);
        TripStopSequenceGuard.EnsureCanArriveDestination(lockedStops);

        var route = await routes.GetByIdAsync(trip.RouteId, cancellationToken)
            ?? throw new CodedNotFoundException(
                "TRIP_NOT_FOUND",
                "Trip route snapshot was not found.");

        var now = clock.UtcNow;
        trip.MarkDestinationArrived(now, request.ActorUserId);
        var integrationEvent = new TripDestinationArrivedIntegrationEvent(
            trip.Id,
            route.DestinationStationId,
            trip.OperatorId,
            request.ActorUserId,
            now);
        await outbox.EnqueueAsync(
            integrationEvent.EventType,
            JsonSerializer.Serialize(integrationEvent, JsonOptions),
            cancellationToken);

        return new ArriveTripDestinationResponse(
            trip.Id,
            route.DestinationStationId,
            "ARRIVED",
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
