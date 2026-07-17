namespace VietRide.Trip.Application.Features.Internal.Stations;

public sealed record InternalStationDto(
    Guid Id,
    string Name,
    string Slug,
    string City,
    string Province,
    decimal? Latitude,
    decimal? Longitude,
    bool SupportsShuttle,
    bool IsActive,
    bool IsMerged,
    Guid CanonicalStationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
