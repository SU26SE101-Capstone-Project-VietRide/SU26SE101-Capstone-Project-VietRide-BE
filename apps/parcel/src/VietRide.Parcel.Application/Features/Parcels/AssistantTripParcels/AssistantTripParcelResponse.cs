namespace VietRide.Parcel.Application.Features.Parcels.AssistantTripParcels;

public sealed record AssistantTripParcelResponse(
    Guid ParcelId,
    string ParcelCode,
    string Status,
    string? RecipientName,
    string? RecipientPhone,
    Guid? DropoffStopId,
    string SizeCategory,
    decimal EstimatedWeightKg,
    string? Description,
    string? PhotoUrl);
