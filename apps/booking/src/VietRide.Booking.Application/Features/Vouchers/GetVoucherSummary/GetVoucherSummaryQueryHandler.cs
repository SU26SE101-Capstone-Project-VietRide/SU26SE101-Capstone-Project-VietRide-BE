using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Application.Features.Vouchers.GetVoucherSummary;

public sealed class GetVoucherSummaryQueryHandler(
    IVoucherRepository vouchers,
    IClock clock) : IRequestHandler<GetVoucherSummaryQuery, VoucherSummaryResult>
{
    public Task<VoucherSummaryResult> Handle(
        GetVoucherSummaryQuery request,
        CancellationToken cancellationToken)
        => vouchers.GetSummaryAsync(
            request.OwnerOperatorId,
            request.PlatformOnly,
            clock.UtcNow,
            cancellationToken);
}
