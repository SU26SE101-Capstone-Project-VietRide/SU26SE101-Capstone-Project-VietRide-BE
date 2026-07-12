using VietRide.Shared.Application.Exceptions;

namespace VietRide.Trip.Application.Features.Stops;

public sealed class TripSubscriptionWriteBlockedException : Exception, ICodedHttpException
{
    public TripSubscriptionWriteBlockedException(int statusCode, string errorCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public int StatusCode { get; }
    public string ErrorCode { get; }
    string ICodedHttpException.Message => Message;
}
