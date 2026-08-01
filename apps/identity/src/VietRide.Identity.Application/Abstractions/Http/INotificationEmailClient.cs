namespace VietRide.Identity.Application.Abstractions.Http;

/// <summary>
/// Request payload for the Notification Service internal email endpoint
/// (<c>POST /internal/v1/emails</c>). Mirrors the Notification
/// <c>CreateEmailSend</c> contract: <c>{ notificationId?, toEmail, templateKey,
/// templateData }</c> (BSOT §5.1 internal-email contract).
/// </summary>
/// <param name="IdempotencyKey">Stable UUID-v4 for this email operation. It is
/// sent as the HTTP <c>Idempotency-Key</c> header and reused by transport retries.</param>
/// <param name="ToEmail">Recipient address.</param>
/// <param name="TemplateKey">Notification <c>EmailTemplateKey</c> value
/// (e.g. <c>AUTH_OTP</c>, <c>SET_INITIAL_PASSWORD</c>).</param>
/// <param name="TemplateData">Template variables consumed by the Notification
/// renderer. Sensitive values (OTP code, set-password URL) are scrubbed by the
/// Notification side before any persistence/log — Identity never logs them.</param>
/// <param name="NotificationId">Optional related notification id; null for
/// standalone transactional emails.</param>
public sealed record NotificationEmailRequest(
    Guid IdempotencyKey,
    string ToEmail,
    string TemplateKey,
    IReadOnlyDictionary<string, object?> TemplateData,
    Guid? NotificationId = null);

/// <summary>
/// Typed internal HTTP client to the Notification Service email endpoint.
/// Outbound calls carry the Internal JWT (<c>X-Internal-Auth</c>) minted by the
/// shared <c>InternalJwtDelegatingHandler</c> and are wrapped in the standard
/// Polly retry + circuit-breaker pipeline (VietRide.Shared.Http).
/// </summary>
public interface INotificationEmailClient
{
    /// <summary>
    /// Enqueues an email delivery on the Notification Service. Throws on a
    /// non-success response or transport failure so the caller's transaction
    /// rolls back instead of silently losing the email.
    /// </summary>
    Task SendEmailAsync(NotificationEmailRequest request, CancellationToken cancellationToken = default);
}
