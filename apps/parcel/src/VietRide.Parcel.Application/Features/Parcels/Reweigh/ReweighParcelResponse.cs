using System.Text.Json.Serialization;

namespace VietRide.Parcel.Application.Features.Parcels.Reweigh;

public sealed record ReweighParcelResponse(
    Guid ParcelId,
    string ParcelCode,
    string Status,
    string ActualSizeCategory,
    decimal ActualChargeableWeightKg,
    long FinalGrossPriceVnd,
    long DiscountAmountVnd,
    long FinalTotalPriceVnd,
    long DepositPaidVnd,
    long BalanceRequiredVnd,
    long RefundDueVnd,
    DateTimeOffset? FinalPaymentDeadline)
{
    [JsonIgnore]
    public long TotalPriceVnd => FinalTotalPriceVnd;

    [JsonIgnore]
    public long AdditionalAmount => BalanceRequiredVnd;

    [JsonIgnore]
    public long RefundAmount => RefundDueVnd;

    [JsonIgnore]
    public string? PaymentRedirectUrl => null;
}
