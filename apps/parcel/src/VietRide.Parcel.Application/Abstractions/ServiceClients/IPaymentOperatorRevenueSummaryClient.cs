using VietRide.Parcel.Application.Features.Parcels.Reports;

namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public interface IPaymentOperatorRevenueSummaryClient
{
    Task<PaymentOperatorRevenueSummaryDto> GetAsync(
        Guid operatorId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);
}
