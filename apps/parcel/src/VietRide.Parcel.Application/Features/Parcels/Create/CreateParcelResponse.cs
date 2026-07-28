using System.Text.Json.Serialization;

namespace VietRide.Parcel.Application.Features.Parcels.Create;

public sealed record CreateParcelResponse(
    Guid ParcelId,
    string ParcelCode,
    string Status,
    string EstimatedSizeCategory,
    long EstimatedGrossPriceVnd,
    long DiscountAmountVnd,
    long EstimatedTotalPriceVnd,
    decimal DepositPercent,
    long DepositRequiredVnd,
    long DepositPaidVnd,
    string? VoucherCode,
    int SettlementPolicyVersion)
{
    [JsonIgnore]
    public long TotalAmount => DepositRequiredVnd;

    [JsonIgnore]
    public long OriginalDepositAmount => DepositRequiredVnd;

    [JsonIgnore]
    public long DiscountAmount => DiscountAmountVnd;

    [JsonIgnore]
    public string? PaymentRedirectUrl => null;
}
