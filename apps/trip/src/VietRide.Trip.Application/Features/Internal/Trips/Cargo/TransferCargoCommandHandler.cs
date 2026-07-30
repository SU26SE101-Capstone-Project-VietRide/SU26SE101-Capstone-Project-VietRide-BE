using System.Text.Json;
using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Internal.Trips.Cargo;

public sealed class TransferCargoCommandHandler
    : IRequestHandler<TransferCargoCommand, CargoTransferDto>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITripRepository tripRepository;
    private readonly IIntegrationEventOutbox outbox;
    private readonly IUnitOfWork unitOfWork;
    private readonly IClock clock;

    public TransferCargoCommandHandler(
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

    public async Task<CargoTransferDto> Handle(
        TransferCargoCommand request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        if (request.SourceTripId == request.TargetTripId)
        {
            throw TransferConflict("Source and target Trip must differ.");
        }

        var now = clock.UtcNow;
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await tripRepository.TransferCargoAsync(
                request.SourceTripId,
                request.ParcelId,
                request.TargetTripId,
                request.TargetState,
                request.AllowCapacityOverflow,
                now,
                cancellationToken);

            ThrowIfFailed(result.Status);
            if (result.NearFullCrossed)
            {
                var integrationEvent = new CargoThresholdCrossedIntegrationEvent(
                    Guid.NewGuid(),
                    now,
                    result.TargetTripId,
                    result.TargetOperatorId,
                    result.TargetLoadedWeightKg,
                    result.TargetMaxCargoWeightKg,
                    result.TargetPercentFull);

                await outbox.EnqueueAsync(
                    integrationEvent.EventId,
                    integrationEvent.EventType,
                    JsonSerializer.Serialize(integrationEvent, JsonOptions),
                    cancellationToken);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return new CargoTransferDto(
                result.ParcelId,
                result.SourceTripId,
                result.TargetTripId,
                result.TargetState,
                result.WeightKg,
                result.VolumeM3);
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static void Validate(TransferCargoCommand request)
    {
        var errors = new List<ValidationError>();
        if (request.SourceTripId == Guid.Empty)
        {
            errors.Add(new ValidationError("sourceTripId", "sourceTripId must be a non-empty UUID."));
        }

        if (request.ParcelId == Guid.Empty)
        {
            errors.Add(new ValidationError("parcelId", "parcelId must be a non-empty UUID."));
        }

        if (request.TargetTripId == Guid.Empty)
        {
            errors.Add(new ValidationError("targetTripId", "targetTripId must be a non-empty UUID."));
        }

        if (request.TargetState is not "RESERVED" and not "LOADED")
        {
            errors.Add(new ValidationError("targetState", "targetState must be RESERVED or LOADED."));
        }

        if (errors.Count > 0)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "The cargo transfer request is invalid.",
                errors);
        }
    }

    private static void ThrowIfFailed(TripCargoTransferStatus status)
    {
        switch (status)
        {
            case TripCargoTransferStatus.SUCCESS:
                return;
            case TripCargoTransferStatus.TRIP_NOT_FOUND:
                throw new CodedNotFoundException(
                    "TRIP_NOT_FOUND",
                    "The source or target Trip was not found.");
            case TripCargoTransferStatus.SOURCE_CARGO_NOT_FOUND:
                throw new CodedNotFoundException(
                    "PARCEL_CARGO_NOT_FOUND",
                    "The source Trip has no active cargo ledger for this Parcel.");
            case TripCargoTransferStatus.CAPACITY_EXCEEDED:
                throw new CodedValidationException(
                    "TRIP_CARGO_CAPACITY_EXCEEDED",
                    "The target Trip does not have enough cargo capacity.");
            case TripCargoTransferStatus.OVERFLOW_NOT_ALLOWED:
                throw new CodedValidationException(
                    "VALIDATION_ERROR",
                    "Cargo capacity overflow is allowed only for a loaded Vehicle Substitution target Trip.");
            case TripCargoTransferStatus.CONFLICT:
                throw TransferConflict("The cargo transfer lost a race or violates Trip ownership.");
            default:
                throw new InvalidOperationException($"Unknown cargo transfer status '{status}'.");
        }
    }

    private static CodedConflictException TransferConflict(string message) =>
        new("TRIP_CARGO_TRANSFER_CONFLICT", message);
}
