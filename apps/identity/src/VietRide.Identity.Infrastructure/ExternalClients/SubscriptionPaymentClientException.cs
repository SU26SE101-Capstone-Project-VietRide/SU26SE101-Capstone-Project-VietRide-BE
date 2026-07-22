using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.Infrastructure.ExternalClients;

public sealed class SubscriptionPaymentClientException : Exception, ICodedHttpException
{
    public int StatusCode { get; }
    public string ErrorCode { get; }

    public SubscriptionPaymentClientException(
        int statusCode,
        string errorCode,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }
}
