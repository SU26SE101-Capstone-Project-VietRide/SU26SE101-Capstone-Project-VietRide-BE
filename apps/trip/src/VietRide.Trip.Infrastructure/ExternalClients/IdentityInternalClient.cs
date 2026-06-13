using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VietRide.Trip.Application.Abstractions.ExternalClients;

namespace VietRide.Trip.Infrastructure.ExternalClients;

/// <summary>
/// Identity internal client used by Trip logical-FK validation.
/// </summary>
public sealed class IdentityInternalClient : IIdentityInternalClient
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
        var role = GetStringProperty(payload, "role");
        var operatorId = GetGuidProperty(payload, "operatorId");
        var status = GetStringProperty(payload, "status");
        if (id is null || role is null || status is null)
        {
            return IdentityUserLookupResult.ValidationFailure("Identity user payload is missing id, role, or status.");
        }

        return IdentityUserLookupResult.Success(id.Value, role, operatorId, status);
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
