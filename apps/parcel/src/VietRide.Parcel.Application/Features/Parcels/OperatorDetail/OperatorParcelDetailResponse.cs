using VietRide.Parcel.Application.Features.Parcels.OperatorList;

namespace VietRide.Parcel.Application.Features.Parcels.OperatorDetail;

public sealed record OperatorParcelDetailResponse : OperatorParcelListItemResponse
{
    public OperatorParcelDetailResponse(OperatorParcelListItemResponse projection)
        : base(projection)
    {
    }

    public Guid OperatorId { get; init; }
    public Guid? RecipientUserId { get; init; }
    public Guid? DropoffStopId { get; init; }
    public string? SenderEmail { get; init; }
    public string? RecipientEmail { get; init; }
    public IReadOnlyCollection<string>? CheckInPhotoUrls { get; init; }
    public IReadOnlyCollection<string>? DeliveryPhotoUrls { get; init; }
    public string DeliveryMethod { get; init; } = null!;
    public long DepositAmount { get; init; }
    public long OriginalDepositAmount { get; init; }
    public long DiscountAmount { get; init; }
    public string? VoucherCode { get; init; }
    public Guid? VoucherUsageId { get; init; }
    public long AdditionalAmount { get; init; }
    public decimal EstimatedLengthCm { get; init; }
    public decimal EstimatedWidthCm { get; init; }
    public decimal EstimatedHeightCm { get; init; }
    public decimal EstimatedDimWeightKg { get; init; }
    public decimal? ActualLengthCm { get; init; }
    public decimal? ActualWidthCm { get; init; }
    public decimal? ActualHeightCm { get; init; }
    public decimal? ActualDimWeightKg { get; init; }
    public long EstimatedGrossPriceVnd { get; init; }
    public long FinalGrossPriceVnd { get; init; }
    public decimal DepositPercent { get; init; }
    public Guid? DepositPaymentId { get; init; }
    public Guid? BalancePaymentId { get; init; }
    public DateTimeOffset? CheckedInAt { get; init; }
    public Guid? CheckedInByUserId { get; init; }
    public DateTimeOffset? ReweighedAt { get; init; }
    public Guid? ReweighedByUserId { get; init; }
    public long PricePerKgVnd { get; init; }
    public long MinimumPriceVnd { get; init; }
    public decimal DimWeightFactor { get; init; }
    public int SettlementPolicyVersion { get; init; }
    public DateTimeOffset? LoadedAt { get; init; }
    public Guid? LoadedByUserId { get; init; }
    public DateTimeOffset? UnloadedAt { get; init; }
    public DateTimeOffset? DeliveredPendingConfirmAt { get; init; }
    public DateTimeOffset? ConfirmedAt { get; init; }
    public Guid? ConfirmedByUserId { get; init; }
    public DateTimeOffset? RejectedAt { get; init; }
    public string? PendingActionResumeStatus { get; init; }
    public string? RejectionReason { get; init; }
    public string? CancellationReason { get; init; }
    public string? ReviewDecision { get; init; }
    public DateTimeOffset? ReviewedAt { get; init; }
    public Guid? ReviewedByUserId { get; init; }
    public Guid? TransferTargetTripId { get; init; }
    public DateTimeOffset? TransferRequestedAt { get; init; }
    public DateTimeOffset? TransferConfirmedAt { get; init; }
    public Guid? TransferConfirmedByUserId { get; init; }
    public string? ReturnReason { get; init; }
    public DateTimeOffset? ReturnedAt { get; init; }
    public Guid? ReturnedByUserId { get; init; }
    public IReadOnlyList<OperatorParcelStatusHistoryItemResponse> StatusHistory { get; init; } = [];
}
