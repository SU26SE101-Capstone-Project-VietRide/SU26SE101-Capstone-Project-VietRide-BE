using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.Application.Exceptions;

public sealed class ShuttleDistanceUnavailableException : Exception, ICodedHttpException
{
    public int StatusCode => 503;
    public string ErrorCode => "SHUTTLE_DISTANCE_UNAVAILABLE";

    public ShuttleDistanceUnavailableException(string message)
        : base(message)
    {
    }
}
