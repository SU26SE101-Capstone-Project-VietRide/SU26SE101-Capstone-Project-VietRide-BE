namespace VietRide.Parcel.Infrastructure.Http;

public sealed record TripAvailabilityDropoffPointDto(
    string Type,
    Guid? StationId,
    Guid? StopId,
    string Name,
    int OrderIndex,
    DateTimeOffset EstimatedArrivalTime);
