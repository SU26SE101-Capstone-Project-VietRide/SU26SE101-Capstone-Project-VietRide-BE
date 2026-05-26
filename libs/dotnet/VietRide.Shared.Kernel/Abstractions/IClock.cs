namespace VietRide.Shared.Kernel.Abstractions;

/// Wraps DateTimeOffset.UtcNow so handlers/services can be tested with frozen time.
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
