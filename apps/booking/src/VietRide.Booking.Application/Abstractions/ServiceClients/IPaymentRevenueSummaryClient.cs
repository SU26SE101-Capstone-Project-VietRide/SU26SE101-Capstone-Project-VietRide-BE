using VietRide.Booking.Application.Features.Admin.Dashboard;

namespace VietRide.Booking.Application.Abstractions.ServiceClients;

public interface IPaymentRevenueSummaryClient
{
    Task<PaymentRevenueSummaryDto> GetAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);
}
