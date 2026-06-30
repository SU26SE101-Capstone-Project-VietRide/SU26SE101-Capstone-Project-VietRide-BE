using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Parcels.ExpireParcelAdditionalPayment;

public sealed class ExpireParcelAdditionalPaymentCommandHandler
    : IRequestHandler<ExpireParcelAdditionalPaymentCommand, int>
{
    private readonly IParcelRepository _parcelRepository;
    private readonly IClock _clock;
    private readonly ILogger<ExpireParcelAdditionalPaymentCommandHandler> _logger;

    public ExpireParcelAdditionalPaymentCommandHandler(
        IParcelRepository parcelRepository,
        IClock clock,
        ILogger<ExpireParcelAdditionalPaymentCommandHandler> logger)
    {
        _parcelRepository = parcelRepository;
        _clock = clock;
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
            var ok = await _parcelRepository.TryMarkAdditionalExpiredByDeadlineAsync(
                parcelId, now, cancellationToken);
            if (ok)
                rejectedCount++;
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
