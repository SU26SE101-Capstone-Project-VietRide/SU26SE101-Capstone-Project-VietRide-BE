namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public enum VoucherUsageOutcomeKind
{
    Success,
    Invalid,
    TransportError,
}

public sealed record VoucherUsageOutcome(
    VoucherUsageOutcomeKind Kind,
    Guid? UsageId,
    string? ErrorMessage);
