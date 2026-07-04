namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record IdentityOperatorInfo(
    Guid Id,
    string Name,
    ParcelNoShowPolicy ParcelNoShowPolicy);

public sealed record ParcelNoShowPolicy(
    decimal NoShowFeePercent,
    int AdditionalPaymentTimeoutMinutes)
{
    public static ParcelNoShowPolicy Default { get; } = new(0, 30);
}
