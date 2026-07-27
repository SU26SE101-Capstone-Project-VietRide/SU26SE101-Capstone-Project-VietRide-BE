using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Parcels.ExpireParcelReview;

public sealed class ExpireParcelReviewCommandHandler
    : IRequestHandler<ExpireParcelReviewCommand, int>
{
    private static readonly TimeSpan ReviewTimeout = TimeSpan.FromHours(24);

    private readonly IParcelRepository _parcelRepository;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IParcelStatsRepository _statsRepository;
    private readonly ILogger<ExpireParcelReviewCommandHandler> _logger;

    public ExpireParcelReviewCommandHandler(
        IParcelRepository parcelRepository,
        IClock clock,
        IUnitOfWork unitOfWork,
        IIntegrationEventOutbox outbox,
        IParcelStatsRepository statsRepository,
        ILogger<ExpireParcelReviewCommandHandler> logger)
    {
        _parcelRepository = parcelRepository;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _outbox = outbox;
        _statsRepository = statsRepository;
        _logger = logger;
    }

    public async Task<int> Handle(
        ExpireParcelReviewCommand request,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var cutoff = now - ReviewTimeout;

        var candidateIds = await _parcelRepository.ListReviewTimedOutIdsAsync(
            cutoff, MaxBatch, cancellationToken);

        if (candidateIds.Count == 0)
            return 0;

        if (candidateIds.Count >= MaxBatch)
        {
            _logger.LogWarning(
                "Review timeout scan hit batch cap of {MaxBatch}. "
                + "Some overdue parcels will be processed on the next tick.",
                MaxBatch);
        }

        var rejectedCount = 0;
        foreach (var parcelId in candidateIds)
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                var snapshot = await _parcelRepository.TryAutoRejectReviewAsync(
                    parcelId, ParcelRejectionReasons.ReviewTimeout, now, cancellationToken);
                if (snapshot is null)
                {
                    await _unitOfWork.RollbackAsync(cancellationToken);
                    continue;
                }

                var eventId = Guid.NewGuid();
                await _outbox.EnqueueAsync(
                    eventId,
                    ParcelOutboxEvents.Cancelled,
                    JsonSerializer.Serialize(new
                    {
                        eventId,
                        occurredAt = now,
                        parcelId = snapshot.ParcelId,
                        parcelCode = snapshot.ParcelCode,
                        operatorId = snapshot.OperatorId,
                        userId = snapshot.SenderUserId,
                        tripId = snapshot.TripId,
                        reason = ParcelRejectionReasons.ReviewTimeout,
                        refundAmount = 0L,
                    }),
                    cancellationToken);
                await _statsRepository.UpsertIncrementAsync(
                    snapshot.OperatorId,
                    DateOnly.FromDateTime(now.UtcDateTime),
                    0, 0, 0, 1, 0, 0, 0,
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
                "Expired review for {RejectedCount} parcel(s).",
                rejectedCount);
        }

        return rejectedCount;
    }

    private const int MaxBatch = 200;
}
