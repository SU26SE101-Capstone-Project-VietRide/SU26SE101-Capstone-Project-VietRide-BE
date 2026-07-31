namespace VietRide.Parcel.Application.Features.ParcelRouteFares.Batch;

public sealed record BatchParcelRouteFareResponse(
    Guid RouteId,
    IReadOnlyList<BatchParcelRouteFareItemResponse> Items);
