using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.Application.Exceptions;

public sealed class BookingUpstreamUnavailableException : Exception, ICodedHttpException
{
    public int StatusCode => 502;

    public string ErrorCode => "UPSTREAM_UNAVAILABLE";

    public BookingUpstreamUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
