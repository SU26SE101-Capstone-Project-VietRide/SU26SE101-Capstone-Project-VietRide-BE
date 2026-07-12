using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.ServiceClients;

namespace VietRide.Parcel.Infrastructure.Http;

public sealed class IdentityServiceClient : IIdentityServiceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
                    (int)response.StatusCode,
                    "UPSTREAM_UNAVAILABLE",
                    $"Identity subscription lookup returned status {(int)response.StatusCode}.");
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            var status = GetStringProperty(json, "status");
            if (string.Equals(status, "EXPIRED", StringComparison.Ordinal))
            {
                return SubscriptionWriteEligibilityOutcome.Rejected(
                    402,
                    "SUBSCRIPTION_EXPIRED",
                    "Operator subscription has expired.");
            }

            if (string.Equals(status, "PENDING_PAYMENT", StringComparison.Ordinal))
            {
                return SubscriptionWriteEligibilityOutcome.Rejected(
                    409,
                    "SUBSCRIPTION_PAYMENT_PENDING",
                    "Operator subscription payment is pending.");
            }

            var parcelEnabled = json.TryGetProperty("plan", out var plan)
                && plan.TryGetProperty("modules", out var modules)
                && modules.TryGetProperty("enableParcel", out var enabled)
                && enabled.ValueKind is JsonValueKind.True;
            if (requireParcelModule && !parcelEnabled)
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

    private static ParcelNoShowPolicy ReadParcelNoShowPolicy(JsonElement json)
    {
        if (!json.TryGetProperty("parcelNoShowPolicy", out var policy) || policy.ValueKind != JsonValueKind.Object)
            return ParcelNoShowPolicy.Default;

        var noShowFeePercent = policy.TryGetProperty("noShowFeePercent", out var fee) && fee.TryGetDecimal(out var feeValue)
            ? feeValue
            : ParcelNoShowPolicy.Default.NoShowFeePercent;
        var additionalPaymentTimeoutMinutes = policy.TryGetProperty("additionalPaymentTimeoutMinutes", out var timeout) && timeout.TryGetInt32(out var timeoutValue) && timeoutValue > 0
            ? timeoutValue
            : ParcelNoShowPolicy.Default.AdditionalPaymentTimeoutMinutes;

        return new ParcelNoShowPolicy(noShowFeePercent, additionalPaymentTimeoutMinutes);
    }
}
