namespace VietRide.Parcel.Application.Features.Parcels.AssistantTripParcels;

public sealed record AssistantParcelIdentityHintsResponse(
    string? PhotoUrl,
    string? Description,
    decimal ExpectedWeightKg,
    decimal? ActualWeightKg,
    decimal ExpectedLengthCm,
    decimal ExpectedWidthCm,
    decimal ExpectedHeightCm,
    decimal? ActualLengthCm,
    decimal? ActualWidthCm,
    decimal? ActualHeightCm);
