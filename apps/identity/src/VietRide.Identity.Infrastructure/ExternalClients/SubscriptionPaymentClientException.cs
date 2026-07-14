using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.Infrastructure.ExternalClients;

public sealed class SubscriptionPaymentClientException : Exception, ICodedHttpException
{
    public int StatusCode { get; }
    public string ErrorCode { get; }

    public SubscriptionPaymentClientException(int statusCode, string errorCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }
}
