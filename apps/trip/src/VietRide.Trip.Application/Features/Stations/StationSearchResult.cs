namespace VietRide.Trip.Application.Features.Stations;

public sealed record StationSearchResult(
    Guid Id,
    string Name,
    Guid? LocationId,
    string City,
    string Province,
    decimal? Latitude,
    decimal? Longitude,
    string? AddressStreet,
    bool SupportsShuttle);
