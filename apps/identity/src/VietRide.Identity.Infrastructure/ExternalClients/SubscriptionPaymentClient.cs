using System.Net.Http.Json;
using System.Text.Json;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Infrastructure.ExternalClients;

public sealed class SubscriptionPaymentClient : ISubscriptionPaymentClient
{
    private const string Path = "/internal/v1/payments/subscription";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
                request.Amount,
            }, options: JsonOptions),
        };
        message.Headers.TryAddWithoutValidation("Idempotency-Key", request.IdempotencyKey);

        using var response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Payment subscription creation failed with status {(int)response.StatusCode}.");

        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<SubscriptionPaymentCreationResult>>(
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
        if (envelope is null || !envelope.Success || envelope.Data is null)
            throw new HttpRequestException("Payment subscription creation returned an invalid API envelope.");

        return envelope.Data;
    }

    public async Task ExpireAsync(Guid paymentId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"/internal/v1/payments/{paymentId:D}/expire-subscription");
        message.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        using var response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Payment subscription expiry failed with status {(int)response.StatusCode}.");
    }
}
