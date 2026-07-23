using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VietRide.Trip.Application.Abstractions.ExternalClients;

namespace VietRide.Trip.Infrastructure.ExternalClients;

/// <summary>
/// Identity internal client used by Trip logical-FK validation.
/// </summary>
public sealed class IdentityInternalClient : IIdentityInternalClient, ISubscriptionQuotaClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public IdentityInternalClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<OperatorWriteEligibilityValidation> ValidateOperatorCanWriteAsync(
        Guid operatorId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(
                $"/internal/v1/operators/{operatorId:D}",
                cancellationToken).ConfigureAwait(false);

            return response.StatusCode switch
            {
                HttpStatusCode.OK => await ReadEligibilityAsync(response, cancellationToken).ConfigureAwait(false),
                HttpStatusCode.Forbidden => OperatorWriteEligibilityValidation.Forbidden(
                    "Operator is not approved or not active in Identity."),
                HttpStatusCode.NotFound => OperatorWriteEligibilityValidation.ValidationFailure(
                    $"Operator '{operatorId}' was not found in Identity."),
                >= HttpStatusCode.InternalServerError => OperatorWriteEligibilityValidation.ValidationFailure(
                    "Identity validation failed due to an upstream server error."),
                _ => OperatorWriteEligibilityValidation.ValidationFailure(
                    $"Identity returned unexpected status code {(int)response.StatusCode}.")
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return OperatorWriteEligibilityValidation.ValidationFailure(
                "Identity validation failed due to transport or circuit-breaker failure.");
        }
    }

    public async Task<OperatorWriteEligibilityValidation> ValidateOperatorSubscriptionCanWriteAsync(
        Guid operatorId,
        bool requireShuttleModule,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(
                $"/internal/v1/operators/{operatorId:D}/subscription",
                cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new OperatorWriteEligibilityValidation(
                    false, 404, "RESOURCE_NOT_FOUND", "Operator subscription was not found.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new OperatorWriteEligibilityValidation(
                    false, (int)response.StatusCode, "UPSTREAM_UNAVAILABLE", "Identity subscription lookup failed.");
            }

            var payload = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            var status = GetStringProperty(payload, "status");
            if (string.Equals(status, "EXPIRED", StringComparison.Ordinal))
            {
                return new OperatorWriteEligibilityValidation(
                    false, 402, "SUBSCRIPTION_EXPIRED", "Operator subscription has expired.");
            }

            var enableShuttle = payload.TryGetProperty("plan", out var plan)
                && plan.TryGetProperty("modules", out var modules)
                && modules.TryGetProperty("enableShuttle", out var value)
                && value.ValueKind == JsonValueKind.True;
            if (requireShuttleModule && !enableShuttle)
            {
                return new OperatorWriteEligibilityValidation(
                    false, 403, "SUBSCRIPTION_MODULE_DISABLED", "Shuttle module is disabled for the operator subscription.");
            }

            return OperatorWriteEligibilityValidation.Allowed();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new OperatorWriteEligibilityValidation(
                false, 503, "UPSTREAM_UNAVAILABLE", "Identity subscription lookup transport failure.");
        }
    }

    public async Task<IdentityUserLookupResult> GetUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(
                $"/internal/v1/users/{userId:D}",
                cancellationToken).ConfigureAwait(false);

            return response.StatusCode switch
            {
                HttpStatusCode.OK => await ReadUserAsync(response, cancellationToken).ConfigureAwait(false),
                HttpStatusCode.Forbidden => IdentityUserLookupResult.Forbidden("Identity rejected the internal user lookup."),
                HttpStatusCode.NotFound => IdentityUserLookupResult.ValidationFailure(
                    $"Identity user '{userId}' was not found."),
                >= HttpStatusCode.InternalServerError => IdentityUserLookupResult.ValidationFailure(
                    "Identity user lookup failed due to an upstream server error."),
                _ => IdentityUserLookupResult.ValidationFailure(
                    $"Identity returned unexpected status code {(int)response.StatusCode}.")
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return IdentityUserLookupResult.ValidationFailure(
                "Identity user lookup failed due to transport or circuit-breaker failure.");
        }
    }

    public async Task<IReadOnlyDictionary<Guid, IdentityUserProfile>> GetUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
            return new Dictionary<Guid, IdentityUserProfile>();

        var query = string.Join("&", userIds.Distinct().Take(100).Select(id => $"ids={id:D}"));
        using var response = await _httpClient.GetAsync($"/internal/v1/users?{query}", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<List<JsonElement>>(JsonOptions, cancellationToken).ConfigureAwait(false) ?? [];
        return payload.Select(ParseUserProfile).Where(profile => profile is not null).Cast<IdentityUserProfile>().ToDictionary(profile => profile.Id);
    }

    public async Task<IdentityOperatorLookupResult> GetOperatorAsync(
        Guid operatorId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(
                $"/internal/v1/operators/{operatorId:D}",
                cancellationToken).ConfigureAwait(false);

            return response.StatusCode switch
            {
                HttpStatusCode.OK => await ReadOperatorAsync(response, cancellationToken).ConfigureAwait(false),
                HttpStatusCode.Forbidden => IdentityOperatorLookupResult.Forbidden("Identity rejected the internal operator lookup."),
                HttpStatusCode.NotFound => IdentityOperatorLookupResult.ValidationFailure(
                    $"Identity operator '{operatorId}' was not found."),
                >= HttpStatusCode.InternalServerError => IdentityOperatorLookupResult.ValidationFailure(
                    "Identity operator lookup failed due to an upstream server error."),
                _ => IdentityOperatorLookupResult.ValidationFailure(
                    $"Identity returned unexpected status code {(int)response.StatusCode}.")
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return IdentityOperatorLookupResult.ValidationFailure(
                "Identity operator lookup failed due to transport or circuit-breaker failure.");
        }
    }

    public async Task<QuotaAllocationResult> ClaimQuotaAllocationAsync(
        Guid operatorId,
        string resource,
        Guid resourceId,
        string? periodKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/internal/v1/operators/{operatorId:D}/quota-allocations")
            {
                Content = JsonContent.Create(new { resource, resourceId, periodKey }),
            };
            request.Headers.TryAddWithoutValidation("Idempotency-Key", resourceId.ToString("D"));
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return QuotaAllocationResult.Rejected(
                    (int)response.StatusCode,
                    response.StatusCode == HttpStatusCode.UnprocessableEntity ? "SUBSCRIPTION_LIMIT_EXCEEDED" : "UPSTREAM_UNAVAILABLE",
                    "Identity rejected quota allocation.");
            }

            var payload = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken).ConfigureAwait(false);
            var allocationId = GetGuidProperty(payload, "allocationId");
            return allocationId.HasValue
                ? QuotaAllocationResult.Allowed(allocationId.Value)
                : QuotaAllocationResult.Rejected(502, "UPSTREAM_INVALID_RESPONSE", "Identity quota response is missing allocationId.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return QuotaAllocationResult.Rejected(503, "UPSTREAM_UNAVAILABLE", "Identity quota allocation transport failure."); }
    }

    public async Task ReleaseQuotaAllocationAsync(Guid operatorId, Guid allocationId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/internal/v1/operators/{operatorId:D}/quota-allocations/{allocationId:D}/release");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", allocationId.ToString("D"));
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<OperatorWriteEligibilityValidation> ReadEligibilityAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return OperatorWriteEligibilityValidation.ValidationFailure(
                "Identity returned an empty operator payload.");
        }

        var registrationStatus = GetStringProperty(payload, "registrationStatus");
        var isActive = GetBooleanProperty(payload, "isActive");

        if (registrationStatus is null || isActive is null)
        {
            return OperatorWriteEligibilityValidation.ValidationFailure(
                "Identity operator payload is missing registrationStatus or isActive.");
        }

        if (string.Equals(registrationStatus, "APPROVED", StringComparison.OrdinalIgnoreCase) && isActive.Value)
        {
            return OperatorWriteEligibilityValidation.Allowed();
        }

        var reason = string.Equals(registrationStatus, "APPROVED", StringComparison.OrdinalIgnoreCase)
            ? "Identity operator payload indicates the operator is inactive."
            : $"Identity operator payload has registrationStatus '{registrationStatus}'.";

        return OperatorWriteEligibilityValidation.Forbidden(reason);
    }

    private static async Task<IdentityUserLookupResult> ReadUserAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return IdentityUserLookupResult.ValidationFailure("Identity returned an empty user payload.");
        }

        var id = GetGuidProperty(payload, "id");
        var displayName = GetStringProperty(payload, "displayName");
        var avatarUrl = GetStringProperty(payload, "avatarUrl");
        var role = GetStringProperty(payload, "role");
        var operatorId = GetGuidProperty(payload, "operatorId");
        var status = GetStringProperty(payload, "status");
        if (id is null || role is null || status is null)
        {
            return IdentityUserLookupResult.ValidationFailure("Identity user payload is missing id, role, or status.");
        }

        return IdentityUserLookupResult.Success(id.Value, displayName, avatarUrl, role, operatorId, status) with
        {
            Phone = GetStringProperty(payload, "phone") ?? GetStringProperty(payload, "phoneNumber"),
        };
    }

    private static IdentityUserProfile? ParseUserProfile(JsonElement payload)
    {
        var id = GetGuidProperty(payload, "id");
        var displayName = GetStringProperty(payload, "displayName");
        var role = GetStringProperty(payload, "role");
        var status = GetStringProperty(payload, "status");
        return id is null || string.IsNullOrWhiteSpace(displayName) || role is null || status is null
            ? null
            : new IdentityUserProfile(id.Value, displayName, GetStringProperty(payload, "avatarUrl"), role, GetGuidProperty(payload, "operatorId"), status);
    }

    private static async Task<IdentityOperatorLookupResult> ReadOperatorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return IdentityOperatorLookupResult.ValidationFailure("Identity returned an empty operator payload.");
        }

        var id = GetGuidProperty(payload, "operatorId") ?? GetGuidProperty(payload, "id");
        var name = GetStringProperty(payload, "name");
        if (id is null || string.IsNullOrWhiteSpace(name))
        {
            return IdentityOperatorLookupResult.ValidationFailure("Identity operator payload is missing operatorId/id or name.");
        }

        return IdentityOperatorLookupResult.Success(id.Value, name);
    }

    private static string? GetStringProperty(JsonElement payload, string propertyName)
    {
        return payload.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static Guid? GetGuidProperty(JsonElement payload, string propertyName)
    {
        return payload.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            && Guid.TryParse(property.GetString(), out var value)
            ? value
            : null;
    }

    private static bool? GetBooleanProperty(JsonElement payload, string propertyName)
    {
        return payload.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;
    }
}
