using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Parcels.FailPaymentForParcel;

public sealed class FailPaymentForParcelCommandHandler
    : IRequestHandler<FailPaymentForParcelCommand, bool>
{
    private const string ParcelReferenceType = "PARCEL";
    private const string ParcelAdditionalReferenceType = "PARCEL_ADDITIONAL";

    private readonly IParcelRepository _parcelRepository;
    private readonly IClock _clock;
    private readonly ILogger<FailPaymentForParcelCommandHandler> _logger;

    public FailPaymentForParcelCommandHandler(
        IParcelRepository parcelRepository,
        IClock clock,
        ILogger<FailPaymentForParcelCommandHandler> logger)
    {
        _parcelRepository = parcelRepository;
        _clock = clock;
        _logger = logger;
    }

    public async Task<bool> Handle(FailPaymentForParcelCommand request, CancellationToken cancellationToken)
    {
        if (string.Equals(request.ReferenceType, ParcelReferenceType, StringComparison.OrdinalIgnoreCase))
        {
            var snapshot = await _parcelRepository.TryMarkDepositFailedAsync(
                request.ReferenceId, _clock.UtcNow, cancellationToken);
            if (snapshot is null)
            {
                _logger.LogInformation(
                    "Payment failed event {PaymentId} ignored for parcel {ParcelId}; deposit already failed or parcel is not pending payment.",
                    request.PaymentId, request.ReferenceId);
                return false;
            }
            return true;
        }

        if (string.Equals(request.ReferenceType, ParcelAdditionalReferenceType, StringComparison.OrdinalIgnoreCase))
        {
            var snapshot = await _parcelRepository.TryMarkAdditionalFailedAsync(
                request.ReferenceId, _clock.UtcNow, cancellationToken);
            if (snapshot is null)
            {
                _logger.LogInformation(
                    "Payment failed event {PaymentId} ignored for parcel {ParcelId}; additional payment already failed or not pending additional payment.",
                    request.PaymentId, request.ReferenceId);
                return false;
            }
            return true;
        }

        return false;
    }
}
