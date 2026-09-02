using System.Net.Http.Json;
using System.Text.Json;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.Serialization;

namespace VietRide.Identity.Infrastructure.ExternalClients;

public sealed class SubscriptionPaymentClient : ISubscriptionPaymentClient
{
    private const string Path = "/internal/v1/payments/subscription";
    private static readonly JsonSerializerOptions JsonOptions = UtcJson.Options;

    private readonly HttpClient _httpClient;

    public SubscriptionPaymentClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<SubscriptionPaymentCreationResult> CreateAsync(
        SubscriptionPaymentCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = JsonContent.Create(new
            {
                request.UpgradeAttemptId,
                request.SubscriptionId,
                request.OperatorId,
                request.PlanId,
                request.BillingPeriod,
                request.PaymentMethod,
                request.Amount,
                request.DueAt,
                request.ReturnMode,
                Context = request.Snapshot,
            }, options: JsonOptions),
        };
        message.Headers.TryAddWithoutValidation("Idempotency-Key", request.IdempotencyKey);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            throw new SubscriptionPaymentClientException(
                503,
                "PAYMENT_SERVICE_UNAVAILABLE",
                "Payment service is unavailable.",
                exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                ApiResponse? failure = null;
                try
                {
                    failure = await response.Content.ReadFromJsonAsync<ApiResponse>(
                        JsonOptions,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is JsonException or NotSupportedException)
                {
                    // Preserve the upstream status when a proxy or service returns a non-ADR response.
                }

                var errorCode = failure?.Error.Code ?? "PAYMENT_SERVICE_ERROR";
                var errorMessage = failure?.Error.Message ?? "Payment subscription creation failed.";
                if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
                {
                    var errors = failure?.Error.Fields?
                        .Select(field => new ValidationError(field.Field, field.Message))
                        .ToList();
                    throw new CodedValidationException(errorCode, errorMessage, errors);
                }

                throw new SubscriptionPaymentClientException(
                    (int)response.StatusCode,
                    errorCode,
                    errorMessage);
            }

            ApiResponse<SubscriptionPaymentCreationResult>? envelope;
            try
            {
                envelope = await response.Content.ReadFromJsonAsync<ApiResponse<SubscriptionPaymentCreationResult>>(
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                throw new SubscriptionPaymentClientException(
                    502,
                    "PAYMENT_SERVICE_INVALID_RESPONSE",
                    "Payment service returned an invalid API response.",
                    exception);
            }

            if (envelope is null || !envelope.Success || envelope.Data is null)
            {
                throw new SubscriptionPaymentClientException(
                    502,
                    "PAYMENT_SERVICE_INVALID_RESPONSE",
                    "Payment service returned an invalid API envelope.");
            }

            return envelope.Data;
        }
    }

    public async Task ExpireAsync(Guid paymentId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"/internal/v1/payments/{paymentId:D}/expire-subscription");
        message.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        using var response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Payment subscription expiry failed with status {(int)response.StatusCode}.");
    }

    public async Task<IReadOnlyList<SubscriptionPaymentStatusResult>> GetStatusesAsync(
        IReadOnlyCollection<Guid> upgradeAttemptIds,
        CancellationToken cancellationToken = default)
    {
        if (upgradeAttemptIds.Count == 0)
            return Array.Empty<SubscriptionPaymentStatusResult>();
        var query = string.Join("&", upgradeAttemptIds.Select(id => $"upgradeAttemptId={id:D}"));
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(
                $"/internal/v1/payments/subscription-status?{query}",
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            throw new SubscriptionPaymentClientException(
                503,
                "PAYMENT_SERVICE_UNAVAILABLE",
                "Payment service is unavailable.",
                exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                ApiResponse? failure = null;
                try
                {
                    failure = await response.Content.ReadFromJsonAsync<ApiResponse>(
                        JsonOptions,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is JsonException or NotSupportedException)
                {
                    // Preserve the upstream status when Payment returns a non-ADR response.
                }

                throw new SubscriptionPaymentClientException(
                    (int)response.StatusCode,
                    failure?.Error.Code ?? "PAYMENT_SERVICE_ERROR",
                    failure?.Error.Message ?? "Payment status query failed.");
            }

            IReadOnlyList<SubscriptionPaymentStatusResult>? statuses;
            try
            {
                statuses = await response.Content.ReadFromJsonAsync<IReadOnlyList<SubscriptionPaymentStatusResult>>(
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                throw new SubscriptionPaymentClientException(
                    502,
                    "PAYMENT_SERVICE_INVALID_RESPONSE",
                    "Payment service returned an invalid status response.",
                    exception);
            }

            if (statuses is null)
            {
                throw new SubscriptionPaymentClientException(
                    502,
                    "PAYMENT_SERVICE_INVALID_RESPONSE",
                    "Payment service returned an empty status response.");
            }

            return statuses;
        }
    }
}
