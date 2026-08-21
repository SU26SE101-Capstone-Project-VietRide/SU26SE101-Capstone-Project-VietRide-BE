using VietRide.Parcel.Application.Features.Reliability.ReadModels;

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
    string? PhotoUrl,
    ReliabilityLocationResponse? DropoffLocation = null,
    ReliabilityCustodySummaryResponse? CurrentCustody = null,
    ReliabilityIncidentSummaryResponse? ActiveIncident = null,
    AssistantParcelPaymentStateResponse? PaymentState = null,
    AssistantParcelIdentityHintsResponse? IdentityCheckHints = null,
    IReadOnlyList<string>? AvailableActions = null);
