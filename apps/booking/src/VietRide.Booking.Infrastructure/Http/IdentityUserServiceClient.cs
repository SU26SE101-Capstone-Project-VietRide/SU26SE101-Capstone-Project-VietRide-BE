using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.Infrastructure.Http;

public sealed class IdentityUserServiceClient : IIdentityUserServiceClient
{
    private readonly HttpClient _httpClient;

    public IdentityUserServiceClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<Guid?> GetUserIdByPhoneAsync(
        string phone,
        CancellationToken cancellationToken = default)
    {
        var canonicalPhone = PhoneNumber.Normalize(phone).Value;
        var path = $"/internal/v1/users/by-phone?phone={Uri.EscapeDataString(canonicalPhone)}";

        try
        {
            using var response = await _httpClient.GetAsync(path, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound
                && await IsResourceNotFoundAsync(response, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
                throw new BookingUpstreamUnavailableException("Identity user lookup failed.");

            var payload = await response.Content
                .ReadFromJsonAsync<UserLookupResponse>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (payload is null || payload.UserId == Guid.Empty)
                throw new JsonException("Identity user lookup returned an invalid payload.");

            return payload.UserId;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BookingUpstreamUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new BookingUpstreamUnavailableException("Identity user lookup is unavailable.", exception);
        }
    }

    private static async Task<bool> IsResourceNotFoundAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            using var body = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return body.RootElement.GetProperty("error").GetProperty("code").GetString()
                == "RESOURCE_NOT_FOUND";
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
    }

    private sealed record UserLookupResponse(Guid UserId);
}
