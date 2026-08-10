using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Shared.Kernel.Serialization;

namespace VietRide.Parcel.Infrastructure.Http;

public sealed class IdentityServiceClient : IIdentityServiceClient
{
    private static readonly JsonSerializerOptions JsonOptions = UtcJson.Options;

    private readonly HttpClient _httpClient;
    private readonly ILogger<IdentityServiceClient> _logger;

    public IdentityServiceClient(HttpClient httpClient, ILogger<IdentityServiceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<UserLookupOutcome> GetUserInfoAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient
                .GetAsync($"/internal/v1/users/{userId:D}", cancellationToken)
                .ConfigureAwait(false);

            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var json = await response.Content
                        .ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken)
                        .ConfigureAwait(false);

                    var id = GetStringProperty(json, "id");
                    var role = GetStringProperty(json, "role");
                    var operatorId = GetStringProperty(json, "operatorId");
                    var status = GetStringProperty(json, "status");

                    if (id is null || role is null || status is null)
                        return new UserLookupOutcome(UserLookupOutcomeKind.TransportError, null,
                            "Identity user payload missing required fields.");

                    var userInfo = new IdentityUserInfo(
                        Guid.Parse(id),
                        role,
                        operatorId is not null ? Guid.Parse(operatorId) : null,
                        status);

                    return new UserLookupOutcome(UserLookupOutcomeKind.Success, userInfo, null);

                case HttpStatusCode.NotFound:
                    return new UserLookupOutcome(UserLookupOutcomeKind.UserNotFound, null, null);

                case HttpStatusCode.Forbidden:
                    return new UserLookupOutcome(UserLookupOutcomeKind.Forbidden, null,
                        "Identity rejected the internal user lookup.");

