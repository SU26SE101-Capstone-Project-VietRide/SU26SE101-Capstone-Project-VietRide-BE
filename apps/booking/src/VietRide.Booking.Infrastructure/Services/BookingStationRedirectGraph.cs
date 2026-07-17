using VietRide.Booking.Domain.Entities;

namespace VietRide.Booking.Infrastructure.Services;

internal static class BookingStationRedirectGraph
{
    private const int MaximumRedirectHops = 32;

    public static StationRedirectPath ResolvePath(
        Guid stationId,
        IReadOnlyDictionary<Guid, Guid> redirects)
    {
        var nodes = new List<Guid> { stationId };
        var visited = new HashSet<Guid> { stationId };
        var current = stationId;
        for (var hop = 0; redirects.TryGetValue(current, out var next); hop++)
        {
            if (hop >= MaximumRedirectHops)
                throw new InvalidOperationException("Booking Station redirect chain exceeds 32 hops.");
            if (next == current || !visited.Add(next))
                throw new InvalidOperationException("Booking Station redirect graph contains a cycle.");

            nodes.Add(next);
            current = next;
        }

        return new StationRedirectPath(nodes, current);
    }

    public static IReadOnlyDictionary<Guid, Guid> ToDictionary(
        IEnumerable<BookingStationRedirect> redirects)
        => redirects.ToDictionary(
            redirect => redirect.DuplicateStationId,
            redirect => redirect.CanonicalStationId);
}

internal sealed record StationRedirectPath(IReadOnlyList<Guid> Nodes, Guid TerminalStationId);
