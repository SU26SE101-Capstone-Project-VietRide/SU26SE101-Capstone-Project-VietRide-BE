using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Locations;

internal static class LocationMapper
{
    public static LocationDto ToDto(Location location, Location? parent = null)
        => new(
            location.Id,
            location.Code,
            location.Name,
            location.Type,
            location.ParentLocationId,
            parent?.Code,
            parent?.Name,
            location.IsActive,
            location.SortOrder,
            location.CreatedAt,
            location.UpdatedAt);
}
