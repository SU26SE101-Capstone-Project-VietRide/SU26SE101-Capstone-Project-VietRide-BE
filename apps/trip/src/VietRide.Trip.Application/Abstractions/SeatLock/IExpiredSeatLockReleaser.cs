namespace VietRide.Trip.Application.Abstractions.SeatLock;

public interface IExpiredSeatLockReleaser
{
    Task<int> ReleaseExpiredAsync(int batchSize, CancellationToken cancellationToken = default);
}
