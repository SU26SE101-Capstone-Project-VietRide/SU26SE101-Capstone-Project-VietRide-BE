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

public sealed class DisruptNoSubstitutionCommandHandler : IRequestHandler<DisruptNoSubstitutionCommand, DisruptNoSubstitutionResponse>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string EventType = "trip.trip.disrupted";

    private readonly ITripRepository tripRepository;
    private readonly ITripStopRepository tripStopRepository;
    private readonly IIntegrationEventOutbox outbox;
    private readonly IUnitOfWork unitOfWork;
    private readonly IClock clock;

    public DisruptNoSubstitutionCommandHandler(
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

    public async Task<DisruptNoSubstitutionResponse> Handle(DisruptNoSubstitutionCommand request, CancellationToken cancellationToken)
    {
        var trip = await tripRepository.GetByIdAsync(request.TripId, cancellationToken)
            ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
        if (trip.OperatorId != request.OperatorId)
        {
            throw new ForbiddenException("FORBIDDEN", "Trip does not belong to this operator.");
        }

        var traveledRatio = await CalculateTraveledRatioAsync(trip.Id, cancellationToken);
        var now = clock.UtcNow;

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            trip.Disrupt(now, request.Reason);
            trip.MarkSubstitution(false);
            await outbox.EnqueueAsync(
                EventType,
                JsonSerializer.Serialize(new
                {
                    tripId = trip.Id,
                    operatorId = request.OperatorId,
                    hasSubstitution = false,
                    traveledRatio,
                    reason = request.Reason,
                    occurredAt = now,
                }, JsonOptions),
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return new DisruptNoSubstitutionResponse(trip.Id, trip.Status.ToString(), traveledRatio);
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<decimal> CalculateTraveledRatioAsync(Guid tripId, CancellationToken cancellationToken)
    {
        var stops = await tripStopRepository.QueryNoTracking()
            .Where(stop => stop.TripId == tripId)
            .OrderBy(stop => stop.OrderIndex)
            .ToListAsync(cancellationToken);
        if (stops.Count == 0 || stops.All(stop => stop.Status != TripStopStatus.ARRIVED))
        {
            return 0m;
        }

        var totalDistance = stops.Where(stop => stop.DistanceFromOriginKm.HasValue)
            .Select(stop => stop.DistanceFromOriginKm!.Value)
            .DefaultIfEmpty(0m)
            .Max();
        var lastArrived = stops.Last(stop => stop.Status == TripStopStatus.ARRIVED);

        if (totalDistance > 0m && lastArrived.DistanceFromOriginKm.HasValue)
        {
            return Math.Clamp(Math.Round(lastArrived.DistanceFromOriginKm.Value / totalDistance, 2), 0m, 1m);
        }

        return Math.Clamp(Math.Round((decimal)lastArrived.OrderIndex / stops.Count, 2), 0m, 1m);
    }
}
