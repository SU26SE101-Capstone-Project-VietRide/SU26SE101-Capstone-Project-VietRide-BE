using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Exceptions;

public sealed class ParcelDependencyUnavailableException : Exception, ICodedHttpException
{
    public int StatusCode => 503;

    public string ErrorCode { get; }

    public ParcelDependencyUnavailableException(
        string errorCode,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}
