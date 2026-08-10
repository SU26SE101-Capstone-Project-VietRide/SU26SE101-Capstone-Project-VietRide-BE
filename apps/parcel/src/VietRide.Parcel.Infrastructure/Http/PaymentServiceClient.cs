using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Infrastructure.Http;

public sealed class PaymentServiceClient : IPaymentServiceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<PaymentServiceClient> _logger;

    public PaymentServiceClient(HttpClient httpClient, ILogger<PaymentServiceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ChargeOutcome> ChargeParcelPaymentAsync(
        string referenceType,
        Guid referenceId,
        Guid userId,
        long amount,
        string method,
        string idempotencyKey,
        CancellationToken cancellationToken = default,
        PaymentContextSnapshot? context = null,
        DateTimeOffset? dueAt = null,
        string? paymentReturnMode = null)
    {
        try
        {
            var body = new
            {
                referenceType,
                referenceId,
                userId,
                amount,
                method,
                context,
                dueAt,
                paymentReturnMode,
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/payments/charge")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(body, JsonOptions),
                    Encoding.UTF8,
                    "application/json"),
            };

            if (!string.IsNullOrWhiteSpace(idempotencyKey))
                request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

            using var response = await _httpClient
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var envelope = await response.Content
                        .ReadFromJsonAsync<ApiResponse<ChargeResult>>(JsonOptions, cancellationToken)
                        .ConfigureAwait(false);

                    if (envelope?.Data is null)
                        return new ChargeOutcome(ChargeOutcomeKind.TransportError, null,
                            "Payment charge returned null data.");

                    return new ChargeOutcome(ChargeOutcomeKind.Success, envelope.Data, null);

                case (HttpStatusCode)402:
                case HttpStatusCode.Conflict:
                case HttpStatusCode.UnprocessableEntity:
                case HttpStatusCode.ServiceUnavailable:
                    var errorResponse = await response.Content
                        .ReadFromJsonAsync<ApiResponse>(JsonOptions, cancellationToken)
                        .ConfigureAwait(false);

                    var errorCode = errorResponse?.Error?.Code ?? string.Empty;

                    if (string.Equals(errorCode, "INSUFFICIENT_FUNDS", StringComparison.OrdinalIgnoreCase))
                        return new ChargeOutcome(ChargeOutcomeKind.InsufficientFunds, null,
                            errorResponse?.Error?.Message ?? "Insufficient funds.");

                    return new ChargeOutcome(ChargeOutcomeKind.TransportError, null,
                        errorResponse?.Error?.Message ?? $"Payment charge failed with status {(int)response.StatusCode}.",
                        (int)response.StatusCode,
                        errorCode);

                default:
                    return new ChargeOutcome(ChargeOutcomeKind.TransportError, null,
                        $"Payment service returned status {(int)response.StatusCode}.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PaymentServiceClient.ChargeParcelPaymentAsync failed.");
            return new ChargeOutcome(ChargeOutcomeKind.TransportError, null,
                $"Payment charge transport failure: {ex.Message}");
        }
    }

    /// <summary>
    /// NOTE: Payment service currently only accepts BOOKING_REFUND referenceType.
    /// Parcel refunds (PARCEL_REFUND) require Payment-side extension in a later phase.
    /// The seam shape is forward-looking; dev stub succeeds regardless.
    /// </summary>
    public async Task<RefundOutcome> RefundParcelPaymentAsync(
        Guid userId,
        long amount,
        string referenceType,
        Guid referenceId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var body = new
            {
                userId,
                amount,
                referenceType,
                referenceId,
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/wallet/refund")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(body, JsonOptions),
                    Encoding.UTF8,
                    "application/json"),
            };

            if (!string.IsNullOrWhiteSpace(idempotencyKey))
                request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

            using var response = await _httpClient
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var envelope = await response.Content
                        .ReadFromJsonAsync<ApiResponse<RefundResult>>(JsonOptions, cancellationToken)
                        .ConfigureAwait(false);

                    if (envelope?.Data is null)
                        return new RefundOutcome(RefundOutcomeKind.TransportError, null,
                            "Payment refund returned null data.");

                    return new RefundOutcome(RefundOutcomeKind.Success, envelope.Data, null);

                default:
                    return new RefundOutcome(RefundOutcomeKind.TransportError, null,
                        $"Payment refund returned status {(int)response.StatusCode}.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PaymentServiceClient.RefundParcelPaymentAsync failed.");
            return new RefundOutcome(RefundOutcomeKind.TransportError, null,
                $"Payment refund transport failure: {ex.Message}");
        }
    }
}
