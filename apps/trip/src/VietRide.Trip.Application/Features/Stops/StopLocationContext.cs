using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Stops;

internal sealed record StopLocationContext(string City, string Ward);

internal static class StopLocationContextResolver
{
    public static StopLocationContext From(Location leaf, Location parent)
        => new(parent.Name, leaf.Name);

    public static IReadOnlyDictionary<Guid, StopLocationContext> Resolve(
        ILocationRepository? locations,
        IReadOnlyCollection<Stop> stops)
    {
        if (locations is null)
        {
            return new Dictionary<Guid, StopLocationContext>();
        }

        var locationIds = stops
            .Where(stop => stop.LocationId.HasValue)
            .Select(stop => stop.LocationId!.Value)
            .Distinct()
            .ToArray();
        if (locationIds.Length == 0)
        {
            return new Dictionary<Guid, StopLocationContext>();
        }

        var leaves = locations.QueryNoTracking()
            .Where(location => locationIds.Contains(location.Id) && location.ParentLocationId.HasValue)
            .ToArray();
        var parentIds = leaves
            .Select(location => location.ParentLocationId!.Value)
            .Distinct()
            .ToArray();
        var parents = locations.QueryNoTracking()
            .Where(location => parentIds.Contains(location.Id))
            .ToDictionary(location => location.Id);

        return leaves
            .Where(leaf => parents.ContainsKey(leaf.ParentLocationId!.Value))
            .ToDictionary(
                leaf => leaf.Id,
                leaf => From(leaf, parents[leaf.ParentLocationId!.Value]));
    }
}
