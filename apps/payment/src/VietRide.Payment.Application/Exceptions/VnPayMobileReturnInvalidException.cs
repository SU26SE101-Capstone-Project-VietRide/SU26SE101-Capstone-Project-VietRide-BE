using VietRide.Shared.Application.Exceptions;

namespace VietRide.Payment.Application.Exceptions;

public sealed class VnPayMobileReturnInvalidException : Exception, ICodedHttpException
{
    public int StatusCode => 400;
    public string ErrorCode { get; }

    public VnPayMobileReturnInvalidException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }
}
