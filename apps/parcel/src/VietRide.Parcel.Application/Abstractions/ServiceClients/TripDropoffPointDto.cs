namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record TripDropoffPointDto(
    string Type,
    Guid? StationId,
    Guid? StopId,
    string Name,
    int OrderIndex,
    DateTimeOffset EstimatedArrivalTime);
