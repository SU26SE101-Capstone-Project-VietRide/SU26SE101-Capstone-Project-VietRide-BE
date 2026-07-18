using VietRide.Booking.Application.Abstractions.Services;

namespace VietRide.Booking.UnitTests.TestDoubles;

internal sealed class MappingBookingStationCanonicalizer : IBookingStationCanonicalizer
{
    private readonly IReadOnlyDictionary<Guid, Guid> _mapping;

    public MappingBookingStationCanonicalizer(IReadOnlyDictionary<Guid, Guid> mapping)
        => _mapping = mapping;

    public List<IReadOnlyCollection<Guid>> LockRequests { get; } = [];

    public Task<StationCanonicalizationResult> LockAndResolveAsync(
        IReadOnlyCollection<Guid> stationIds,
        CancellationToken cancellationToken = default)
    {
        var distinctIds = stationIds.Distinct().ToArray();
        LockRequests.Add(distinctIds);
        return Task.FromResult(new StationCanonicalizationResult(
            distinctIds.ToDictionary(
                stationId => stationId,
                stationId => _mapping.TryGetValue(stationId, out var canonical) ? canonical : stationId),
            distinctIds.ToHashSet()));
    }
}
