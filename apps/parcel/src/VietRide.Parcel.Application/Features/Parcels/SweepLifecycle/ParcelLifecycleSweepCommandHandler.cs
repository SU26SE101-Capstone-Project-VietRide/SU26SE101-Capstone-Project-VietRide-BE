using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Parcels.SweepLifecycle;

public sealed class ParcelLifecycleSweepCommandHandler : IRequestHandler<ParcelLifecycleSweepCommand, int>
{
    private static readonly TimeSpan TransferConfirmWindow = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan DeliveryRejectedUndoWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan DeliveryPendingConfirmWindow = TimeSpan.FromDays(7);
    private static readonly TimeSpan PendingPaymentWindow = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan PendingOperatorActionRealertWindow = TimeSpan.FromHours(2);
    private const int MaxBatch = 200;

    private readonly IParcelRepository _parcelRepository;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly ILogger<ParcelLifecycleSweepCommandHandler> _logger;

    public ParcelLifecycleSweepCommandHandler(
        IParcelRepository parcelRepository,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork,
        IClock clock,
        ILogger<ParcelLifecycleSweepCommandHandler> logger)
    {
        _parcelRepository = parcelRepository;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _logger = logger;
    }

    public async Task<int> Handle(ParcelLifecycleSweepCommand request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var processed = 0;

        processed += await SweepAsync(
            () => _parcelRepository.TryBulkEscalatePendingTransfersAsync(now.Subtract(TransferConfirmWindow), now, MaxBatch, cancellationToken),
            ParcelOutboxEvents.TransferEscalated,
            cancellationToken);
        processed += await SweepAsync(
            () => _parcelRepository.TryBulkInitiateReturnForRejectedDeliveriesAsync(now.Subtract(DeliveryRejectedUndoWindow), now, MaxBatch, cancellationToken),
            ParcelOutboxEvents.ReturnInitiated,
            cancellationToken);
        processed += await SweepAsync(
            () => _parcelRepository.TryBulkSetPendingOperatorActionForExpiredConfirmationsAsync(now.Subtract(DeliveryPendingConfirmWindow), now, MaxBatch, cancellationToken),
            ParcelOutboxEvents.PendingOperatorAction,
            cancellationToken);
        processed += await SweepAsync(
            () => _parcelRepository.TryBulkExpireOrphanPendingPaymentsAsync(now.Subtract(PendingPaymentWindow), now, MaxBatch, cancellationToken),
            ParcelOutboxEvents.Rejected,
            cancellationToken);
        processed += await SweepAsync(
            () => _parcelRepository.TryBulkRealertPendingOperatorActionAsync(
                now.Subtract(PendingOperatorActionRealertWindow),
                now.Subtract(PendingOperatorActionRealertWindow),
                now,
                MaxBatch,
                cancellationToken),
            ParcelOutboxEvents.PendingOperatorActionRealert,
            cancellationToken);

        if (processed > 0)
        {
            _logger.LogInformation("Parcel lifecycle sweep processed {ProcessedCount} parcel(s).", processed);
        }

        return processed;
    }

    private async Task<int> SweepAsync(
        Func<Task<IReadOnlyList<ParcelEventSnapshot>>> transition,
        string eventType,
        CancellationToken cancellationToken)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var parcels = await transition();
            foreach (var parcel in parcels)
            {
                await ParcelOutboxEvents.EnqueueAsync(
                    _outbox,
                    eventType,
                    new
                    {
                        parcelId = parcel.ParcelId,
                        parcelCode = parcel.ParcelCode,
                        operatorId = parcel.OperatorId,
                        userId = parcel.SenderUserId,
                        tripId = parcel.TripId,
                        deliveryToken = parcel.DeliveryToken,
                        deliveryTokenExpiresAt = parcel.DeliveryTokenExpiresAt,
                    },
                    cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitAsync(cancellationToken);
            return parcels.Count;
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
