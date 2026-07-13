namespace VietRide.Trip.Application.Features.Internal.Trips.GetTripSnapshot;

public sealed record InternalTripStationSnapshotDto(
    Guid Id,
    string Name,
    bool SupportsShuttle = false,
    decimal? Latitude = null,
    decimal? Longitude = null,
    bool IsActive = true);
