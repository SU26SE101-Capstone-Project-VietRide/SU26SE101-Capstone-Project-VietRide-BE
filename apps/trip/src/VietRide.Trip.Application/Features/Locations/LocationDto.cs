namespace VietRide.Trip.Application.Features.Locations;

public sealed record LocationDto(
    Guid Id,
    string Code,
    string Name,
    string Type,
    bool IsActive,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
