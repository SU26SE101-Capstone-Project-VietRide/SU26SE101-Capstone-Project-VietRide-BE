namespace VietRide.Parcel.Application.Features.Parcels.AssistantTripParcels;

public sealed record AssistantTripManifestSummaryResponse(
    int Total,
    int CheckedIn,
    int Loaded,
    int ExpectedAtCurrentStop,
    int Unloaded,
    int ExceptionCount,
    int UnresolvedCount);
