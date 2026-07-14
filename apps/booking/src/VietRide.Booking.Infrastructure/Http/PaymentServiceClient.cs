using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using VietRide.Booking.Application.Abstractions.ServiceClients;

namespace VietRide.Booking.Infrastructure.Http;

/// <summary>
/// HTTP client implementation for the Payment inter-service seam.
/// Targets POST /internal/v1/payments/charge (API Contract line 1565).
/// <para>
/// Day-12 note: real wallet debit is Day 15/16. This implementation calls the
/// canonical seam path; the in-process payment service stub returns SUCCEEDED for
/// WALLET so the saga reaches CONFIRMED. VNPay is left PENDING_PAYMENT with a
/// redirect URL placeholder.
/// </para>
/// Location: Infrastructure/Http/ per BSOT §3.5 line 479.
/// </summary>
public sealed class PaymentServiceClient : IPaymentServiceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<PaymentServiceClient> _logger;

    public PaymentServiceClient(HttpClient httpClient, ILogger<PaymentServiceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<BatchChargeOutcome> BatchChargeAsync(
        Guid userId,
        string method,
        IReadOnlyList<BatchChargeItem> items,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var body = new BatchChargeRequest(userId, method, items);
            using var request = BuildJsonRequest(
                HttpMethod.Post,
                "/internal/v1/payments/batch-charge",
                body,
                idempotencyKey);

            using var response = await _httpClient.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var data = await response.Content
                    .ReadFromJsonAsync<BatchChargeData>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);

                if (data?.Payments is null)
                    return new BatchChargeOutcome.TransportError("Payment service returned null batch data.");

                var payments = data.Payments
                    .Select(x => new BatchChargePaymentResult(
                        x.PaymentId,
                        x.ReferenceType,
                        x.ReferenceId,
                        x.Status,
                        x.PaymentRedirectUrl))
                    .ToList();

                return new BatchChargeOutcome.Success(payments);
            }

            if (response.StatusCode == HttpStatusCode.PaymentRequired
                || response.StatusCode == HttpStatusCode.UnprocessableEntity
                || response.StatusCode == HttpStatusCode.Conflict)
            {
                return new BatchChargeOutcome.InsufficientFunds(
                    $"Payment service rejected batch charge (status {(int)response.StatusCode}).");
            }

            return new BatchChargeOutcome.TransportError(
                $"Payment service returned unexpected status {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PaymentServiceClient.BatchChargeAsync transport failure for {ItemCount} item(s)", items.Count);
            return new BatchChargeOutcome.TransportError($"Payment service transport failure: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<ChargeOutcome> ChargeAsync(
        string referenceType,
        Guid referenceId,
        Guid userId,
        long amount,
        string method,
        string idempotencyKey,
        CancellationToken cancellationToken = default,
        PaymentContextSnapshot? context = null)
    {
        try
        {
            var body = new ChargeRequest(referenceType, referenceId, userId, amount, method, context);
            using var request = BuildJsonRequest(
                HttpMethod.Post,
                "/internal/v1/payments/charge",
                body,
                idempotencyKey);

            using var response = await _httpClient.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var envelope = await response.Content
                    .ReadFromJsonAsync<ApiEnvelope<ChargeData>>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);

                if (envelope?.Data is null)
                    return new ChargeOutcome.TransportError("Payment service returned null data.");

                var result = new ChargeResult(
                    envelope.Data.PaymentId,
                    envelope.Data.Status,
                    envelope.Data.PaymentRedirectUrl);

                return new ChargeOutcome.Success(result);
            }

            if (response.StatusCode == HttpStatusCode.PaymentRequired
                || response.StatusCode == HttpStatusCode.UnprocessableEntity
                || response.StatusCode == HttpStatusCode.Conflict)
            {
                // Insufficient funds or business rule rejection
                return new ChargeOutcome.InsufficientFunds(
                    $"Payment service rejected charge (status {(int)response.StatusCode}).");
            }

            return new ChargeOutcome.TransportError(
                $"Payment service returned unexpected status {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PaymentServiceClient.ChargeAsync transport failure for reference {ReferenceId}", referenceId);
            return new ChargeOutcome.TransportError($"Payment service transport failure: {ex.Message}");
        }
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private static HttpRequestMessage BuildJsonRequest<T>(
        HttpMethod method,
        string uri,
        T body,
        string idempotencyKey)
    {
        var request = new HttpRequestMessage(method, uri)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body, JsonOptions),
                Encoding.UTF8,
                "application/json"),
        };

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

        return request;
    }

    // -----------------------------------------------------------------------
    // Internal request / response DTOs
    // -----------------------------------------------------------------------

    private sealed record ChargeRequest(
        string ReferenceType,
        Guid ReferenceId,
        Guid UserId,
        long Amount,
        string Method,
        PaymentContextSnapshot? Context);

    private sealed record BatchChargeRequest(
        Guid UserId,
        string Method,
        IReadOnlyList<BatchChargeItem> Items);

    private sealed record ApiEnvelope<T>(T? Data);

    private sealed record ChargeData(
        Guid PaymentId,
        string Status,
        string? PaymentRedirectUrl);

    private sealed record BatchChargeData(
        IReadOnlyList<BatchChargePaymentData> Payments);

    private sealed record BatchChargePaymentData(
        Guid PaymentId,
        string ReferenceType,
        Guid ReferenceId,
        string Status,
        string? PaymentRedirectUrl);
}
