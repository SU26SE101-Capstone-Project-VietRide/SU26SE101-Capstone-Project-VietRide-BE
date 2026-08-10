using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Parcels.ExpireSettlementTimeouts;

public sealed class ExpireParcelSettlementTimeoutsCommandHandler
    : IRequestHandler<ExpireParcelSettlementTimeoutsCommand, int>
{
    private const int MaxBatch = 200;

    private readonly IParcelRepository _parcels;
    private readonly ITripServiceClient _trips;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IParcelStatsRepository _stats;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly ILogger<ExpireParcelSettlementTimeoutsCommandHandler> _logger;

    public ExpireParcelSettlementTimeoutsCommandHandler(
        IParcelRepository parcels,
        ITripServiceClient trips,
        IIntegrationEventOutbox outbox,
        IParcelStatsRepository stats,
        IUnitOfWork unitOfWork,
        IClock clock,
        ILogger<ExpireParcelSettlementTimeoutsCommandHandler> logger)
    {
        _parcels = parcels;
        _trips = trips;
        _outbox = outbox;
        _stats = stats;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _logger = logger;
    }

    public async Task<int> Handle(
        ExpireParcelSettlementTimeoutsCommand request,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var checkInIds = await _parcels.ListCheckInTimedOutIdsAsync(now, MaxBatch, cancellationToken);
        var finalPaymentIds = await _parcels.ListFinalPaymentTimedOutIdsAsync(now, MaxBatch, cancellationToken);
        var processed = 0;

        foreach (var parcelId in checkInIds)
        {
            processed += await RejectAndReleaseAsync(
                parcelId,
                ParcelRejectionReasons.CheckInTimeout,
                isFinalPayment: false,
                now,
                cancellationToken);
        }

        foreach (var parcelId in finalPaymentIds)
        {
            processed += await RejectAndReleaseAsync(
                parcelId,
                ParcelRejectionReasons.FinalPaymentTimeout,
                isFinalPayment: true,
                now,
                cancellationToken);
        }

        return processed;
    }

    private async Task<int> RejectAndReleaseAsync(
        Guid parcelId,
        string reason,
        bool isFinalPayment,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var parcel = await _parcels.GetByIdAsync(parcelId, cancellationToken);
        if (parcel is null)
            return 0;

        ParcelPaymentTransitionSnapshot? snapshot;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            snapshot = isFinalPayment
                ? await _parcels.TryRejectFinalPaymentTimedOutAsync(parcelId, reason, now, cancellationToken)
                : await _parcels.TryRejectCheckInTimedOutAsync(parcelId, reason, now, cancellationToken);
            if (snapshot is null)
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
                return 0;
            }

            var eventId = Guid.NewGuid();
            await _outbox.EnqueueAsync(
                eventId,
                ParcelOutboxEvents.AutoRejected,
                JsonSerializer.Serialize(new
                {
                    eventId,
                    occurredAt = now,
                    parcelId = snapshot.ParcelId,
                    parcelCode = snapshot.ParcelCode,
                    operatorId = snapshot.OperatorId,
                    userId = snapshot.SenderUserId,
                    tripId = snapshot.TripId,
                    reason,
                    forfeitedDepositVnd = parcel.DepositPaidVnd.Amount,
                    refundAmount = 0L,
                }),
                cancellationToken);
            await _stats.UpsertIncrementAsync(
                snapshot.OperatorId,
                VietRide.Shared.Kernel.Time.BusinessTime.ToLocalDate(now),
                0, 0, 0, 1, 0, 0, 0,
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        var release = await _trips.ReleaseCargoAsync(
            parcel.TripId,
            parcel.Id,
            parcel.ActualWeightKg ?? parcel.EstimatedWeightKg,
            parcel.ActualVolumeM3 ?? parcel.EstimatedVolumeM3,
            parcel.Id,
            cancellationToken);
        if (release.Kind != TripCargoOutcomeKind.Success)
        {
            _logger.LogError(
                "Parcel {ParcelId} timed out with {Reason}, but cargo release failed: {Message}",
                parcel.Id,
                reason,
                release.ErrorMessage);
        }

        return 1;
    }
}
