using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed class SubstituteVehicleCommandHandler : IRequestHandler<SubstituteVehicleCommand, SubstituteVehicleResponse>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string EventType = "trip.vehicle_substituted";

    private readonly ITripRepository tripRepository;
    private readonly ITripSeatRepository tripSeatRepository;
    private readonly ITripStopRepository tripStopRepository;
    private readonly ITripStopFareRepository tripStopFareRepository;
    private readonly IIntegrationEventOutbox outbox;
    private readonly IUnitOfWork unitOfWork;
    private readonly IClock clock;

    public SubstituteVehicleCommandHandler(
        ITripRepository tripRepository,
        ITripSeatRepository tripSeatRepository,
        ITripStopRepository tripStopRepository,
        ITripStopFareRepository tripStopFareRepository,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        this.tripRepository = tripRepository;
        this.tripSeatRepository = tripSeatRepository;
        this.tripStopRepository = tripStopRepository;
        this.tripStopFareRepository = tripStopFareRepository;
        this.outbox = outbox;
        this.unitOfWork = unitOfWork;
        this.clock = clock;
    }

    public async Task<SubstituteVehicleResponse> Handle(SubstituteVehicleCommand request, CancellationToken cancellationToken)
    {
        var oldTrip = await tripRepository.GetByIdAsync(request.TripId, cancellationToken)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
        if (oldTrip.OperatorId != request.OperatorId)
        {
            throw new ForbiddenException("FORBIDDEN", "Trip does not belong to this operator.");
        }

        var now = clock.UtcNow;
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            oldTrip.Disrupt(now, request.Reason);
            oldTrip.MarkSubstitution(true);

            var newTrip = Domain.Entities.Trip.Create(
                oldTrip.OperatorId,
                oldTrip.RouteId,
                request.NewVehicleId,
                request.NewDriverUserId,
                request.NewAssistantUserId,
                null,
                now,
                oldTrip.EstimatedArrivalTime > now ? oldTrip.EstimatedArrivalTime : now.AddHours(1),
                TripSource.VEHICLE_SUBSTITUTION,
                oldTrip.BaseFare,
                oldTrip.MaxCargoWeightKg,
                oldTrip.EstimatedPassengerLuggageKg,
                hasSubstitution: false);
            newTrip.MarkBoarding(now);
            await tripRepository.AddAsync(newTrip, cancellationToken);
            await CloneTripChildrenAsync(oldTrip, newTrip, cancellationToken);

            await outbox.EnqueueAsync(
                EventType,
                JsonSerializer.Serialize(new
                {
                    oldTripId = oldTrip.Id,
                    newTripId = newTrip.Id,
                    operatorId = request.OperatorId,
                    reason = request.Reason,
                    occurredAt = now,
                }, JsonOptions),
                cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return new SubstituteVehicleResponse(oldTrip.Id, newTrip.Id, oldTrip.Status.ToString());
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task CloneTripChildrenAsync(
        Domain.Entities.Trip oldTrip,
        Domain.Entities.Trip newTrip,
        CancellationToken cancellationToken)
    {
        var departureOffset = newTrip.DepartureDateTime - oldTrip.DepartureDateTime;

        var seats = await tripSeatRepository.QueryNoTracking()
            .Where(seat => seat.TripId == oldTrip.Id)
            .ToListAsync(cancellationToken);
        foreach (var seat in seats)
        {
            await tripSeatRepository.AddAsync(
                TripSeat.Create(
                    newTrip.Id,
                    seat.SeatNumber,
                    seat.SeatType,
                    seat.Status,
                    seat.DisabledReason),
                cancellationToken);
        }

        var stops = await tripStopRepository.QueryNoTracking()
            .Where(stop => stop.TripId == oldTrip.Id)
            .OrderBy(stop => stop.OrderIndex)
            .ToListAsync(cancellationToken);
        foreach (var stop in stops)
        {
            await tripStopRepository.AddAsync(
                TripStop.Create(
                    newTrip.Id,
                    stop.StopId,
                    stop.OrderIndex,
                    stop.EstimatedArrivalTime + departureOffset,
                    stop.AllowPickup,
                    stop.AllowDropoff,
                    stop.DistanceFromOriginKm),
                cancellationToken);
        }

        var fares = await tripStopFareRepository.QueryNoTracking()
            .Where(fare => fare.TripId == oldTrip.Id)
            .ToListAsync(cancellationToken);
        foreach (var fare in fares)
        {
            await tripStopFareRepository.AddAsync(
                TripStopFare.Create(newTrip.Id, fare.StopId, fare.FareFromThisStop),
                cancellationToken);
        }
    }
}
