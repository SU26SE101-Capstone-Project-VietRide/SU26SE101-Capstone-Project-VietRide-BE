namespace VietRide.Parcel.Application.Features.Parcels.AssistantTripParcels;

public sealed record AssistantTripManifestPaginationResponse(
    int Page,
    int PageSize,
    long TotalItems,
    int TotalPages,
    bool HasNextPage,
    bool HasPreviousPage);
