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
    int SettlementPolicyVersion,
    ParcelCompensationPolicySnapshotResponse? CompensationPolicy = null)
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

public sealed record ParcelCompensationPolicySnapshotResponse(
    int Version,
    int CompensationRatePercent,
    long MaxCompensationVnd,
    int NoProofFallbackMultiplier,
    int ClaimWindowDays,
    int SearchSlaHours,
    int DecisionSlaBusinessDays,
    int PayoutSlaBusinessDays);
