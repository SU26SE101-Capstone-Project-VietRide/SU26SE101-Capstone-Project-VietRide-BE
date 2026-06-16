namespace VietRide.Trip.Application.Abstractions.SeatLock;

public interface ISeatLockTtlProvider
{
    TimeSpan DefaultTtl { get; }
}
