using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;

namespace VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;

public sealed class ReturnParcelCommandHandler
    : IRequestHandler<ReturnParcelCommand, OperationalParcelResponse>
{
    private readonly IParcelRepository _parcelRepository;
    private readonly IIdentityServiceClient _identityClient;
    private readonly ITripServiceClient _tripClient;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IParcelStatsRepository _statsRepository;

    public ReturnParcelCommandHandler(
        IParcelRepository parcelRepository,
        IIdentityServiceClient identityClient,
        ITripServiceClient tripClient,
        IIntegrationEventOutbox outbox,
        IParcelStatsRepository statsRepository)
    {
        _parcelRepository = parcelRepository;
        _identityClient = identityClient;
        _tripClient = tripClient;
        _outbox = outbox;
        _statsRepository = statsRepository;
    }

    public async Task<OperationalParcelResponse> Handle(
        ReturnParcelCommand command,
        CancellationToken cancellationToken)
    {
        var parcel = await _parcelRepository.GetByIdAsync(command.ParcelId, cancellationToken);
        if (parcel is null)
            throw new CodedNotFoundException("PARCEL_NOT_FOUND", $"Parcel '{command.ParcelId}' not found.");

        if (parcel.OperatorId != command.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Parcel does not belong to this operator.");

        if (parcel.Status is not (ParcelStatus.PENDING_OPERATOR_ACTION or ParcelStatus.TRANSFER_ESCALATED))
            throw new CodedConflictException("INVALID_TRANSITION", $"Parcel status '{parcel.Status}' cannot be returned.");

        if (string.IsNullOrWhiteSpace(command.Reason))
            throw new CodedValidationException("VALIDATION_ERROR", "Return reason is required.");

        var fromStatus = parcel.Status;
        var now = DateTimeOffset.UtcNow;
        var snapshot = await _parcelRepository.TryReturnAsync(
            command.ParcelId,
            command.OperatorId,
            command.ReturnedByUserId,
            command.Reason.Trim(),
            now,
            cancellationToken);

        if (snapshot is null)
            throw new CodedConflictException("RACE_LOST", "Parcel status changed concurrently; cannot return.");

        await EnsureCargoSuccessAsync(
            await _tripClient.ReleaseCargoAsync(
                snapshot.TripId,
                snapshot.ParcelId,
                parcel.ActualWeightKg ?? parcel.EstimatedWeightKg,
                cancellationToken));

        var refundAmount = await ParcelRefundAmountCalculator.CalculateRefundAsync(
            _identityClient,
            snapshot.OperatorId,
            snapshot.DepositAmount + snapshot.AdditionalAmount,
            cancellationToken);

        await ParcelOutboxEvents.EnqueueAsync(
            _outbox,
            ParcelOutboxEvents.Returned,
            new
            {
                parcelId = snapshot.ParcelId,
                parcelCode = snapshot.ParcelCode,
                operatorId = snapshot.OperatorId,
                userId = snapshot.SenderUserId,
                tripId = snapshot.TripId,
                refundAmount,
            },
            cancellationToken);
        await ParcelOutboxEvents.EnqueueRefundAsync(
            _outbox,
            snapshot.ParcelId,
            snapshot.SenderUserId,
            refundAmount,
            cancellationToken);

        if (command.IsStatusOverride)
        {
            await ParcelOutboxEvents.EnqueueAsync(
                _outbox,
                ParcelOutboxEvents.StatusOverridden,
                new
                {
                    parcelId = snapshot.ParcelId,
                    operatorId = snapshot.OperatorId,
                    actorUserId = command.ReturnedByUserId,
                    fromStatus = fromStatus.ToString(),
                    toStatus = snapshot.Status.ToString(),
                    reason = command.Reason.Trim(),
                    timestamp = now,
                },
                cancellationToken);
        }

        await _statsRepository.UpsertIncrementAsync(
            snapshot.OperatorId,
            DateOnly.FromDateTime(now.UtcDateTime),
            0, 0, 0, 0, 1, 0, refundAmount,
            cancellationToken);

        return new OperationalParcelResponse(
            snapshot.ParcelId,
            snapshot.ParcelCode,
            snapshot.Status.ToString(),
            TripId: snapshot.TripId,
            ReturnReason: command.Reason.Trim(),
            ReturnedAt: now);
    }

    private static Task EnsureCargoSuccessAsync(TripCargoOutcome outcome)
    {
        return outcome.Kind switch
        {
            TripCargoOutcomeKind.Success => Task.CompletedTask,
            TripCargoOutcomeKind.TripNotFound => throw new ParcelDependencyUnavailableException(
                "TRIP_NOT_FOUND",
                outcome.ErrorMessage ?? "Trip was not found."),
            TripCargoOutcomeKind.CapacityExceeded => throw new ParcelDependencyUnavailableException(
                "TRIP_CARGO_CAPACITY_EXCEEDED",
                outcome.ErrorMessage ?? "Trip cargo capacity would be exceeded."),
            _ => throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                outcome.ErrorMessage ?? "Trip service unavailable."),
        };
    }
}
