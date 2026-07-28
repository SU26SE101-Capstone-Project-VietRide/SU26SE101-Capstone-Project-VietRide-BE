using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Parcels.ConfirmPaymentForParcel;

public sealed class ConfirmPaymentForParcelCommandHandler
    : IRequestHandler<ConfirmPaymentForParcelCommand, bool>
{
    private const string ParcelReferenceType = "PARCEL";
    private const string ParcelAdditionalReferenceType = "PARCEL_ADDITIONAL";

    private readonly IParcelRepository _parcelRepository;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IParcelStatsRepository _statsRepository;
    private readonly ITripServiceClient _tripClient;
    private readonly IClock _clock;
    private readonly ILogger<ConfirmPaymentForParcelCommandHandler> _logger;
    private readonly IBookingServiceClient? _bookingClient;

    public ConfirmPaymentForParcelCommandHandler(
        IParcelRepository parcelRepository,
        IIntegrationEventOutbox outbox,
        IParcelStatsRepository statsRepository,
        ITripServiceClient tripClient,
        IClock clock,
        ILogger<ConfirmPaymentForParcelCommandHandler> logger,
        IBookingServiceClient? bookingClient = null)
    {
        _parcelRepository = parcelRepository;
        _outbox = outbox;
        _statsRepository = statsRepository;
        _tripClient = tripClient;
        _clock = clock;
        _logger = logger;
        _bookingClient = bookingClient;
    }

    public async Task<bool> Handle(ConfirmPaymentForParcelCommand request, CancellationToken cancellationToken)
    {
        if (string.Equals(request.ReferenceType, ParcelReferenceType, StringComparison.OrdinalIgnoreCase))
        {
            var now = _clock.UtcNow;
            var paidAt = request.PaidAt ?? now;
            if (request.DueAt.HasValue && paidAt >= request.DueAt.Value)
            {
                var expired = await _parcelRepository.TryMarkDepositExpiredAsync(
                    request.ReferenceId,
                    now,
                    cancellationToken);
                if (expired is not null)
                {
                    await ReleaseDepositCargoAsync(request.ReferenceId, request.PaymentId, cancellationToken);
                }

                _logger.LogWarning(
                    "Late deposit payment {PaymentId} for parcel {ParcelId} was paid at {PaidAt}, due at {DueAt}; it is not recognized by Parcel.",
                    request.PaymentId,
                    request.ReferenceId,
                    paidAt,
                    request.DueAt.Value);
                return true;
            }

            var snapshot = await _parcelRepository.TryMarkDepositSucceededAsync(
                request.ReferenceId, request.Amount, now, cancellationToken);
            if (snapshot is null)
            {
                var parcel = await _parcelRepository.GetByIdAsync(request.ReferenceId, cancellationToken);
                if (parcel is not null
                    && parcel.SettlementPolicyVersion >= ParcelCargoCalculator.SettlementPolicyVersion
                    && parcel.Status == ParcelStatus.EXPIRED
                    && request.DueAt.HasValue
                    && paidAt < request.DueAt.Value)
                {
                    return await ReconcileExpiredDepositAsync(
                        request,
                        parcel,
                        now,
                        cancellationToken);
                }

                return await HandleLateOrMismatchedSuccessAsync(
                    request.PaymentId,
                    request.ReferenceId,
                    request.Amount,
                    isAdditionalPayment: false,
                    cancellationToken);
            }
            await ConsumeVoucherIfNeededAsync(request.ReferenceId, request.Method, cancellationToken);
            await _statsRepository.UpsertIncrementAsync(
                snapshot.OperatorId,
                DateOnly.FromDateTime(now.UtcDateTime),
                0, 0, 0, 0, 0, snapshot.DepositAmount, 0,
                cancellationToken);
            await TryReserveCargoAfterPaymentAsync(snapshot, request.PaymentId, cancellationToken);
            return true;
        }

        if (string.Equals(request.ReferenceType, ParcelAdditionalReferenceType, StringComparison.OrdinalIgnoreCase))
        {
            var parcel = await _parcelRepository.GetByIdAsync(request.ReferenceId, cancellationToken);
            if (parcel?.SettlementPolicyVersion >= ParcelCargoCalculator.SettlementPolicyVersion)
            {
                return await HandleBalanceSucceededAsync(
                    request,
                    parcel,
                    cancellationToken);
            }

            var now = _clock.UtcNow;
            var snapshot = await _parcelRepository.TryMarkAdditionalSucceededAsync(
                request.ReferenceId, request.Amount, request.PaymentId, now, cancellationToken);
            if (snapshot is null)
            {
                return await HandleLateOrMismatchedSuccessAsync(
                    request.PaymentId,
                    request.ReferenceId,
                    request.Amount,
                    isAdditionalPayment: true,
                    cancellationToken);
            }
            await _statsRepository.UpsertIncrementAsync(
                snapshot.OperatorId,
                DateOnly.FromDateTime(now.UtcDateTime),
                0, 0, 0, 0, 0, snapshot.AdditionalAmount, 0,
                cancellationToken);
            await TryReserveCargoAfterPaymentAsync(snapshot, request.PaymentId, cancellationToken);
            return true;
        }

        return false;
    }

    private async Task<bool> ReconcileExpiredDepositAsync(
        ConfirmPaymentForParcelCommand request,
        Domain.Entities.Parcel parcel,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (request.Amount != parcel.DepositRequiredVnd.Amount)
            return false;

        var canStillServe = await CanRestoreDepositCargoAsync(
            parcel,
            request.PaymentId,
            now,
            cancellationToken);
        var refundAmount = canStillServe ? 0L : request.Amount;
        var snapshot = await _parcelRepository.TryReconcileExpiredDepositAsync(
            parcel.Id,
            request.PaymentId,
            request.Amount,
            canStillServe,
            VietRide.Shared.Kernel.ValueObjects.Money.FromRaw(refundAmount),
            "PAYMENT_CALLBACK_DELAY_CANNOT_SERVE",
            now,
            cancellationToken);
        if (snapshot is null)
            return false;

        if (canStillServe)
        {
            await ConsumeVoucherIfNeededAsync(parcel.Id, request.Method, cancellationToken);
        }
        else
        {
            await ParcelOutboxEvents.EnqueueRefundAsync(
                _outbox,
                parcel.Id,
                parcel.SenderUserId,
                refundAmount,
                $"{parcel.Id:D}:PAYMENT_CALLBACK_DELAY_CANNOT_SERVE",
                cancellationToken);
        }

        await _statsRepository.UpsertIncrementAsync(
            snapshot.OperatorId,
            DateOnly.FromDateTime(now.UtcDateTime),
            0, 0, 0, canStillServe ? 0 : 1, 0, request.Amount, refundAmount,
            cancellationToken);
        return true;
    }

    private async Task<bool> CanRestoreDepositCargoAsync(
        Domain.Entities.Parcel parcel,
        Guid paymentId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!parcel.LatestCheckInAt.HasValue || now >= parcel.LatestCheckInAt.Value)
            return false;

        var trip = await _tripClient.GetTripParcelSnapshotAsync(parcel.TripId, cancellationToken);
        if (trip.Kind == TripSnapshotOutcomeKind.TransportError)
        {
            throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                trip.ErrorMessage ?? "Trip service unavailable during deposit reconciliation.");
        }

        if (trip.Kind != TripSnapshotOutcomeKind.Success
            || trip.Snapshot is null
            || trip.Snapshot.Status is not ("SCHEDULED" or "BOARDING"))
        {
            return false;
        }

        var cargo = await _tripClient.ReserveCargoAsync(
            parcel.TripId,
            parcel.Id,
            parcel.EstimatedWeightKg,
            parcel.EstimatedVolumeM3,
            paymentId,
            cancellationToken);
        if (cargo.Kind == TripCargoOutcomeKind.TransportError)
        {
            throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                cargo.ErrorMessage ?? "Trip cargo reservation failed during deposit reconciliation.");
        }

        return cargo.Kind == TripCargoOutcomeKind.Success;
    }

    private async Task ReleaseDepositCargoAsync(
        Guid parcelId,
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        var parcel = await _parcelRepository.GetByIdAsync(parcelId, cancellationToken);
        if (parcel is null)
            return;

        await _tripClient.ReleaseCargoAsync(
            parcel.TripId,
            parcel.Id,
            parcel.EstimatedWeightKg,
            parcel.EstimatedVolumeM3,
            paymentId,
            cancellationToken);
    }

    private async Task<bool> HandleBalanceSucceededAsync(
        ConfirmPaymentForParcelCommand request,
        Domain.Entities.Parcel parcel,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var paidAt = request.PaidAt ?? now;
        if (!parcel.FinalPaymentDeadline.HasValue
            || request.Amount != parcel.BalanceRequiredVnd.Amount)
        {
            _logger.LogError(
                "Balance payment {PaymentId} does not match parcel {ParcelId} settlement. Expected {ExpectedAmount}, got {PaidAmount}.",
                request.PaymentId,
                parcel.Id,
                parcel.BalanceRequiredVnd.Amount,
                request.Amount);
            return false;
        }

        if (paidAt >= parcel.FinalPaymentDeadline.Value)
        {
            _logger.LogWarning(
                "Late balance payment {PaymentId} for parcel {ParcelId} was paid at {PaidAt}, deadline {Deadline}; it is not recognized by Parcel.",
                request.PaymentId,
                parcel.Id,
                paidAt,
                parcel.FinalPaymentDeadline.Value);
            return true;
        }

        var snapshot = await _parcelRepository.TryMarkBalanceSucceededAsync(
            parcel.Id,
            request.PaymentId,
            request.Amount,
            paidAt,
            now,
            cancellationToken);
        if (snapshot is not null)
        {
            await _statsRepository.UpsertIncrementAsync(
                snapshot.OperatorId,
                DateOnly.FromDateTime(now.UtcDateTime),
                0, 0, 0, 0, 0, request.Amount, 0,
                cancellationToken);
            return true;
        }

        parcel = await _parcelRepository.GetByIdAsync(parcel.Id, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_NOT_FOUND", $"Parcel '{request.ReferenceId}' not found.");
        if (parcel.Status == ParcelStatus.READY_TO_LOAD
            && parcel.BalancePaymentId == request.PaymentId
            && parcel.BalancePaidVnd.Amount == request.Amount)
        {
            return true;
        }

        if (parcel.Status != ParcelStatus.REJECTED
            || !string.Equals(parcel.RejectionReason, "FINAL_PAYMENT_TIMEOUT", StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "On-time balance payment {PaymentId} cannot reconcile parcel {ParcelId} from status {Status}/{Reason}.",
                request.PaymentId,
                parcel.Id,
                parcel.Status,
                parcel.RejectionReason);
            return false;
        }

        var canStillServe = await CanRestoreCargoAsync(parcel, request.PaymentId, now, cancellationToken);
        var refundAmount = canStillServe
            ? 0L
            : checked(parcel.DepositPaidVnd.Amount + request.Amount);
        snapshot = await _parcelRepository.TryReconcileTimedOutBalanceAsync(
            parcel.Id,
            request.PaymentId,
            request.Amount,
            paidAt,
            canStillServe,
            VietRide.Shared.Kernel.ValueObjects.Money.FromRaw(refundAmount),
            "PAYMENT_CALLBACK_DELAY_CANNOT_SERVE",
            now,
            cancellationToken);
        if (snapshot is null)
            return false;

        if (!canStillServe)
        {
            await ParcelOutboxEvents.EnqueueRefundAsync(
                _outbox,
                parcel.Id,
                parcel.SenderUserId,
                refundAmount,
                $"{parcel.Id:D}:PAYMENT_CALLBACK_DELAY_CANNOT_SERVE",
                cancellationToken);
        }

        var recoveredEventId = Guid.NewGuid();
        await ParcelOutboxEvents.EnqueueAsync(
            _outbox,
            recoveredEventId,
            ParcelOutboxEvents.SettlementRecovered,
            new
            {
                eventId = recoveredEventId,
                occurredAt = now,
                parcelId = snapshot.ParcelId,
                parcelCode = snapshot.ParcelCode,
                userId = snapshot.SenderUserId,
                tripId = snapshot.TripId,
                recoveredStatus = snapshot.Status.ToString(),
                refundAmountVnd = refundAmount,
            },
            cancellationToken);

        await _statsRepository.UpsertIncrementAsync(
            snapshot.OperatorId,
            DateOnly.FromDateTime(now.UtcDateTime),
            0, 0, 0, canStillServe ? 0 : 1, 0, request.Amount, refundAmount,
            cancellationToken);
        return true;
    }

    private async Task<bool> CanRestoreCargoAsync(
        Domain.Entities.Parcel parcel,
        Guid paymentId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!parcel.LoadCutoffAt.HasValue || now >= parcel.LoadCutoffAt.Value)
            return false;

        var trip = await _tripClient.GetTripParcelSnapshotAsync(parcel.TripId, cancellationToken);
        if (trip.Kind == TripSnapshotOutcomeKind.TransportError)
        {
            throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                trip.ErrorMessage ?? "Trip service unavailable during payment reconciliation.");
        }

        if (trip.Kind != TripSnapshotOutcomeKind.Success
            || trip.Snapshot is null
            || trip.Snapshot.Status is not ("SCHEDULED" or "BOARDING"))
        {
            return false;
        }

        var cargo = await _tripClient.ReserveCargoAsync(
            parcel.TripId,
            parcel.Id,
            parcel.ActualWeightKg ?? parcel.EstimatedWeightKg,
            parcel.ActualVolumeM3 ?? parcel.EstimatedVolumeM3,
            paymentId,
            cancellationToken);
        if (cargo.Kind == TripCargoOutcomeKind.TransportError)
        {
            throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                cargo.ErrorMessage ?? "Trip cargo reservation failed during payment reconciliation.");
        }

        return cargo.Kind == TripCargoOutcomeKind.Success;
    }

    private async Task ConsumeVoucherIfNeededAsync(
        Guid parcelId,
        string? paymentMethod,
        CancellationToken cancellationToken)
    {
        if (_bookingClient is null)
            return;

        var parcel = await _parcelRepository.GetByIdAsync(parcelId, cancellationToken);
        if (parcel is null
            || parcel.SettlementPolicyVersion < ParcelCargoCalculator.SettlementPolicyVersion
            || string.IsNullOrWhiteSpace(parcel.VoucherCode)
            || parcel.VoucherUsageId.HasValue)
            return;

        var trip = await _tripClient.GetTripParcelSnapshotAsync(parcel.TripId, cancellationToken);
        if (trip.Kind != TripSnapshotOutcomeKind.Success || trip.Snapshot is null)
            throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                trip.ErrorMessage ?? "Trip snapshot is unavailable for voucher consumption.");
        var validation = await _bookingClient.ValidateVoucherAsync(
            parcel.VoucherCode,
            parcel.OperatorId,
            trip.Snapshot.RouteId,
            parcel.SenderUserId,
            parcel.EstimatedGrossPriceVnd.Amount,
            paymentMethod ?? "VNPAY",
            cancellationToken);
        if (validation.Kind != VoucherValidationOutcomeKind.Success
            || !validation.VoucherId.HasValue
            || validation.DiscountAmount != parcel.DiscountAmountVnd.Amount)
            throw new CodedValidationException(
                "VOUCHER_NOT_APPLICABLE",
                validation.ErrorMessage ?? "Voucher is no longer applicable.");

        var usage = await _bookingClient.RecordVoucherUsageAsync(
            validation.VoucherId.Value,
            parcel.SenderUserId,
            parcel.Id,
            parcel.DiscountAmountVnd.Amount,
            cancellationToken);
        if (usage.Kind != VoucherUsageOutcomeKind.Success || !usage.UsageId.HasValue)
            throw new ParcelDependencyUnavailableException(
                "BOOKING_SERVICE_UNAVAILABLE",
                usage.ErrorMessage ?? "Voucher usage could not be recorded.");
        parcel.AttachVoucherUsage(usage.UsageId.Value);
    }

    private async Task<bool> HandleLateOrMismatchedSuccessAsync(
        Guid paymentId,
        Guid parcelId,
        long paidAmount,
        bool isAdditionalPayment,
        CancellationToken cancellationToken)
    {
        var snapshot = await _parcelRepository.GetPaymentTransitionSnapshotAsync(parcelId, cancellationToken);
        if (snapshot is null)
        {
            _logger.LogWarning(
                "Payment succeeded event {PaymentId} references missing parcel {ParcelId}.",
                paymentId, parcelId);
            return false;
        }

        var expectedAmount = isAdditionalPayment ? snapshot.AdditionalAmount : snapshot.DepositAmount;
        if (paidAmount != expectedAmount)
        {
            _logger.LogError(
                "Payment succeeded event {PaymentId} amount mismatch for parcel {ParcelId}. Expected {ExpectedAmount}, got {PaidAmount}.",
                paymentId, parcelId, expectedAmount, paidAmount);
            return false;
        }

        if (!IsTerminal(snapshot.Status))
        {
            _logger.LogInformation(
                "Payment succeeded event {PaymentId} ignored for parcel {ParcelId}; parcel status is {ParcelStatus}.",
                paymentId, parcelId, snapshot.Status);
            return false;
        }

        await ParcelOutboxEvents.EnqueueRefundAsync(
            _outbox,
            snapshot.ParcelId,
            snapshot.SenderUserId,
            paidAmount,
            cancellationToken);
        _logger.LogWarning(
            "Late payment succeeded event {PaymentId} for terminal parcel {ParcelId} in status {ParcelStatus}; refund initiated.",
            paymentId, parcelId, snapshot.Status);
        return true;
    }

    private static bool IsTerminal(ParcelStatus status)
        => status is ParcelStatus.CANCELLED
            or ParcelStatus.REJECTED
            or ParcelStatus.EXPIRED
            or ParcelStatus.RETURNED
            or ParcelStatus.DELIVERY_CONFIRMED;

    private async Task TryReserveCargoAfterPaymentAsync(
        ParcelPaymentTransitionSnapshot snapshot,
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        var cargo = await GetCargoAsync(snapshot.ParcelId, cancellationToken);
        var outcome = await _tripClient.ReserveCargoAsync(
            snapshot.TripId,
            snapshot.ParcelId,
            cargo.WeightKg,
            cargo.VolumeM3,
            paymentId,
            cancellationToken);

        outcome ??= await _tripClient.ReserveCargoAsync(
            snapshot.TripId,
            snapshot.ParcelId,
            cargo.WeightKg,
            cancellationToken);

        if (outcome is null)
            return;

        if (outcome.Kind == TripCargoOutcomeKind.Success)
        {
            return;
        }

        var reason = outcome.Kind switch
        {
            TripCargoOutcomeKind.TripNotFound => "TRIP_NOT_FOUND",
            TripCargoOutcomeKind.CapacityExceeded => "TRIP_CARGO_CAPACITY_EXCEEDED",
            _ => "TRIP_SERVICE_UNAVAILABLE",
        };

        _logger.LogError(
            "Payment succeeded for parcel {ParcelId} via payment {PaymentId}, but cargo reservation failed with {Reason}: {Message}.",
            snapshot.ParcelId,
            paymentId,
            reason,
            outcome.ErrorMessage);

        await _parcelRepository.TrySetPendingOperatorActionAsync(
            snapshot.ParcelId,
            reason == "TRIP_CARGO_CAPACITY_EXCEEDED"
                ? PendingActionType.CAPACITY_EXCEEDED
                : PendingActionType.RESERVE_FAILED,
            outcome.ErrorMessage ?? reason,
            null,
            _clock.UtcNow,
            cancellationToken);

        await ParcelOutboxEvents.EnqueueAsync(
            _outbox,
            ParcelOutboxEvents.PendingOperatorAction,
            new
            {
                parcelId = snapshot.ParcelId,
                operatorId = snapshot.OperatorId,
                reason,
                message = outcome.ErrorMessage,
                paymentId,
            },
            cancellationToken);
    }

    private async Task<(decimal WeightKg, decimal VolumeM3)> GetCargoAsync(Guid parcelId, CancellationToken cancellationToken)
    {
        var parcel = await _parcelRepository.GetByIdAsync(parcelId, cancellationToken);
        return (
            parcel?.ActualWeightKg ?? parcel?.EstimatedWeightKg ?? 0m,
            parcel?.ActualVolumeM3 ?? parcel?.EstimatedVolumeM3 ?? 0m);
    }
}
