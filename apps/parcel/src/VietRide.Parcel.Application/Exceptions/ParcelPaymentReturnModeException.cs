using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Exceptions;

public sealed class ParcelPaymentReturnModeException : Exception, ICodedHttpException
{
    public int StatusCode { get; }
    public string ErrorCode { get; }

    public ParcelPaymentReturnModeException(int statusCode, string errorCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }
}
