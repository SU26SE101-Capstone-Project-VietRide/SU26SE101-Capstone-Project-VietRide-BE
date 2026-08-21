namespace VietRide.Parcel.Application.Features.Parcels.AssistantTripParcels;

public sealed record AssistantTripParcelManifestResponse(
    AssistantTripManifestContextResponse TripContext,
    AssistantTripManifestSummaryResponse Summary,
    IReadOnlyList<AssistantTripParcelResponse> Items,
    AssistantTripManifestPaginationResponse Pagination);
