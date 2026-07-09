using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;

namespace VietRide.Parcel.Application.Features.Parcels.ManualCancel;

public sealed class ManualCancelParcelCommandHandler
    : IRequestHandler<ManualCancelParcelCommand, OperationalParcelResponse>
{
    private readonly IParcelRepository parcelRepository;
    private readonly IIdentityServiceClient identityClient;
    private readonly ITripServiceClient tripClient;
    private readonly IIntegrationEventOutbox outbox;
    private readonly IParcelStatsRepository statsRepository;

    public ManualCancelParcelCommandHandler(
        IParcelRepository parcelRepository,
        IIdentityServiceClient identityClient,
        ITripServiceClient tripClient,
        IIntegrationEventOutbox outbox,
        IParcelStatsRepository statsRepository)
    {
        this.parcelRepository = parcelRepository;
        this.identityClient = identityClient;
        this.tripClient = tripClient;
        this.outbox = outbox;
        this.statsRepository = statsRepository;
    }

    public async Task<OperationalParcelResponse> Handle(
        ManualCancelParcelCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            throw new CodedValidationException("VALIDATION_ERROR", "Cancellation reason is required.");
        }

        var choice = ParseRefundChoice(command.RefundChoice);
        if (choice == ManualCancelRefundChoice.NO_REFUND && string.IsNullOrWhiteSpace(command.Reason))
        {
            throw new CodedValidationException("VALIDATION_ERROR", "Reason is required for NO_REFUND.");
        }

        var parcel = await parcelRepository.GetByIdAsync(command.ParcelId, cancellationToken);
        if (parcel is null)
        {
            throw new CodedNotFoundException("PARCEL_NOT_FOUND", $"Parcel '{command.ParcelId}' not found.");
        }

        if (parcel.OperatorId != command.OperatorId)
        {
            throw new ForbiddenException("FORBIDDEN", "Parcel does not belong to this operator.");
        }

        var targetStatus = parcel.Status switch
        {
            ParcelStatus.PENDING_PAYMENT or ParcelStatus.PENDING_OPERATOR_REVIEW => ParcelStatus.REJECTED,
            ParcelStatus.PENDING or ParcelStatus.PENDING_ADDITIONAL_PAYMENT => ParcelStatus.CANCELLED,
            _ => throw new CodedConflictException(
                "INVALID_STATUS",
                $"Parcel '{command.ParcelId}' is in status '{parcel.Status}' and cannot be manually cancelled."),
        };

        var now = DateTimeOffset.UtcNow;
        var snapshot = await parcelRepository.TryManualCancelAsync(
            command.ParcelId,
            command.OperatorId,
            targetStatus,
            command.Reason.Trim(),
            now,
            cancellationToken)
            ?? throw new CodedConflictException("RACE_LOST", "Parcel status changed concurrently; cannot cancel.");

        var refundAmount = targetStatus == ParcelStatus.CANCELLED
            ? await CalculateRefundAsync(choice, parcel, cancellationToken)
            : 0L;

        if (targetStatus == ParcelStatus.CANCELLED)
        {
            await EnsureCargoSuccessAsync(
                await tripClient.ReleaseCargoAsync(
                    parcel.TripId,
                    parcel.Id,
                    parcel.ActualWeightKg ?? parcel.EstimatedWeightKg,
                    parcel.ActualVolumeM3 ?? parcel.EstimatedVolumeM3,
                    cancellationToken));
        }

        await ParcelOutboxEvents.EnqueueAsync(
            outbox,
            targetStatus == ParcelStatus.REJECTED ? ParcelOutboxEvents.Rejected : ParcelOutboxEvents.Cancelled,
            new
            {
                parcelId = snapshot.ParcelId,
                parcelCode = snapshot.ParcelCode,
                operatorId = snapshot.OperatorId,
                userId = snapshot.SenderUserId,
                tripId = snapshot.TripId,
                refundAmount,
                reason = command.Reason.Trim(),
                refundChoice = choice.ToString(),
            },
            cancellationToken);

        if (refundAmount > 0)
        {
            await ParcelOutboxEvents.EnqueueRefundAsync(
                outbox,
                snapshot.ParcelId,
                snapshot.SenderUserId,
                refundAmount,
                cancellationToken);
        }

        await statsRepository.UpsertIncrementAsync(
            snapshot.OperatorId,
            DateOnly.FromDateTime(now.UtcDateTime),
            0, 0, 0, 1, 0, 0, refundAmount,
            cancellationToken);

        return new OperationalParcelResponse(
            snapshot.ParcelId,
            snapshot.ParcelCode,
            snapshot.Status.ToString(),
            null,
            null);
    }

    private async Task<long> CalculateRefundAsync(
        ManualCancelRefundChoice choice,
        Domain.Entities.Parcel parcel,
        CancellationToken cancellationToken)
    {
        var paidAmount = parcel.DepositAmount.Amount;
        if (parcel.AdditionalPaymentId.HasValue)
        {
            paidAmount += parcel.AdditionalAmount.Amount;
        }

        return choice switch
        {
            ManualCancelRefundChoice.FULL_REFUND => paidAmount,
            ManualCancelRefundChoice.NO_REFUND => 0,
            _ => await ParcelRefundAmountCalculator.CalculateRefundAsync(
                identityClient,
                parcel.OperatorId,
                paidAmount,
                cancellationToken),
        };
    }

    private static ManualCancelRefundChoice ParseRefundChoice(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ManualCancelRefundChoice.POLICY_REFUND;
        }

        return Enum.TryParse<ManualCancelRefundChoice>(value, ignoreCase: true, out var choice)
            ? choice
            : throw new CodedValidationException("INVALID_REFUND_CHOICE", "Refund choice is invalid.");
    }

    private static Task EnsureCargoSuccessAsync(TripCargoOutcome outcome)
    {
        if (outcome is null)
            return Task.CompletedTask;

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
