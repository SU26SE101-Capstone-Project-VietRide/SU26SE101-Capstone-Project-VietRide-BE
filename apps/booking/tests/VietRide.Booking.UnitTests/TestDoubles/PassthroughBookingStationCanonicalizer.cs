using VietRide.Booking.Application.Abstractions.Services;

namespace VietRide.Booking.UnitTests.TestDoubles;

internal sealed class PassthroughBookingStationCanonicalizer : IBookingStationCanonicalizer
{
    public static PassthroughBookingStationCanonicalizer Instance { get; } = new();

    public Task<StationCanonicalizationResult> LockAndResolveAsync(
        IReadOnlyCollection<Guid> stationIds,
        CancellationToken cancellationToken = default)
    {
        var distinctIds = stationIds.Distinct().ToArray();
        return Task.FromResult(new StationCanonicalizationResult(
            distinctIds.ToDictionary(stationId => stationId, stationId => stationId),
            distinctIds.ToHashSet()));
    }
}
