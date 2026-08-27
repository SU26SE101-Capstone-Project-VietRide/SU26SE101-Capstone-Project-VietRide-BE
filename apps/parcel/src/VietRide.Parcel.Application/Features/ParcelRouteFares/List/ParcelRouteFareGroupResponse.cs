namespace VietRide.Parcel.Application.Features.ParcelRouteFares.List;

public sealed record ParcelRouteFareGroupResponse(
    Guid RouteId,
    IReadOnlyList<ParcelRouteFareListItemResponse> Fares);
