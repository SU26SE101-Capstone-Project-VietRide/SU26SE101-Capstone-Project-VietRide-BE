namespace VietRide.Booking.Application.Features.Vouchers.AvailableVouchers;

public sealed record AvailableVoucherItem(
    Guid Id,
    string Code,
    string Name,
    string Type,
    long Value,
    long MinOrderAmount,
    long? MaxDiscountAmount,
    long DiscountAmount,
    IReadOnlyList<string> ApplicableServices,
    IReadOnlyList<string> ApplicablePaymentMethods,
    DateTimeOffset ValidUntil);
