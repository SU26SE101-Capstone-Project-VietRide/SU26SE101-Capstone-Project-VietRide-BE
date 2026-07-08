namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record AvailableVoucherDto(
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
