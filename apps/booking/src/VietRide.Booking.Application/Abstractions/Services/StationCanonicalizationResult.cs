namespace VietRide.Booking.Application.Abstractions.Services;

public sealed class StationCanonicalizationResult
{
    private readonly IReadOnlyDictionary<Guid, Guid> _canonicalByStationId;

    public StationCanonicalizationResult(
        IReadOnlyDictionary<Guid, Guid> canonicalByStationId,
        IReadOnlySet<Guid> lockedStationIds)
    {
        _canonicalByStationId = canonicalByStationId;
        LockedStationIds = lockedStationIds;
    }

    public IReadOnlySet<Guid> LockedStationIds { get; }

    public Guid Resolve(Guid stationId)
        => _canonicalByStationId.TryGetValue(stationId, out var canonicalStationId)
            ? canonicalStationId
            : stationId;

    public Guid? Resolve(Guid? stationId)
        => stationId.HasValue ? Resolve(stationId.Value) : null;
}
