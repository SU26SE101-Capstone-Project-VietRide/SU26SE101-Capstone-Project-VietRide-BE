namespace VietRide.Trip.Application.Features.Internal.Trips.ParcelAvailability;

public sealed record ParcelTripDropoffPointDto(
    string Type,
    Guid? StationId,
    Guid? StopId,
    string Name,
    int OrderIndex,
    DateTimeOffset EstimatedArrivalTime);
