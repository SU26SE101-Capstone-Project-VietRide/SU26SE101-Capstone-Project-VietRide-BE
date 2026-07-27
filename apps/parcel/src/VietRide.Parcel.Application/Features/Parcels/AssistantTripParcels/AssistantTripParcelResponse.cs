namespace VietRide.Parcel.Application.Features.Parcels.AssistantTripParcels;

public sealed record AssistantTripParcelResponse(
    Guid ParcelId,
    string ParcelCode,
    string Status,
    string? RecipientName,
    string? RecipientPhone,
    Guid? DropoffStopId,
    string SizeCategory,
    string EstimatedSizeCategory,
    string? ActualSizeCategory,
    decimal EstimatedWeightKg,
    decimal? ActualWeightKg,
    long BalanceRequiredVnd,
    long BalancePaidVnd,
    DateTimeOffset? FinalPaymentDeadline,
    string? Description,
    string? PhotoUrl);
