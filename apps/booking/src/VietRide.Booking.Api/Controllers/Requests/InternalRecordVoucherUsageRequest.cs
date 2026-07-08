namespace VietRide.Booking.Api.Controllers.Requests;

public sealed record InternalRecordVoucherUsageRequest(
    Guid VoucherId,
    Guid UserId,
    string ReferenceType,
    Guid ReferenceId,
    long DiscountAmount);
