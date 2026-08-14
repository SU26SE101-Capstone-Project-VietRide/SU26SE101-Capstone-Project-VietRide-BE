using MediatR;

namespace VietRide.Booking.Application.Features.Vouchers.GetVoucherSummary;

public sealed record GetVoucherSummaryQuery(
    Guid? OwnerOperatorId,
    bool PlatformOnly) : IRequest<VoucherSummaryResult>;
