using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Parcels.ConfirmPaymentForParcel;

public sealed class ConfirmPaymentForParcelCommandHandler
    : IRequestHandler<ConfirmPaymentForParcelCommand, bool>
{
    private const string ParcelReferenceType = "PARCEL";
    private const string ParcelAdditionalReferenceType = "PARCEL_ADDITIONAL";

    private readonly IParcelRepository _parcelRepository;
    private readonly IParcelStatsRepository _statsRepository;
    private readonly IClock _clock;
    private readonly ILogger<ConfirmPaymentForParcelCommandHandler> _logger;

    public ConfirmPaymentForParcelCommandHandler(
        IParcelRepository parcelRepository,
        IParcelStatsRepository statsRepository,
        IClock clock,
        ILogger<ConfirmPaymentForParcelCommandHandler> logger)
    {
        _parcelRepository = parcelRepository;
        _statsRepository = statsRepository;
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
                _logger.LogInformation(
                    "Payment succeeded event {PaymentId} ignored for parcel {ParcelId}; deposit already succeeded or parcel is not pending payment.",
                    request.PaymentId, request.ReferenceId);
                return false;
            }
            await _statsRepository.UpsertIncrementAsync(
                snapshot.OperatorId,
                DateOnly.FromDateTime(now.UtcDateTime),
                0, 0, 0, 0, 0, snapshot.DepositAmount, 0,
                cancellationToken);
            return true;
        }

        if (string.Equals(request.ReferenceType, ParcelAdditionalReferenceType, StringComparison.OrdinalIgnoreCase))
        {
            var now = _clock.UtcNow;
            var snapshot = await _parcelRepository.TryMarkAdditionalSucceededAsync(
                request.ReferenceId, request.Amount, request.PaymentId, now, cancellationToken);
            if (snapshot is null)
            {
                _logger.LogInformation(
                    "Payment succeeded event {PaymentId} ignored for parcel {ParcelId}; additional payment already succeeded or not pending additional payment.",
                    request.PaymentId, request.ReferenceId);
                return false;
            }
            await _statsRepository.UpsertIncrementAsync(
                snapshot.OperatorId,
                DateOnly.FromDateTime(now.UtcDateTime),
                0, 0, 0, 0, 0, snapshot.AdditionalAmount, 0,
                cancellationToken);
            return true;
        }

        return false;
    }
}
