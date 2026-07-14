using System.Text.Json;
using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;
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

    public StartTripCommandHandler(
        ITripRepository tripRepository,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        this.tripRepository = tripRepository;
        this.outbox = outbox;
        this.unitOfWork = unitOfWork;
        this.clock = clock;
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
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
