using MediatR;
using System.Text.Json;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Internal.Trips.Cargo;

public sealed class CargoMutationCommandHandler : IRequestHandler<CargoMutationCommand, CargoCapacityDto>
{
    private const string NearFullEventType = "trip.cargo_near_full";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITripRepository tripRepository;
    private readonly IIntegrationEventOutbox outbox;
    private readonly IClock clock;

    public CargoMutationCommandHandler(
        ITripRepository tripRepository,
        IIntegrationEventOutbox outbox,
        IClock clock)
    {
        this.tripRepository = tripRepository;
        this.outbox = outbox;
        this.clock = clock;
    }

    public async Task<CargoCapacityDto> Handle(CargoMutationCommand request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        TripCargoMutationResult? result;
        try
        {
            result = request.Action switch
            {
                "reserve" => await tripRepository.ReserveCargoAsync(
                    request.TripId, request.ParcelId, request.WeightKg, now, cancellationToken),
                "load" => await tripRepository.LoadCargoAsync(
                    request.TripId, request.ParcelId, request.WeightKg, now, cancellationToken),
                "release" => await tripRepository.ReleaseCargoAsync(
                    request.TripId, request.ParcelId, now, cancellationToken),
                _ => throw new CodedValidationException("INVALID_CARGO_ACTION", "Cargo action is invalid."),
            };
        }
        catch (InvalidOperationException ex)
        {
            throw new CodedConflictException("TRIP_CARGO_CAPACITY_EXCEEDED", ex.Message);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new CodedValidationException("VALIDATION_ERROR", ex.Message);
        }

        if (result is null)
        {
            throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
        }

        if (result.NearFullCrossed)
        {
            await outbox.EnqueueAsync(
                NearFullEventType,
                JsonSerializer.Serialize(new
                {
                    tripId = result.TripId,
                    loadedWeightKg = result.LoadedWeightKg,
                    maxCargoWeightKg = result.MaxCargoWeightKg,
                    percentFull = result.PercentFull,
                    occurredAt = now,
                }, JsonOptions),
                cancellationToken);
        }

        return new CargoCapacityDto(
            result.TripId,
            result.ReservedWeightKg,
            result.LoadedWeightKg,
            result.MaxCargoWeightKg,
            result.PercentFull);
    }

}
