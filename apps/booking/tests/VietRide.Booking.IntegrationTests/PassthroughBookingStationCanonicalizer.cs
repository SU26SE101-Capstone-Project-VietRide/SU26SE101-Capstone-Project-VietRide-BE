using VietRide.Booking.Application.Abstractions.Services;

namespace VietRide.Booking.IntegrationTests;

internal sealed class PassthroughBookingStationCanonicalizer : IBookingStationCanonicalizer
{
    public static readonly PassthroughBookingStationCanonicalizer Instance = new();

    private PassthroughBookingStationCanonicalizer()
    {
    }

    public Task<StationCanonicalizationResult> LockAndResolveAsync(
        IReadOnlyCollection<Guid> stationIds,
        CancellationToken cancellationToken = default)
    {
        var lockedIds = stationIds.Where(id => id != Guid.Empty).Distinct().ToHashSet();
        var canonicalIds = lockedIds.ToDictionary(id => id, id => id);
        return Task.FromResult(new StationCanonicalizationResult(canonicalIds, lockedIds));
    }
}
