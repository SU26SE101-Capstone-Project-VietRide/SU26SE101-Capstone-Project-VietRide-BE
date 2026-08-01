using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using VietRide.Booking.Application.Abstractions.ServiceClients;

namespace VietRide.Booking.Infrastructure.Http;

/// <summary>
/// Read-only direct Payment client for resumable redirect lookup. This client deliberately does
/// not share the mutation client's idempotency or retry pipeline.
/// </summary>
public sealed class PaymentRedirectLookupClient : IPaymentRedirectLookupClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;

    public PaymentRedirectLookupClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<PaymentRedirectLookupItem>> LookupAsync(
        Guid userId,
        IReadOnlyCollection<PaymentRedirectLookupReference> references,
        CancellationToken cancellationToken = default)
    {
        if (references.Count == 0)
            return [];

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                    "/internal/v1/payments/redirect-sessions/lookup",
                    new LookupRequest(userId, references),
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return [];

            var items = await response.Content
                .ReadFromJsonAsync<IReadOnlyList<LookupResponse>>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (items is null || items.Any(item => !IsWellFormed(item)))
                return [];

            return items
                .Select(item => new PaymentRedirectLookupItem(
                    item.PaymentId!.Value,
                    item.ReferenceType!,
                    item.ReferenceId!.Value,
                    item.Amount!.Value,
                    item.DueAt!.Value,
                    item.PaymentRedirectUrl!))
                .ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static bool IsWellFormed(LookupResponse item)
    {
        if (!item.PaymentId.HasValue
            || item.PaymentId.Value == Guid.Empty
            || string.IsNullOrWhiteSpace(item.ReferenceType)
            || !item.ReferenceId.HasValue
            || item.ReferenceId.Value == Guid.Empty
            || !item.Amount.HasValue
            || item.Amount.Value < 0
            || !item.DueAt.HasValue
            || string.IsNullOrWhiteSpace(item.PaymentRedirectUrl))
        {
            return false;
        }

        if (!string.Equals(item.ReferenceType, "BOOKING", StringComparison.Ordinal)
            && !string.Equals(item.ReferenceType, "BOOKING_GROUP", StringComparison.Ordinal))
        {
            return false;
        }

        return Uri.TryCreate(item.PaymentRedirectUrl, UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(uri.UserInfo);
    }

    private sealed record LookupRequest(
        Guid UserId,
        IReadOnlyCollection<PaymentRedirectLookupReference> References);

    private sealed record LookupResponse(
        Guid? PaymentId,
        string? ReferenceType,
        Guid? ReferenceId,
        long? Amount,
        DateTimeOffset? DueAt,
        string? PaymentRedirectUrl);
}
