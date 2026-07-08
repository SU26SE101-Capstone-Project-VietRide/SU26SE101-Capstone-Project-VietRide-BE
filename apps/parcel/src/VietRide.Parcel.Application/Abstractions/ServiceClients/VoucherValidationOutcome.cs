namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public enum VoucherValidationOutcomeKind
{
    Success,
    Invalid,
    TransportError,
}

public sealed record VoucherValidationOutcome(
    VoucherValidationOutcomeKind Kind,
    Guid? VoucherId,
    long DiscountAmount,
    string? ErrorMessage);
