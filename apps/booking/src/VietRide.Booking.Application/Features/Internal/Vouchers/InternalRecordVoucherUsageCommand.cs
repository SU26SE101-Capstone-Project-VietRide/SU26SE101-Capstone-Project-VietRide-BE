using MediatR;

namespace VietRide.Booking.Application.Features.Internal.Vouchers;

public sealed record InternalRecordVoucherUsageCommand(
    Guid VoucherId,
    Guid UserId,
    string ReferenceType,
    Guid ReferenceId,
    long DiscountAmount) : IRequest<InternalRecordVoucherUsageResult>;

public sealed record InternalRecordVoucherUsageResult(Guid UsageId);
