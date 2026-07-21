using System.Text.Json;
using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed class DepartStopHandler : IRequestHandler<DepartStopCommand, DepartStopResponse>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ITripRepository trips;
    private readonly ITripStopRepository tripStops;
    private readonly IStopRepository stops;
    private readonly IBookingImpactClient bookingImpact;
    private readonly IIntegrationEventOutbox outbox;
    private readonly IClock clock;

    public DepartStopHandler(
        ITripRepository trips,
        ITripStopRepository tripStops,
        IStopRepository stops,
        IBookingImpactClient bookingImpact,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        this.trips = trips;
        this.tripStops = tripStops;
        this.stops = stops;
        this.bookingImpact = bookingImpact;
        this.outbox = outbox;
        this.clock = clock;
    }

    public async Task<DepartStopResponse> Handle(
        DepartStopCommand request,
        CancellationToken cancellationToken)
    {
        ValidateIds(request);
        var trip = await trips.GetForUpdateAsync(request.TripId, cancellationToken)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");

        EnsureAssignedCrewAndTenant(trip, request);
        if (trip.Status != TripStatus.IN_PROGRESS)
        {
            throw new CodedValidationException(
                "TRIP_NOT_IN_PROGRESS",
                "Trip must be in progress before a stop departure can be recorded.");
        }

        var tripStop = await tripStops.GetForUpdateAsync(
            request.TripId,
            request.StopId,
            cancellationToken)
            ?? throw new CodedNotFoundException("TRIP_STOP_NOT_FOUND", "Trip stop was not found.");

        if (tripStop.ActualDepartureTime.HasValue)
        {
            throw new CodedConflictException(
                "TRIP_STOP_ALREADY_DEPARTED",
                "Trip stop departure has already been recorded.");
        }

        if (tripStop.Status != TripStopStatus.ARRIVED)
        {
            throw new CodedValidationException(
                "TRIP_STOP_NOT_ARRIVED",
                "Trip stop must be arrived before departure can be recorded.");
        }

        var stop = await stops.GetByIdAsync(request.StopId, cancellationToken)
            ?? throw new CodedNotFoundException("TRIP_STOP_NOT_FOUND", "Trip stop snapshot was not found.");
        if (stop.OperatorId != trip.OperatorId)
        {
            throw new ForbiddenException("FORBIDDEN", "Trip stop does not belong to this tenant.");
        }

        var departedAt = clock.UtcNow.ToUniversalTime();
        if (!await tripStops.TryMarkDepartedAsync(
                trip.Id,
                tripStop.StopId,
                departedAt,
                cancellationToken))
        {
            throw new CodedConflictException(
                "TRIP_STOP_ALREADY_DEPARTED",
                "Trip stop departure has already been recorded.");
        }

        TripStopPendingPassengerCountProjection projection;
        try
        {
            projection = await bookingImpact.GetPendingPassengerCountAsync(
                trip.Id,
                tripStop.StopId,
                trip.OperatorId,
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TripUpstreamUnavailableException("Booking pending-passenger count timed out.");
        }
        catch (HttpRequestException exception)
        {
            throw new TripUpstreamUnavailableException(
                "Booking pending-passenger count is unavailable.",
                exception);
        }

        var eventEmitted = projection.PendingPassengerCount > 0;
        if (eventEmitted)
        {
            var integrationEvent = new TripStopDepartedWithPendingIntegrationEvent(
                Guid.NewGuid(),
                departedAt,
                trip.Id,
                tripStop.StopId,
                stop.Name,
                projection.PendingPassengerCount,
                trip.DriverUserId,
                trip.AssistantUserId,
                departedAt);
            await outbox.EnqueueAsync(
                integrationEvent.EventId,
                integrationEvent.EventType,
                JsonSerializer.Serialize(integrationEvent, JsonOptions),
                cancellationToken);
        }

        return new DepartStopResponse(
            trip.Id,
            tripStop.StopId,
            departedAt,
            projection.PendingPassengerCount,
            eventEmitted);
    }

    private static void ValidateIds(DepartStopCommand request)
    {
        if (request.TripId == Guid.Empty
            || request.StopId == Guid.Empty
            || request.ActorUserId == Guid.Empty
            || request.OperatorId == Guid.Empty)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Trip, stop, actor, and operator ids must be non-empty UUIDs.");
        }
    }

    private static void EnsureAssignedCrewAndTenant(
        Domain.Entities.Trip trip,
        DepartStopCommand request)
    {
        var assigned = request.ActorRole switch
        {
            "DRIVER" => trip.DriverUserId == request.ActorUserId,
            "ASSISTANT" => trip.AssistantUserId == request.ActorUserId,
            _ => false,
        };
        if (!assigned || trip.OperatorId != request.OperatorId)
        {
            throw new ForbiddenException(
                "FORBIDDEN",
                "Trip is not assigned to this crew member and tenant.");
        }
    }
}
