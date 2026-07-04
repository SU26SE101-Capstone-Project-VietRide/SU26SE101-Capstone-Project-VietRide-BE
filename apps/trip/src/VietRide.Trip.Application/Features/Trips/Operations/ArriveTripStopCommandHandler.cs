using MediatR;
using System.Text.Json;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed class ArriveTripStopCommandHandler : IRequestHandler<ArriveTripStopCommand, ArriveTripStopResponse>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string EventType = "trip.stop.arrived";

    private readonly ITripRepository tripRepository;
    private readonly ITripStopRepository tripStopRepository;
    private readonly IIntegrationEventOutbox outbox;
    private readonly IUnitOfWork unitOfWork;
    private readonly IClock clock;

    public ArriveTripStopCommandHandler(
        ITripRepository tripRepository,
        ITripStopRepository tripStopRepository,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        this.tripRepository = tripRepository;
        this.tripStopRepository = tripStopRepository;
        this.outbox = outbox;
        this.unitOfWork = unitOfWork;
        this.clock = clock;
    }

    public async Task<ArriveTripStopResponse> Handle(ArriveTripStopCommand request, CancellationToken cancellationToken)
    {
        var trip = await tripRepository.GetByIdAsync(request.TripId, cancellationToken)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
        if (trip.OperatorId != request.OperatorId)
        {
            throw new ForbiddenException("FORBIDDEN", "Trip does not belong to this operator.");
        }

        if (trip.Status is not (TripStatus.BOARDING or TripStatus.IN_PROGRESS))
        {
            throw new CodedConflictException(
                "INVALID_TRIP_STATUS",
                $"Trip '{trip.Id}' is in status '{trip.Status}' and cannot mark stop arrival.");
        }

        var stop = await tripStopRepository.GetByIdAsync((request.TripId, request.StopId), cancellationToken)
            ?? throw new CodedNotFoundException("TRIP_STOP_NOT_FOUND", "Trip stop was not found.");
        if (stop.TripId != trip.Id)
        {
            throw new CodedNotFoundException("TRIP_STOP_NOT_FOUND", "Trip stop was not found.");
        }

        var now = clock.UtcNow;
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            try
            {
                stop.MarkArrived(now);
            }
            catch (InvalidOperationException ex)
            {
                throw new CodedConflictException(
                    "TRIP_STOP_ALREADY_FINALIZED",
                    ex.Message);
            }

            await outbox.EnqueueAsync(
                EventType,
                JsonSerializer.Serialize(new
                {
                    tripId = trip.Id,
                    stopId = stop.StopId,
                    operatorId = request.OperatorId,
                    actorUserId = request.ActorUserId,
                    actualArrivalTime = now,
                    occurredAt = now,
                }, JsonOptions),
                cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);

            return new ArriveTripStopResponse(trip.Id, stop.StopId, stop.Status.ToString(), now);
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
