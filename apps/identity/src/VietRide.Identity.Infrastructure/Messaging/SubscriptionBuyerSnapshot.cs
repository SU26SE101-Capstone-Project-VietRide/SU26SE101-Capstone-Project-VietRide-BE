namespace VietRide.Identity.Infrastructure.Messaging;

public sealed class SubscriptionBuyerSnapshot
{
    public string Name { get; init; } = string.Empty;
    public string BusinessRegistrationNumber { get; init; } = string.Empty;
    public string TaxCode { get; init; } = string.Empty;
    public string ContactEmail { get; init; } = string.Empty;
    public string ContactPhone { get; init; } = string.Empty;
    public string? AddressStreet { get; init; }
    public string? AddressWard { get; init; }
    public string? AddressDistrict { get; init; }
    public string? AddressProvince { get; init; }
}
