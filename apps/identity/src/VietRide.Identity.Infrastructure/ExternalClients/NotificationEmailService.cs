using Microsoft.Extensions.Logging;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Abstractions.Http;
using VietRide.Identity.Infrastructure.Http;

namespace VietRide.Identity.Infrastructure.ExternalClients;

/// <summary>
/// Production <see cref="IEmailService"/> implementation for non-OTP transactional emails.
/// Delivers emails by POSTing to the Notification Service internal endpoint
/// (<c>POST /internal/v1/emails</c>) via <see cref="INotificationEmailClient"/>.
/// OTP delivery is now async via Outbox → RabbitMQ → Notification Service
/// (BSOT §1.2 / §3.5). Identity takes no SendGrid dependency — SendGrid is Notification-only.
///
/// Selected over <see cref="LoggingEmailService"/> when <c>EMAIL_PROVIDER=SENDGRID</c>
/// (the container/prod default); local dev keeps <c>LoggingEmailService</c>.
/// </summary>
public sealed class NotificationEmailService : IEmailService
{
    // Notification EmailTemplateKey values (apps/notification prisma enum).
    private const string SetInitialPasswordTemplateKey = "SET_INITIAL_PASSWORD";

    private readonly INotificationEmailClient _client;
    private readonly ILogger<NotificationEmailService> _logger;

    public NotificationEmailService(
        INotificationEmailClient client,
        ILogger<NotificationEmailService> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SendAccountCreatedLinkAsync(
        string to,
        AccountCreatedEmailDto accountInfo,
        CancellationToken ct = default)
    {
        var request = new NotificationEmailRequest(
            IdempotencyKey: accountInfo.OperationId,
            ToEmail: to,
            TemplateKey: SetInitialPasswordTemplateKey,
            TemplateData: new Dictionary<string, object?>
            {
                ["userId"] = accountInfo.UserId,
                ["displayName"] = accountInfo.DisplayName,
                ["setInitialPasswordUrl"] = accountInfo.SetInitialPasswordUrl,
                ["expiresAt"] = accountInfo.ExpiresAt,
            });

        await SendAsync(request, "account-created", to, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task SendParcelDeliveryLinkAsync(
        string to,
        string deliveryToken,
        ParcelDeliveryEmailDto parcelInfo,
        CancellationToken ct = default)
    {
        throw new NotImplementedException("Parcel delivery email — Day 26+");
    }

    private async Task SendAsync(
        NotificationEmailRequest request,
        string kind,
        string to,
        CancellationToken ct)
    {
        try
        {
            await _client.SendEmailAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (NotificationEmailDeliveryException ex)
        {
            _logger.LogError(
                ex,
                "Notification rejected {Kind} email to {Email} (template {TemplateKey}).",
                kind,
                to,
                request.TemplateKey);
            throw;
        }
        catch (Exception ex)
        {
            // Transport / circuit-breaker / timeout after Polly exhausted retries.
            _logger.LogError(
                ex,
                "Failed to reach Notification for {Kind} email to {Email} (template {TemplateKey}).",
                kind,
                to,
                request.TemplateKey);
            throw new NotificationEmailDeliveryException(
                $"Failed to deliver {kind} email via Notification (template '{request.TemplateKey}').",
                ex);
        }
    }
}
