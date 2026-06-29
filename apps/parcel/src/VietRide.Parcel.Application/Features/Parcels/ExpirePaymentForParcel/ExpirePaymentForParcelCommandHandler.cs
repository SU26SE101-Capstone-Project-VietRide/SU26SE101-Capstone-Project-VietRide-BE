using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Parcels.ExpirePaymentForParcel;

public sealed class ExpirePaymentForParcelCommandHandler
    : IRequestHandler<ExpirePaymentForParcelCommand, bool>
{
    private const string ParcelReferenceType = "PARCEL";
    private const string ParcelAdditionalReferenceType = "PARCEL_ADDITIONAL";

    private readonly IParcelRepository _parcelRepository;
    private readonly IClock _clock;
    private readonly ILogger<ExpirePaymentForParcelCommandHandler> _logger;

    public ExpirePaymentForParcelCommandHandler(
        IParcelRepository parcelRepository,
        IClock clock,
        ILogger<ExpirePaymentForParcelCommandHandler> logger)
    {
        _parcelRepository = parcelRepository;
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
            var snapshot = await _parcelRepository.TryMarkAdditionalExpiredAsync(
                request.ReferenceId, _clock.UtcNow, cancellationToken);
            if (snapshot is null)
            {
                _logger.LogInformation(
                    "Payment expired event {PaymentId} ignored for parcel {ParcelId}; additional payment already expired or not pending additional payment.",
                    request.PaymentId, request.ReferenceId);
                return false;
            }
            return true;
        }

        return false;
    }
}
