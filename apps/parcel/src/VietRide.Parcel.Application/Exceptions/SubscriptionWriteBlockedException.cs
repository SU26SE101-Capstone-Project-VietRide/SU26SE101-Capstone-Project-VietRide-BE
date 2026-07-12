using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Exceptions;

public sealed class SubscriptionWriteBlockedException : Exception, ICodedHttpException
{
    public SubscriptionWriteBlockedException(int statusCode, string errorCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public int StatusCode { get; }
    public string ErrorCode { get; }
    string ICodedHttpException.Message => Message;
}
