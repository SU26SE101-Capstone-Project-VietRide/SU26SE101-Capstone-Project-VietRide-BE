using VietRide.Shared.Application.Exceptions;

namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed class TripUpstreamUnavailableException : Exception, ICodedHttpException
{
    public int StatusCode => 502;

    public string ErrorCode => "UPSTREAM_UNAVAILABLE";

    public TripUpstreamUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
