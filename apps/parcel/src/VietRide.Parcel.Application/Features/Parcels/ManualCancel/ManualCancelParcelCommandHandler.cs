using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;
using VietRide.Parcel.Application.Features.Parcels.TripCancellationImpact;
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
        var reason = command.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 500)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Cancellation reason must contain between 1 and 500 characters.");
        }

        var choice = ParseRefundChoice(command.RefundChoice);
        var parcel = await parcelRepository.GetByIdAsync(
            command.ParcelId,
            cancellationToken);
        if (parcel is null)
        {
            throw new CodedNotFoundException(
                "PARCEL_NOT_FOUND",
                $"Parcel '{command.ParcelId}' not found.");
        }

        if (parcel.OperatorId != command.OperatorId)
        {
            throw new ForbiddenException(
                "FORBIDDEN",
                "Parcel does not belong to this operator.");
        }

        if (!ParcelTripCancellationClassifier.IsPreLoad(parcel.Status))
        {
            throw new CodedConflictException(
                "INVALID_STATUS",
                $"Parcel '{command.ParcelId}' is in status '{parcel.Status}' and cannot be manually cancelled.");
        }

        const ParcelStatus targetStatus = ParcelStatus.CANCELLED;
        var outstanding = ParcelTripCancellationClassifier.CalculateOutstandingCollected(
            parcel.DepositPaidVnd.Amount,
            parcel.BalancePaidVnd.Amount,
            parcel.RefundedAmountVnd.Amount);
        var refundAmount = await CalculateRefundAsync(
            choice,
            parcel.OperatorId,
            outstanding,
            cancellationToken);
        var refundDueVnd = Math.Max(
            parcel.RefundDueVnd.Amount,
            checked(parcel.RefundedAmountVnd.Amount + refundAmount));

        var now = DateTimeOffset.UtcNow;
        var snapshot = await parcelRepository.TryManualCancelAsync(
            command.ParcelId,
            command.OperatorId,
            targetStatus,
            reason,
            refundDueVnd,
            now,
            cancellationToken)
            ?? throw new CodedConflictException(
                "RACE_LOST",
                "Parcel status changed concurrently; cannot cancel.");

        await EnsureCargoSuccessAsync(
            await tripClient.ReleaseCargoAsync(
                parcel.TripId,
                parcel.Id,
                Positive(parcel.ActualWeightKg ?? parcel.EstimatedWeightKg),
                Positive(parcel.ActualVolumeM3 ?? parcel.EstimatedVolumeM3),
                ParcelOperationId.Create(
                    command.IdempotencyKey,
                    parcel.Id,
                    "MANUAL_CANCEL_CARGO_RELEASE"),
                cancellationToken));

        await ParcelOutboxEvents.EnqueueTerminalAsync(
            outbox,
            ParcelOperationId.Create(
                command.IdempotencyKey,
                parcel.Id,
                "MANUAL_CANCEL_TERMINAL"),
            now,
            targetStatus == ParcelStatus.REJECTED
                ? ParcelOutboxEvents.Rejected
                : ParcelOutboxEvents.Cancelled,
            snapshot.ParcelId,
            snapshot.ParcelCode,
            snapshot.OperatorId,
            snapshot.SenderUserId,
            snapshot.TripId,
            refundAmount,
            reason,
            cancellationToken);

        if (refundAmount > 0)
        {
            var refundReason = choice == ManualCancelRefundChoice.FULL
                ? "MANUAL_CANCEL_FULL"
                : "MANUAL_CANCEL_POLICY";
            await ParcelOutboxEvents.EnqueueCanonicalRefundAsync(
                outbox,
                ParcelOperationId.Create(
                    command.IdempotencyKey,
                    parcel.Id,
                    "MANUAL_CANCEL_REFUND_EVENT"),
                now,
                snapshot.ParcelId,
                snapshot.SenderUserId,
                refundAmount,
                refundReason,
                ParcelOperationId.Create(
                    command.IdempotencyKey,
                    parcel.Id,
                    "MANUAL_CANCEL_REFUND"),
                cancellationToken);
        }

        await statsRepository.UpsertIncrementAsync(
            snapshot.OperatorId,
            DateOnly.FromDateTime(now.UtcDateTime),
            0,
            0,
            0,
            1,
            0,
            0,
            refundAmount,
            cancellationToken);

        return new OperationalParcelResponse(
            snapshot.ParcelId,
            snapshot.ParcelCode,
            snapshot.Status.ToString(),
            TripId: snapshot.TripId,
            RefundChoice: choice.ToString(),
            RefundAmount: refundAmount);
    }

    private async Task<long> CalculateRefundAsync(
        ManualCancelRefundChoice choice,
        Guid operatorId,
        long outstanding,
        CancellationToken cancellationToken)
    {
        if (choice == ManualCancelRefundChoice.FULL)
        {
            return outstanding;
        }

        if (choice == ManualCancelRefundChoice.NO)
        {
            return 0;
        }

        var outcome = await identityClient.GetOperatorInfoAsync(
            operatorId,
            cancellationToken);
        if (outcome.Kind != OperatorLookupOutcomeKind.Success
            || outcome.OperatorInfo is null)
        {
            throw new ParcelDependencyUnavailableException(
                "UPSTREAM_UNAVAILABLE",
                outcome.ErrorMessage ?? "Parcel cancellation policy is unavailable.");
        }

        var policy = outcome.OperatorInfo.ParcelNoShowPolicy
            ?? ParcelNoShowPolicy.Default;
        if (policy.NoShowFeePercent is < 0 or > 100)
        {
            throw new ParcelDependencyUnavailableException(
                "UPSTREAM_UNAVAILABLE",
                "Parcel cancellation policy is malformed.");
        }

        return ParcelRefundAmountCalculator.ApplyNoShowFee(outstanding, policy);
    }

    private static ManualCancelRefundChoice ParseRefundChoice(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? "POLICY"
            : value.Trim().ToUpperInvariant();
        normalized = normalized switch
        {
            "FULL_REFUND" => "FULL",
            "POLICY_REFUND" => "POLICY",
            "NO_REFUND" => "NO",
            _ => normalized,
        };

        return Enum.TryParse<ManualCancelRefundChoice>(
            normalized,
            ignoreCase: false,
            out var choice)
            ? choice
            : throw new CodedValidationException(
                "INVALID_REFUND_CHOICE",
                "Refund choice must be FULL, POLICY, or NO.");
    }

    private static decimal Positive(decimal value)
        => value > 0 ? value : 0.0001m;

    private static Task EnsureCargoSuccessAsync(TripCargoOutcome outcome)
    {
        if (outcome is null)
        {
            return Task.CompletedTask;
        }

        return outcome.Kind switch
        {
            TripCargoOutcomeKind.Success => Task.CompletedTask,
            TripCargoOutcomeKind.TripNotFound => throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                outcome.ErrorMessage ?? "Trip was not found."),
            TripCargoOutcomeKind.CapacityExceeded => throw new ParcelDependencyUnavailableException(
                "TRIP_CARGO_TRANSFER_CONFLICT",
                outcome.ErrorMessage ?? "Trip cargo release lost a concurrent mutation."),
            _ => throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                outcome.ErrorMessage ?? "Trip service unavailable."),
        };
    }
}
