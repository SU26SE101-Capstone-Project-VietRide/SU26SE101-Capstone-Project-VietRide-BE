namespace VietRide.Parcel.Application.Features.Parcels.OperatorList;

public sealed record OperatorParcelRouteResponse(
    Guid RouteId,
    string RouteName,
    string OriginStationName,
    string DestinationStationName);
