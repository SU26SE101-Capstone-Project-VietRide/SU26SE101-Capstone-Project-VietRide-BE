using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Exceptions;

public sealed class PassengerHistoryUpstreamUnavailableException : Exception, ICodedHttpException
{
    public int StatusCode => 502;

    public string ErrorCode => "UPSTREAM_UNAVAILABLE";

    public PassengerHistoryUpstreamUnavailableException(string message)
        : base(message)
    {
    }
}
