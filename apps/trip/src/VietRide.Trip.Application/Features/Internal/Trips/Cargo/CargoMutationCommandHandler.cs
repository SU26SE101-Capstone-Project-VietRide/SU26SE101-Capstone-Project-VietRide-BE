using System.Text.Json;
using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Exceptions;

namespace VietRide.Trip.Application.Features.Internal.Trips.Cargo;

public sealed class CargoMutationCommandHandler : IRequestHandler<CargoMutationCommand, CargoCapacityDto>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITripRepository tripRepository;
    private readonly IIntegrationEventOutbox outbox;
    private readonly IUnitOfWork unitOfWork;
    private readonly IClock clock;

    public CargoMutationCommandHandler(
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

    public async Task<CargoCapacityDto> Handle(CargoMutationCommand request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            TripCargoMutationResult? result;
            try
            {
                result = request.Action switch
                {
                    "reserve" => await tripRepository.ReserveCargoAsync(
                        request.TripId, request.ParcelId, request.WeightKg, request.VolumeM3, request.AllowCapacityOverflow, now, cancellationToken),
                    "remeasure" => await tripRepository.RemeasureReservedCargoAsync(
                        request.TripId, request.ParcelId, request.WeightKg, request.VolumeM3, request.AllowCapacityOverflow, now, cancellationToken),
                    "load" => await tripRepository.LoadCargoAsync(
                        request.TripId, request.ParcelId, request.WeightKg, request.VolumeM3, request.AllowCapacityOverflow, now, cancellationToken),
                    "release" => await tripRepository.ReleaseCargoAsync(
                        request.TripId, request.ParcelId, now, cancellationToken),
                    _ => throw new CodedValidationException("INVALID_CARGO_ACTION", "Cargo action is invalid."),
                };
            }
            catch (TripCargoCapacityExceededException ex)
            {
                throw new CodedConflictException("TRIP_CARGO_CAPACITY_EXCEEDED", ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                throw new CodedConflictException("TRIP_CARGO_STATE_INVALID", ex.Message);
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
                var integrationEvent = new CargoThresholdCrossedIntegrationEvent(
                    Guid.NewGuid(),
                    now,
                    result.TripId,
                    result.OperatorId,
                    result.LoadedWeightKg,
                    result.MaxCargoWeightKg,
                    result.PercentFull);

                await outbox.EnqueueAsync(
                    integrationEvent.EventId,
                    integrationEvent.EventType,
                    JsonSerializer.Serialize(integrationEvent, JsonOptions),
                    cancellationToken);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);

            return new CargoCapacityDto(
                result.TripId,
                result.ReservedWeightKg,
                result.ReservedVolumeM3,
                result.LoadedWeightKg,
                result.LoadedVolumeM3,
                result.MaxCargoWeightKg,
                result.MaxCargoVolumeM3,
                Math.Max(0m, result.MaxCargoWeightKg - result.ReservedWeightKg - result.LoadedWeightKg),
                Math.Max(0m, result.MaxCargoVolumeM3 - result.ReservedVolumeM3 - result.LoadedVolumeM3),
                result.PercentFull);
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
