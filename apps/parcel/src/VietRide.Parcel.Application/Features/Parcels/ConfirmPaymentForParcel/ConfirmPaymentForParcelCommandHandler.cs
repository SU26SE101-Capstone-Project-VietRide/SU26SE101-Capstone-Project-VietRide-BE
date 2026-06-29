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
    private readonly IClock _clock;
    private readonly ILogger<ConfirmPaymentForParcelCommandHandler> _logger;

    public ConfirmPaymentForParcelCommandHandler(
        IParcelRepository parcelRepository,
        IClock clock,
        ILogger<ConfirmPaymentForParcelCommandHandler> logger)
    {
        _parcelRepository = parcelRepository;
        _clock = clock;
        _logger = logger;
    }

    public async Task<bool> Handle(ConfirmPaymentForParcelCommand request, CancellationToken cancellationToken)
    {
        if (string.Equals(request.ReferenceType, ParcelReferenceType, StringComparison.OrdinalIgnoreCase))
        {
            var snapshot = await _parcelRepository.TryMarkDepositSucceededAsync(
                request.ReferenceId, request.Amount, _clock.UtcNow, cancellationToken);
            if (snapshot is null)
            {
                _logger.LogInformation(
                    "Payment succeeded event {PaymentId} ignored for parcel {ParcelId}; deposit already succeeded or parcel is not pending payment.",
                    request.PaymentId, request.ReferenceId);
                return false;
            }
            return true;
        }

        if (string.Equals(request.ReferenceType, ParcelAdditionalReferenceType, StringComparison.OrdinalIgnoreCase))
        {
            var snapshot = await _parcelRepository.TryMarkAdditionalSucceededAsync(
                request.ReferenceId, request.Amount, request.PaymentId, _clock.UtcNow, cancellationToken);
            if (snapshot is null)
            {
                _logger.LogInformation(
                    "Payment succeeded event {PaymentId} ignored for parcel {ParcelId}; additional payment already succeeded or not pending additional payment.",
                    request.PaymentId, request.ReferenceId);
                return false;
            }
            return true;
        }

        return false;
    }
}
