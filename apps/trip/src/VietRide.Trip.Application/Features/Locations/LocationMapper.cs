using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Locations;

internal static class LocationMapper
{
    public static LocationDto ToDto(Location location)
        => new(
            location.Id,
            location.Code,
            location.Name,
            location.Type,
            location.IsActive,
            location.SortOrder,
            location.CreatedAt,
            location.UpdatedAt);
}
