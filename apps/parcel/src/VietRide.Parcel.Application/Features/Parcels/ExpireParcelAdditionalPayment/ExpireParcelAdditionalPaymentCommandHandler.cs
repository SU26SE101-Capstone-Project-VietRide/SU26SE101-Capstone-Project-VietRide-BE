using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Parcels.ExpireParcelAdditionalPayment;

public sealed class ExpireParcelAdditionalPaymentCommandHandler
    : IRequestHandler<ExpireParcelAdditionalPaymentCommand, int>
{
    private readonly IParcelRepository _parcelRepository;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IParcelStatsRepository _statsRepository;
    private readonly ILogger<ExpireParcelAdditionalPaymentCommandHandler> _logger;

    public ExpireParcelAdditionalPaymentCommandHandler(
        IParcelRepository parcelRepository,
        IClock clock,
        IUnitOfWork unitOfWork,
        IIntegrationEventOutbox outbox,
        ILogger<ExpireParcelAdditionalPaymentCommandHandler> logger,
        IParcelStatsRepository statsRepository)
    {
        _parcelRepository = parcelRepository;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _outbox = outbox;
        _statsRepository = statsRepository;
        _logger = logger;
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

                var refundAmount = snapshot.DepositAmount;
                await ParcelOutboxEvents.EnqueueAsync(
                    _outbox,
                    ParcelOutboxEvents.AutoRejected,
                    new { parcelId = snapshot.ParcelId, refundAmount },
                    cancellationToken);
                await ParcelOutboxEvents.EnqueueAsync(
                    _outbox,
                    ParcelOutboxEvents.RefundInitiated,
                    new { parcelId = snapshot.ParcelId, refundAmount },
                    cancellationToken);
                await _statsRepository.UpsertIncrementAsync(
                    snapshot.OperatorId,
                    DateOnly.FromDateTime(now.UtcDateTime),
                    0, 0, 0, 1, 0, 0, refundAmount,
                    cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitAsync(cancellationToken);
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
