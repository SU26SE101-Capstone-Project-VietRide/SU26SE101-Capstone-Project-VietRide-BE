namespace VietRide.Identity.Infrastructure.Http;

/// <summary>
/// Raised when the Notification Service rejects or fails to accept an internal
/// email delivery (non-2xx response, or a transport/circuit-breaker failure
/// surfaced after the Polly pipeline is exhausted). Thrown by
/// <c>NotificationEmailClient</c> / <c>NotificationEmailService</c>; because the
/// send sits inside a command handler it propagates through
/// <c>TransactionBehavior</c>, rolling back the user-create work cleanly rather
/// than silently losing the email. The message never contains the OTP code or
/// the set-initial-password URL.
/// </summary>
public sealed class NotificationEmailDeliveryException : Exception
{
    public NotificationEmailDeliveryException(string message)
        : base(message)
    {
    }

    public NotificationEmailDeliveryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
