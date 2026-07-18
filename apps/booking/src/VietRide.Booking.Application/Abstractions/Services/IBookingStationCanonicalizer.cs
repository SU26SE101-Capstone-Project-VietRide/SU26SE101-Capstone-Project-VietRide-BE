namespace VietRide.Booking.Application.Abstractions.Services;

public interface IBookingStationCanonicalizer
{
    Task<StationCanonicalizationResult> LockAndResolveAsync(
        IReadOnlyCollection<Guid> stationIds,
        CancellationToken cancellationToken = default);
}
