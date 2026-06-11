namespace VietRide.Trip.Application.Features.Stations;

public sealed record StationSearchResult(
    Guid Id,
    string Name,
    string City,
    string Province,
    decimal? Latitude,
    decimal? Longitude,
    string? AddressStreet,
    bool SupportsShuttle);
