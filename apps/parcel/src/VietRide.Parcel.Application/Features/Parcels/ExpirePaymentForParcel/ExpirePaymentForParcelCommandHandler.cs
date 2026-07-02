using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Parcels.ExpirePaymentForParcel;

public sealed class ExpirePaymentForParcelCommandHandler
    : IRequestHandler<ExpirePaymentForParcelCommand, bool>
{
    private const string ParcelReferenceType = "PARCEL";
    private const string ParcelAdditionalReferenceType = "PARCEL_ADDITIONAL";

    private readonly IParcelRepository _parcelRepository;
    private readonly IIdentityServiceClient _identityClient;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IParcelStatsRepository _statsRepository;
    private readonly IClock _clock;
    private readonly ILogger<ExpirePaymentForParcelCommandHandler> _logger;

    public ExpirePaymentForParcelCommandHandler(
        IParcelRepository parcelRepository,
        IIdentityServiceClient identityClient,
        IIntegrationEventOutbox outbox,
        IParcelStatsRepository statsRepository,
        IClock clock,
        ILogger<ExpirePaymentForParcelCommandHandler> logger)
    {
        _parcelRepository = parcelRepository;
        _identityClient = identityClient;
        _outbox = outbox;
        _statsRepository = statsRepository;
        _clock = clock;
        _logger = logger;
    }

    public async Task<bool> Handle(ExpirePaymentForParcelCommand request, CancellationToken cancellationToken)
    {
        if (string.Equals(request.ReferenceType, ParcelReferenceType, StringComparison.OrdinalIgnoreCase))
        {
            var snapshot = await _parcelRepository.TryMarkDepositExpiredAsync(
                request.ReferenceId, _clock.UtcNow, cancellationToken);
            if (snapshot is null)
            {
                _logger.LogInformation(
                    "Payment expired event {PaymentId} ignored for parcel {ParcelId}; deposit already expired or parcel is not pending payment.",
                    request.PaymentId, request.ReferenceId);
                return false;
            }
            return true;
        }

        if (string.Equals(request.ReferenceType, ParcelAdditionalReferenceType, StringComparison.OrdinalIgnoreCase))
        {
            var now = _clock.UtcNow;
            var snapshot = await _parcelRepository.TryMarkAdditionalExpiredAsync(
                request.ReferenceId, now, cancellationToken);
            if (snapshot is null)
            {
                _logger.LogInformation(
                    "Payment expired event {PaymentId} ignored for parcel {ParcelId}; additional payment already expired or not pending additional payment.",
                    request.PaymentId, request.ReferenceId);
                return false;
            }

            var refundAmount = await ParcelRefundAmountCalculator.CalculateRefundAsync(
                _identityClient,
                snapshot.OperatorId,
                snapshot.DepositAmount,
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

            return true;
        }

        return false;
    }
}
