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

    public async Task<IReadOnlyDictionary<Guid, BookingBuyerSnapshotProfile>> GetUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, BookingBuyerSnapshotProfile>();
        }

        try
        {
            var profiles = new Dictionary<Guid, BookingBuyerSnapshotProfile>();
            foreach (var chunk in userIds.Distinct().Chunk(100))
            {
                var query = string.Join("&", chunk.Select(id => $"ids={id:D}"));
                using var response = await _httpClient.GetAsync(
                    $"/internal/v1/users?{query}",
                    cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw new BookingUpstreamUnavailableException("Identity user batch lookup failed.");
                }

                var payload = await response.Content.ReadFromJsonAsync<List<IdentityUserProjection>>(
                    cancellationToken: cancellationToken).ConfigureAwait(false)
                    ?? throw new JsonException("Identity user batch lookup returned an invalid payload.");
                foreach (var user in payload)
                {
                    if (user.Id == Guid.Empty || string.IsNullOrWhiteSpace(user.DisplayName))
                    {
                        throw new JsonException("Identity user batch lookup returned an invalid user.");
                    }

                    var deleted = user.Deleted;
                    profiles[user.Id] = new BookingBuyerSnapshotProfile(
                        user.Id,
                        deleted ? BookingBuyerSnapshotProfile.DeletedDisplayName : user.DisplayName.Trim(),
                        deleted ? null : NormalizeOptional(user.Phone),
                        deleted ? null : NormalizeOptional(user.Email),
                        deleted ? null : NormalizeOptional(user.AvatarUrl),
                        deleted);
                }
            }

            return profiles;
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
            throw new BookingUpstreamUnavailableException(
                "Identity user batch lookup is unavailable.",
                exception);
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

    private sealed record IdentityUserProjection(
        Guid Id,
        string? DisplayName,
        string? Phone,
        string? Email,
        string? AvatarUrl,
        bool Deleted);

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
