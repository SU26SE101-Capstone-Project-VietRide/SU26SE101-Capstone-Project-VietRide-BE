namespace VietRide.Trip.Domain.Exceptions;

public sealed class TripCargoCapacityExceededException : InvalidOperationException
{
    public TripCargoCapacityExceededException(string message)
        : base(message)
    {
    }
}
