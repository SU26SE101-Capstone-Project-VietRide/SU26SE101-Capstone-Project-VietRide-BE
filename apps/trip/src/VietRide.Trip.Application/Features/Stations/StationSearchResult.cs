namespace VietRide.Trip.Application.Features.Stations;

public sealed record StationSearchResult(
    Guid Id,
    string Name,
    Guid? LocationId,
    string City,
    string? Ward,
    decimal? Latitude,
    decimal? Longitude,
    string? AddressStreet,
    bool SupportsShuttle);
