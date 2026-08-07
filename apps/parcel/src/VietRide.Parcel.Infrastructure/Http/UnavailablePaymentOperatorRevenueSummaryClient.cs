using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels.Reports;

namespace VietRide.Parcel.Infrastructure.Http;

internal sealed class UnavailablePaymentOperatorRevenueSummaryClient
    : IPaymentOperatorRevenueSummaryClient
{
    public Task<PaymentOperatorRevenueSummaryDto> GetAsync(
        Guid operatorId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
        => throw new ParcelDependencyUnavailableException(
            "UPSTREAM_UNAVAILABLE",
            "Payment revenue summary is unavailable while the Payment dev stub is enabled.");
}
