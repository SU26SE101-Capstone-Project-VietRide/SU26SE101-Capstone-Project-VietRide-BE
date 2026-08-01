using System.Net.Http.Json;
using System.Text.Json;
using VietRide.Identity.Application.Abstractions.Http;

namespace VietRide.Identity.Infrastructure.Http;

/// <summary>
/// Typed HTTP client to the Notification Service internal email endpoint
/// (<c>POST /internal/v1/emails</c>). The Internal JWT header, correlation id,
/// retry and circuit-breaker are applied by the delegating-handler + Polly
/// pipeline configured on the <see cref="HttpClient"/> in
/// <c>InfrastructureServiceCollectionExtensions</c> — this class only shapes the
/// request body and validates the response status.
/// </summary>
public sealed class NotificationEmailClient : INotificationEmailClient
{
    private const string EmailsPath = "/internal/v1/emails";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public NotificationEmailClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task SendEmailAsync(
        NotificationEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            notificationId = request.NotificationId,
            toEmail = request.ToEmail,
            templateKey = request.TemplateKey,
            templateData = request.TemplateData,
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, EmailsPath)
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };
        message.Headers.TryAddWithoutValidation(
            "Idempotency-Key",
            request.IdempotencyKey.ToString("D"));

        using var response = await _httpClient
            .SendAsync(message, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // Drain the error body so the keep-alive connection can be reused
            // by the pool before we dispose. The body is NOT inspected/logged —
            // it may carry nothing sensitive here, but we keep the message free
            // of templateData (OTP code / set-password URL) regardless.
            await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
            throw new NotificationEmailDeliveryException(
                $"Notification email delivery failed with status {(int)response.StatusCode} " +
                $"for template '{request.TemplateKey}'.");
        }
    }
}
