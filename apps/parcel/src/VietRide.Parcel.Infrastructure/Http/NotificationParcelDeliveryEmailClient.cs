using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Shared.Kernel.Serialization;

namespace VietRide.Parcel.Infrastructure.Http;

public sealed class NotificationParcelDeliveryEmailClient : IParcelDeliveryEmailClient
{
    private const string EmailsPath = "/internal/v1/emails";
    private const string TemplateKey = "PARCEL_DELIVERY_LINK";
    private static readonly JsonSerializerOptions JsonOptions = UtcJson.Options;

    private readonly HttpClient _httpClient;
    private readonly string _publicAppUrl;

    public NotificationParcelDeliveryEmailClient(
        HttpClient httpClient,
        IOptions<ParcelDeliveryEmailOptions> options)
    {
        _httpClient = httpClient;
        _publicAppUrl = options.Value.PublicAppUrl.TrimEnd('/');
    }

    public async Task SendDeliveryLinkAsync(
        ParcelDeliveryEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        var deliveryUrl =
            $"{_publicAppUrl}/parcels/delivery/confirm?token={Uri.EscapeDataString(request.DeliveryToken.ToString("D"))}";
        var body = new
        {
            notificationId = (Guid?)null,
            dedupeKey = $"parcel-delivery-token:{request.DeliveryTokenId:D}",
            toEmail = request.ToEmail,
            templateKey = TemplateKey,
            templateData = new
            {
                deliveryUrl,
                parcelCode = request.ParcelCode,
                expiresAt = request.ExpiresAt,
            },
        };

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, EmailsPath)
            {
                Content = JsonContent.Create(body, options: JsonOptions),
            };
            message.Headers.TryAddWithoutValidation(
                "Idempotency-Key",
                request.DeliveryTokenId.ToString("D"));

            using var response = await _httpClient
                .SendAsync(message, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Accepted)
            {
                return;
            }

            _ = await response.Content
                .ReadAsByteArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            throw new ParcelDependencyUnavailableException(
                "UPSTREAM_UNAVAILABLE",
                $"Notification Service did not accept the parcel delivery email (status {(int)response.StatusCode}).");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ParcelDependencyUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ParcelDependencyUnavailableException(
                "UPSTREAM_UNAVAILABLE",
                $"Notification Service could not accept the parcel delivery email: {exception.GetType().Name}.");
        }
    }
}
