using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Infrastructure.Http;

namespace VietRide.Parcel.UnitTests.Infrastructure;

public sealed class PendingPaymentParcelEntitlementTests
{
    private static readonly Guid OperatorId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Theory]
    [InlineData("ACTIVE")]
    [InlineData("PENDING_PAYMENT")]
    public async Task EligibleStatus_WithActivePlanParcelModuleEnabled_AllowsWrite(string status)
    {
        var handler = new FakeMessageHandler(HttpStatusCode.OK, BuildPayload(status, enableParcel: true));
        var client = BuildClient(handler);

        var result = await client.GetSubscriptionWriteEligibilityAsync(
            OperatorId,
            requireParcelModule: true);

        result.IsAllowed.Should().BeTrue();
        result.FailureStatusCode.Should().BeNull();
        result.ErrorCode.Should().BeNull();
        handler.LastRequest!.RequestUri!.AbsolutePath.Should()
            .Be($"/internal/v1/operators/{OperatorId:D}/subscription");
    }

    [Theory]
    [InlineData("ACTIVE")]
    [InlineData("PENDING_PAYMENT")]
    public async Task EligibleStatus_WithActivePlanParcelModuleDisabled_ReturnsModuleDisabled(string status)
    {
        var client = BuildClient(new FakeMessageHandler(
            HttpStatusCode.OK,
            BuildPayload(status, enableParcel: false)));

        var result = await client.GetSubscriptionWriteEligibilityAsync(
            OperatorId,
            requireParcelModule: true);

        result.IsAllowed.Should().BeFalse();
        result.FailureStatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("SUBSCRIPTION_MODULE_DISABLED");
    }

    [Theory]
    [InlineData("EXPIRED")]
    [InlineData("CANCELLED")]
    public async Task TerminalStatus_ReturnsSubscriptionExpired(string status)
    {
        var client = BuildClient(new FakeMessageHandler(
            HttpStatusCode.OK,
            BuildPayload(status, enableParcel: true)));

        var result = await client.GetSubscriptionWriteEligibilityAsync(
            OperatorId,
            requireParcelModule: true);

        result.IsAllowed.Should().BeFalse();
        result.FailureStatusCode.Should().Be(402);
        result.ErrorCode.Should().Be("SUBSCRIPTION_EXPIRED");
    }

    [Fact]
    public async Task ActivePersistedStatus_WithInactiveEntitlementFlag_ReturnsSubscriptionExpired()
    {
        var payload = $$"""
            {
              "operatorId": "{{OperatorId:D}}",
              "status": "ACTIVE",
              "entitlementActive": false,
              "plan": { "modules": { "enableParcel": true } }
            }
            """;
        var client = BuildClient(new FakeMessageHandler(HttpStatusCode.OK, payload));

        var result = await client.GetSubscriptionWriteEligibilityAsync(
            OperatorId,
            requireParcelModule: true);

        result.IsAllowed.Should().BeFalse();
        result.FailureStatusCode.Should().Be(402);
        result.ErrorCode.Should().Be("SUBSCRIPTION_EXPIRED");
    }

    [Theory]
    [InlineData("PENDING_APPROVAL")]
    [InlineData("UNKNOWN")]
    [InlineData("")]
    public async Task NonEligibleOrUnknownStatus_FailsClosed(string status)
    {
        var client = BuildClient(new FakeMessageHandler(
            HttpStatusCode.OK,
            BuildPayload(status, enableParcel: true)));

        var result = await client.GetSubscriptionWriteEligibilityAsync(
            OperatorId,
            requireParcelModule: true);

        AssertUpstreamUnavailable(result);
    }

    [Fact]
    public async Task PendingPayment_UsesActivePlanAndIgnoresPendingTargetPlan()
    {
        var payload = $$"""
            {
              "operatorId": "{{OperatorId:D}}",
              "status": "PENDING_PAYMENT",
              "plan": { "modules": { "enableParcel": true } },
              "pendingUpgrade": {
                "targetPlan": { "modules": { "enableParcel": false } }
              }
            }
            """;
        var client = BuildClient(new FakeMessageHandler(HttpStatusCode.OK, payload));

        var result = await client.GetSubscriptionWriteEligibilityAsync(
            OperatorId,
            requireParcelModule: true);

        result.IsAllowed.Should().BeTrue();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("not-json")]
    public async Task MalformedPayload_FailsClosed(string payload)
    {
        var client = BuildClient(new FakeMessageHandler(HttpStatusCode.OK, payload));

        var result = await client.GetSubscriptionWriteEligibilityAsync(
            OperatorId,
            requireParcelModule: true);

        AssertUpstreamUnavailable(result);
    }

    [Fact]
    public async Task MismatchedOperator_FailsClosed()
    {
        var payload = BuildPayload("ACTIVE", enableParcel: true, operatorId: Guid.NewGuid());
        var client = BuildClient(new FakeMessageHandler(HttpStatusCode.OK, payload));

        var result = await client.GetSubscriptionWriteEligibilityAsync(
            OperatorId,
            requireParcelModule: true);

        AssertUpstreamUnavailable(result);
    }

    [Theory]
    [InlineData("{ \"operatorId\": \"22222222-2222-2222-2222-222222222222\", \"status\": \"ACTIVE\" }")]
    [InlineData("{ \"operatorId\": \"22222222-2222-2222-2222-222222222222\", \"status\": \"ACTIVE\", \"plan\": { \"modules\": {} } }")]
    [InlineData("{ \"operatorId\": \"22222222-2222-2222-2222-222222222222\", \"status\": \"ACTIVE\", \"plan\": { \"modules\": { \"enableParcel\": \"true\" } } }")]
    public async Task MissingOrMalformedModuleFlag_FailsClosed(string payload)
    {
        var client = BuildClient(new FakeMessageHandler(HttpStatusCode.OK, payload));

        var result = await client.GetSubscriptionWriteEligibilityAsync(
            OperatorId,
            requireParcelModule: true);

        AssertUpstreamUnavailable(result);
    }

    [Fact]
    public async Task NotFound_PreservesCanonicalResourceNotFound()
    {
        var client = BuildClient(new FakeMessageHandler(HttpStatusCode.NotFound, "{}"));

        var result = await client.GetSubscriptionWriteEligibilityAsync(
            OperatorId,
            requireParcelModule: true);

        result.IsAllowed.Should().BeFalse();
        result.FailureStatusCode.Should().Be(404);
        result.ErrorCode.Should().Be("RESOURCE_NOT_FOUND");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task OtherNonSuccessStatus_FailsClosedAsUpstreamUnavailable(HttpStatusCode statusCode)
    {
        var client = BuildClient(new FakeMessageHandler(statusCode, "{}"));

        var result = await client.GetSubscriptionWriteEligibilityAsync(
            OperatorId,
            requireParcelModule: true);

        AssertUpstreamUnavailable(result);
    }

    [Fact]
    public async Task TransportFailure_FailsClosedAsUpstreamUnavailable()
    {
        var client = BuildClient(new ExceptionMessageHandler(new HttpRequestException("Identity unavailable.")));

        var result = await client.GetSubscriptionWriteEligibilityAsync(
            OperatorId,
            requireParcelModule: true);

        AssertUpstreamUnavailable(result);
    }

    private static string BuildPayload(string status, bool enableParcel, Guid? operatorId = null)
        => $$"""
            {
              "operatorId": "{{(operatorId ?? OperatorId):D}}",
              "status": "{{status}}",
              "plan": { "modules": { "enableParcel": {{enableParcel.ToString().ToLowerInvariant()}} } }
            }
            """;

    private static IdentityServiceClient BuildClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://identity-service"),
        };
        return new IdentityServiceClient(httpClient, NullLogger<IdentityServiceClient>.Instance);
    }

    private static void AssertUpstreamUnavailable(
        SubscriptionWriteEligibilityOutcome result)
    {
        result.IsAllowed.Should().BeFalse();
        result.FailureStatusCode.Should().Be(503);
        result.ErrorCode.Should().Be("UPSTREAM_UNAVAILABLE");
    }
}
