namespace VietRide.Trip.Application.Features.Stops;

public sealed record StopDto(
    Guid Id,
    Guid OperatorId,
    string Name,
    string? Description,
    decimal Latitude,
    decimal Longitude,
    string? Address,
    string? GooglePlaceId,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? LocationId = null);
