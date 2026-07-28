using System.Text.Json.Serialization;

namespace VietRide.Parcel.Application.Features.Parcels.Review;

public sealed record ReviewParcelResponse(
    Guid ParcelId,
    string ParcelCode,
    string Status,
    long? DepositRequiredVnd)
{
    [JsonIgnore]
    public long? DepositAmount => DepositRequiredVnd;

    [JsonIgnore]
    public string? PaymentRedirectUrl => null;
}
