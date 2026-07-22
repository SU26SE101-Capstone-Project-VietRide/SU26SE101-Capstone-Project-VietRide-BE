using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Parcels.ExpireParcelAdditionalPayment;

public sealed class ExpireParcelAdditionalPaymentCommandHandler
    : IRequestHandler<ExpireParcelAdditionalPaymentCommand, int>
{
    private readonly IParcelRepository _parcelRepository;
    private readonly IIdentityServiceClient _identityClient;
    private readonly ITripServiceClient? _tripClient;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IParcelStatsRepository _statsRepository;
    private readonly ILogger<ExpireParcelAdditionalPaymentCommandHandler> _logger;

    public ExpireParcelAdditionalPaymentCommandHandler(
        IParcelRepository parcelRepository,
        IIdentityServiceClient identityClient,
        ITripServiceClient tripClient,
        IClock clock,
        IUnitOfWork unitOfWork,
        IIntegrationEventOutbox outbox,
        ILogger<ExpireParcelAdditionalPaymentCommandHandler> logger,
        IParcelStatsRepository statsRepository)
    {
        _parcelRepository = parcelRepository;
        _identityClient = identityClient;
        _tripClient = tripClient;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _outbox = outbox;
        _statsRepository = statsRepository;
        _logger = logger;
    }

    public ExpireParcelAdditionalPaymentCommandHandler(
        IParcelRepository parcelRepository,
        IIdentityServiceClient identityClient,
        IClock clock,
        IUnitOfWork unitOfWork,
        IIntegrationEventOutbox outbox,
        ILogger<ExpireParcelAdditionalPaymentCommandHandler> logger,
        IParcelStatsRepository statsRepository)
        : this(
            parcelRepository,
            identityClient,
            tripClient: null!,
            clock,
            unitOfWork,
            outbox,
            logger,
            statsRepository)
    {
    }

    public async Task<int> Handle(
        ExpireParcelAdditionalPaymentCommand request,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var candidateIds = await _parcelRepository.ListAdditionalPaymentTimedOutIdsAsync(
            now, MaxBatch, cancellationToken);

        if (candidateIds.Count == 0)
            return 0;

        if (candidateIds.Count >= MaxBatch)
        {
            _logger.LogWarning(
                "Additional-payment timeout scan hit batch cap of {MaxBatch}. "
                + "Some overdue parcels will be processed on the next tick.",
                MaxBatch);
        }

        var rejectedCount = 0;
        foreach (var parcelId in candidateIds)
        {
            var parcel = _tripClient is null
                ? null
                : await _parcelRepository.GetByIdAsync(parcelId, cancellationToken);
            if (_tripClient is not null && parcel is null)
            {
                continue;
            }

            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                var snapshot = await _parcelRepository.TryMarkAdditionalExpiredByDeadlineAsync(
                    parcelId, now, cancellationToken);
                if (snapshot is null)
                {
                    await _unitOfWork.RollbackAsync(cancellationToken);
                    continue;
                }

                var refundAmount = await ParcelRefundAmountCalculator.CalculateRefundAsync(
                    _identityClient,
                    snapshot.OperatorId,
                    snapshot.DepositAmount,
                    cancellationToken);
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
                        refundAmount,
                    }),
                    cancellationToken);
                await ParcelOutboxEvents.EnqueueRefundAsync(
                    _outbox,
                    snapshot.ParcelId,
                    snapshot.SenderUserId,
                    refundAmount,
                    cancellationToken);
                await _statsRepository.UpsertIncrementAsync(
                    snapshot.OperatorId,
                    DateOnly.FromDateTime(now.UtcDateTime),
                    0, 0, 0, 1, 0, 0, refundAmount,
                    cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitAsync(cancellationToken);

                var releaseOutcome = _tripClient is null || parcel is null
                    ? new TripCargoOutcome(TripCargoOutcomeKind.Success, null)
                    : await _tripClient.ReleaseCargoAsync(
                        parcel.TripId,
                        parcel.Id,
                        parcel.ActualWeightKg ?? parcel.EstimatedWeightKg,
                        parcel.ActualVolumeM3 ?? parcel.EstimatedVolumeM3,
                        cancellationToken);
                if (releaseOutcome.Kind != TripCargoOutcomeKind.Success)
                {
                    _logger.LogWarning(
                        "Failed to release cargo for expired additional payment parcel {ParcelId}: {Reason}",
                        parcelId,
                        releaseOutcome.ErrorMessage);
                }

                rejectedCount++;
            }
            catch
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
                throw;
            }
        }

        if (rejectedCount > 0)
        {
            _logger.LogInformation(
                "Expired additional payment for {RejectedCount} parcel(s).",
                rejectedCount);
        }

        return rejectedCount;
    }

    private const int MaxBatch = 200;
}
