using VietRide.Shared.Application.Exceptions;

namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed class TripIdentityUnavailableException : Exception, ICodedHttpException
{
    public int StatusCode => 503;

    public string ErrorCode => "UPSTREAM_UNAVAILABLE";

    public TripIdentityUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
