namespace VietRide.Trip.Application.Features.Internal.Stations;

public sealed record InternalStationDto(
    Guid Id,
    string Name,
    string Slug,
    string City,
    string Province,
    decimal? Latitude,
    decimal? Longitude,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
