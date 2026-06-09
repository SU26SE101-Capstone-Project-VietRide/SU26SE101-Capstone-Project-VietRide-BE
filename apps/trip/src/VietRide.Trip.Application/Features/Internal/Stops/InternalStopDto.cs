namespace VietRide.Trip.Application.Features.Internal.Stops;

public sealed record InternalStopDto(
    Guid Id,
    Guid OperatorId,
    string Name,
    string? Description,
    decimal? Latitude,
    decimal? Longitude,
    string? Address,
    string? GooglePlaceId,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
