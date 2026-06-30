using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Parcels.ExpireParcelReview;

public sealed class ExpireParcelReviewCommandHandler
    : IRequestHandler<ExpireParcelReviewCommand, int>
{
    private static readonly TimeSpan ReviewTimeout = TimeSpan.FromHours(24);

    private readonly IParcelRepository _parcelRepository;
    private readonly IClock _clock;
    private readonly ILogger<ExpireParcelReviewCommandHandler> _logger;

    public ExpireParcelReviewCommandHandler(
        IParcelRepository parcelRepository,
        IClock clock,
        ILogger<ExpireParcelReviewCommandHandler> logger)
    {
        _parcelRepository = parcelRepository;
        _clock = clock;
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
            var ok = await _parcelRepository.TryAutoRejectReviewAsync(
                parcelId, ParcelRejectionReasons.ReviewTimeout, now, cancellationToken);
            if (ok)
                rejectedCount++;
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
