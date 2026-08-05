namespace VietRide.Trip.Application.Features.Stations;

public sealed record StationDto(
    Guid Id, string Name, string Slug, string? AddressStreet, Guid? LocationId,
    string City, string? Ward, decimal? Latitude, decimal? Longitude,
    string? ContactPhone, string? ContactEmail, string? OperatingHours,
    string? Facilities, bool SupportsShuttle, bool IsActive,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
