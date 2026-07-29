namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record TripRouteSummarySnapshot(
    Guid RouteId,
    string Name,
    string OriginName,
    string DestinationName);