                default:
                    return new UserLookupOutcome(UserLookupOutcomeKind.TransportError, null,
                        $"Identity service returned status {(int)response.StatusCode}.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IdentityServiceClient.GetUserInfoAsync({UserId}) failed.", userId);
            return new UserLookupOutcome(UserLookupOutcomeKind.TransportError, null,
                $"Identity user lookup transport failure: {ex.Message}");
        }
    }

    public async Task<IdentityUserBatchOutcome> GetUsersAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        if (userIds.Any(userId => userId == Guid.Empty))
            throw new ArgumentException("User ids cannot contain an empty UUID.", nameof(userIds));

        var distinctUserIds = userIds.Distinct().ToArray();
        if (distinctUserIds.Length == 0)
            return IdentityUserBatchOutcome.Success([]);
        if (distinctUserIds.Length > 100)
            throw new ArgumentOutOfRangeException(nameof(userIds), "At most 100 distinct user ids are allowed.");

        try
        {
            var query = string.Join("&", distinctUserIds.Select(userId => $"ids={userId:D}"));
            using var response = await _httpClient
                .GetAsync($"/internal/v1/users?{query}", cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return IdentityUserBatchOutcome.TransportFailure(
                    $"Identity user batch returned status {(int)response.StatusCode}.");
            }

            var users = await response.Content
                .ReadFromJsonAsync<IReadOnlyList<IdentityUserSummary>>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (users is null || users.Any(user => user is null))
                return IdentityUserBatchOutcome.TransportFailure("Identity user batch returned an invalid payload.");

            var requestedIds = distinctUserIds.ToHashSet();
            var responseIds = users.Select(user => user.Id).ToArray();
            var malformed = responseIds.Distinct().Count() != responseIds.Length
                || users.Any(user =>
                    !requestedIds.Contains(user.Id)
                    || string.IsNullOrWhiteSpace(user.DisplayName)
                    || string.IsNullOrWhiteSpace(user.Role)
                    || string.IsNullOrWhiteSpace(user.Status));
            return malformed
                ? IdentityUserBatchOutcome.TransportFailure("Identity user batch returned an invalid payload.")
                : IdentityUserBatchOutcome.Success(users);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IdentityServiceClient.GetUsersAsync failed.");
            return IdentityUserBatchOutcome.TransportFailure(
                $"Identity user batch transport failure: {ex.Message}");
        }
    }

    public async Task<OperatorLookupOutcome> GetOperatorInfoAsync(
        Guid operatorId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient
                .GetAsync($"/internal/v1/operators/{operatorId:D}", cancellationToken)
                .ConfigureAwait(false);

            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var json = await response.Content
                        .ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken)
                        .ConfigureAwait(false);

                    var id = GetStringProperty(json, "operatorId")
                             ?? GetStringProperty(json, "id");

                    var name = GetStringProperty(json, "name");

                    if (id is null || string.IsNullOrWhiteSpace(name))
                        return new OperatorLookupOutcome(OperatorLookupOutcomeKind.TransportError, null,
                            "Identity operator payload missing required fields.");

                    var opInfo = new IdentityOperatorInfo(Guid.Parse(id), name, ReadParcelNoShowPolicy(json));
                    return new OperatorLookupOutcome(OperatorLookupOutcomeKind.Success, opInfo, null);

                case HttpStatusCode.NotFound:
                    return new OperatorLookupOutcome(OperatorLookupOutcomeKind.OperatorNotFound, null, null);

                case HttpStatusCode.Forbidden:
                    return new OperatorLookupOutcome(OperatorLookupOutcomeKind.Forbidden, null,
                        "Identity rejected the internal operator lookup.");

                default:
                    return new OperatorLookupOutcome(OperatorLookupOutcomeKind.TransportError, null,
                        $"Identity service returned status {(int)response.StatusCode}.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IdentityServiceClient.GetOperatorInfoAsync({OperatorId}) failed.", operatorId);
            return new OperatorLookupOutcome(OperatorLookupOutcomeKind.TransportError, null,
                $"Identity operator lookup transport failure: {ex.Message}");
        }
    }

    public async Task<SubscriptionWriteEligibilityOutcome> GetSubscriptionWriteEligibilityAsync(
        Guid operatorId,
        bool requireParcelModule,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient
                .GetAsync($"/internal/v1/operators/{operatorId:D}/subscription", cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return SubscriptionWriteEligibilityOutcome.Rejected(
                    404,
                    "RESOURCE_NOT_FOUND",
                    "Operator subscription was not found.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return SubscriptionWriteEligibilityOutcome.Rejected(
                    503,
                    "UPSTREAM_UNAVAILABLE",
                    $"Identity subscription lookup returned status {(int)response.StatusCode}.");
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (!TryGetGuidProperty(json, "operatorId", out var responseOperatorId)
                || responseOperatorId != operatorId)
            {
                return SubscriptionWriteEligibilityOutcome.Rejected(
                    503,
                    "UPSTREAM_UNAVAILABLE",
                    "Identity subscription lookup returned unusable operator data.");
            }

            var status = GetStringProperty(json, "status");
            if (string.Equals(status, "EXPIRED", StringComparison.Ordinal)
                || string.Equals(status, "CANCELLED", StringComparison.Ordinal))
            {
                return SubscriptionWriteEligibilityOutcome.Rejected(
                    402,
                    "SUBSCRIPTION_EXPIRED",
                    "Operator subscription is no longer active.");
            }

            if (!string.Equals(status, "ACTIVE", StringComparison.Ordinal)
                && !string.Equals(status, "PENDING_PAYMENT", StringComparison.Ordinal))
            {
                return SubscriptionWriteEligibilityOutcome.Rejected(
                    503,
                    "UPSTREAM_UNAVAILABLE",
                    "Identity subscription lookup returned an unusable status.");
            }

            if (!requireParcelModule)
                return SubscriptionWriteEligibilityOutcome.Allowed();

            if (!TryGetBooleanProperty(json, "plan", "modules", "enableParcel", out var parcelEnabled))
            {
                return SubscriptionWriteEligibilityOutcome.Rejected(
                    503,
                    "UPSTREAM_UNAVAILABLE",
                    "Identity subscription lookup returned unusable Parcel entitlement data.");
            }

            if (!parcelEnabled)
            {
                return SubscriptionWriteEligibilityOutcome.Rejected(
                    403,
                    "SUBSCRIPTION_MODULE_DISABLED",
                    "Parcel module is disabled for the operator subscription.");
            }

            return SubscriptionWriteEligibilityOutcome.Allowed();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Identity subscription lookup failed for operator {OperatorId}.", operatorId);
            return SubscriptionWriteEligibilityOutcome.Rejected(
                503,
                "UPSTREAM_UNAVAILABLE",
                "Identity subscription lookup transport failure.");
        }
    }

    private static string? GetStringProperty(JsonElement json, string propertyName)
    {
        return json.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static bool TryGetGuidProperty(JsonElement json, string propertyName, out Guid value)
    {
        value = Guid.Empty;
        return json.ValueKind == JsonValueKind.Object
            && json.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            && Guid.TryParse(property.GetString(), out value)
            && value != Guid.Empty;
    }

    private static bool TryGetBooleanProperty(
        JsonElement json,
        string objectPropertyName,
        string nestedObjectPropertyName,
        string booleanPropertyName,
        out bool value)
    {
        value = false;
        if (json.ValueKind != JsonValueKind.Object
            || !json.TryGetProperty(objectPropertyName, out var objectProperty)
            || objectProperty.ValueKind != JsonValueKind.Object
            || !objectProperty.TryGetProperty(nestedObjectPropertyName, out var nestedObjectProperty)
            || nestedObjectProperty.ValueKind != JsonValueKind.Object
            || !nestedObjectProperty.TryGetProperty(booleanPropertyName, out var booleanProperty)
            || booleanProperty.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = booleanProperty.GetBoolean();
        return true;
    }

    private static ParcelNoShowPolicy ReadParcelNoShowPolicy(JsonElement json)
    {
        if (!json.TryGetProperty("parcelNoShowPolicy", out var policy)
            || policy.ValueKind == JsonValueKind.Null)
        {
            return ParcelNoShowPolicy.Default;
        }

        if (policy.ValueKind != JsonValueKind.Object
            || !policy.TryGetProperty("noShowFeePercent", out var fee)
            || fee.ValueKind != JsonValueKind.Number
            || !fee.TryGetDecimal(out var noShowFeePercent)
            || noShowFeePercent < 0m
            || noShowFeePercent > 100m
            || !policy.TryGetProperty(
                "additionalPaymentTimeoutMinutes",
                out var timeout)
            || timeout.ValueKind != JsonValueKind.Number
            || !timeout.TryGetInt32(out var additionalPaymentTimeoutMinutes)
            || additionalPaymentTimeoutMinutes < 0)
        {
            throw new JsonException(
                "Identity parcelNoShowPolicy is malformed or out of range.");
        }

        return new ParcelNoShowPolicy(noShowFeePercent, additionalPaymentTimeoutMinutes);
    }
}
