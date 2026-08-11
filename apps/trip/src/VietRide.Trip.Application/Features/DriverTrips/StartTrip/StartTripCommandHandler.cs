using System.Text.Json;
using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Events;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.DriverTrips.StartTrip;

public sealed class StartTripCommandHandler : IRequestHandler<StartTripCommand, StartTripResponse>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string EventType = "trip.trip.started";

    private readonly ITripRepository tripRepository;
    private readonly IIntegrationEventOutbox outbox;
    private readonly IUnitOfWork unitOfWork;
    private readonly IClock clock;
    private readonly IResourceAvailabilityService? resourceAvailability;
    private readonly ITripAssignmentAlertStore? assignmentAlerts;

    public StartTripCommandHandler(
        ITripRepository tripRepository,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork,
        IClock clock,
        IResourceAvailabilityService? resourceAvailability = null,
        ITripAssignmentAlertStore? assignmentAlerts = null)
    {
        this.tripRepository = tripRepository;
        this.outbox = outbox;
        this.unitOfWork = unitOfWork;
        this.clock = clock;
        this.resourceAvailability = resourceAvailability;
        this.assignmentAlerts = assignmentAlerts;
    }

    public async Task<StartTripResponse> Handle(StartTripCommand request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var trip = await tripRepository.AcquireForLifecycleTransitionAsync(request.TripId, cancellationToken)
                ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");

            if (trip.DriverUserId != request.ActorUserId)
            {
                throw new ForbiddenException("FORBIDDEN", "Trip is not assigned to this driver.");
            }

            try
            {
                if (resourceAvailability is not null)
                {
                    await resourceAvailability.ActivateTripAsync(trip.Id, now, cancellationToken);
                }
                trip.Start(now);
            }
            catch (InvalidOperationException exception)
            {
                throw new CodedConflictException("TRIP_INVALID_TRANSITION", exception.Message);
            }

            await outbox.EnqueueAsync(
                EventType,
                JsonSerializer.Serialize(new
                {
                    tripId = trip.Id,
                    actualDepartureTime = now,
                }, JsonOptions),
                cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);

            return new StartTripResponse(trip.Id, trip.Status.ToString(), now);
        }
        catch (CodedConflictException exception) when (
            assignmentAlerts is not null
            && GetError(exception, "conflictReason") == "RESOURCE_ACTIVE")
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            await PersistStartBlockedAlertAsync(request.TripId, exception, now, cancellationToken);
            throw;
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task PersistStartBlockedAlertAsync(
        Guid tripId,
        CodedConflictException exception,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var trip = await tripRepository.AcquireForLifecycleTransitionAsync(tripId, cancellationToken)
                ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
            if (await assignmentAlerts!.TryAddStartBlockedAsync(
                    trip.Id,
                    trip.OperatorId,
                    cancellationToken))
            {
                var integrationEvent = new TripAssignmentStartBlockedIntegrationEvent(
                    Guid.NewGuid(),
                    occurredAt,
                    trip.Id,
                    trip.OperatorId,
                    GetError(exception, "resourceRole") ?? "DRIVER",
                    ParseGuidError(exception, "resourceId"),
                    GetError(exception, "conflictingSourceType") ?? "TRIP",
                    ParseGuidError(exception, "conflictingSourceId"),
                    "RESOURCE_ACTIVE",
                    ParseDateTimeOffsetError(exception, "blockingUntil"));
                await outbox.EnqueueAsync(
                    integrationEvent.EventId,
                    integrationEvent.EventType,
                    JsonSerializer.Serialize(integrationEvent, JsonOptions),
                    cancellationToken);
            }

            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static string? GetError(CodedConflictException exception, string field) =>
        exception.Errors.FirstOrDefault(error => string.Equals(error.Field, field, StringComparison.Ordinal))?.Message;

    private static Guid ParseGuidError(CodedConflictException exception, string field) =>
        Guid.TryParse(GetError(exception, field), out var value) ? value : Guid.Empty;

    private static DateTimeOffset? ParseDateTimeOffsetError(CodedConflictException exception, string field) =>
        DateTimeOffset.TryParse(GetError(exception, field), out var value) ? value : null;
}
