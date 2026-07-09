using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Domain.Enums;
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

    public ConfirmPaymentForParcelCommandHandler(
        IParcelRepository parcelRepository,
        IIntegrationEventOutbox outbox,
        IParcelStatsRepository statsRepository,
        ITripServiceClient tripClient,
        IClock clock,
        ILogger<ConfirmPaymentForParcelCommandHandler> logger)
    {
        _parcelRepository = parcelRepository;
        _outbox = outbox;
        _statsRepository = statsRepository;
        _tripClient = tripClient;
        _clock = clock;
        _logger = logger;
    }

    public async Task<bool> Handle(ConfirmPaymentForParcelCommand request, CancellationToken cancellationToken)
    {
        if (string.Equals(request.ReferenceType, ParcelReferenceType, StringComparison.OrdinalIgnoreCase))
        {
            var now = _clock.UtcNow;
            var snapshot = await _parcelRepository.TryMarkDepositSucceededAsync(
                request.ReferenceId, request.Amount, now, cancellationToken);
            if (snapshot is null)
            {
                return await HandleLateOrMismatchedSuccessAsync(
                    request.PaymentId,
                    request.ReferenceId,
                    request.Amount,
                    isAdditionalPayment: false,
                    cancellationToken);
            }
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
