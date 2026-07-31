using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Infrastructure.Http;

namespace VietRide.Parcel.UnitTests.Infrastructure;

public class IdentityServiceClientInternalClientTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OperatorId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private FakeMessageHandler _handler = null!;

    [Fact]
    public async Task GetUsersAsync_DeduplicatesIdsAndDeserializesRedactedBatch()
    {
        var body = JsonSerializer.Serialize(new[]
        {
            new
            {
                id = UserId,
                displayName = "Người dùng đã xóa",
                phone = (string?)null,
                email = (string?)null,
                avatarUrl = (string?)null,
                role = "PASSENGER",
                operatorId = (Guid?)null,
                status = "DELETED",
                deleted = true,
            },
        }, JsonOptions);
        var client = BuildClient(HttpStatusCode.OK, body);

        var outcome = await client.GetUsersAsync([UserId, UserId]);

        outcome.Kind.Should().Be(IdentityUserBatchOutcomeKind.Success);
        var user = outcome.Users.Should().ContainSingle().Which;
        user.Id.Should().Be(UserId);
        user.Deleted.Should().BeTrue();
        user.DisplayName.Should().Be("Người dùng đã xóa");
        _handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
        _handler.LastRequest.RequestUri!.AbsolutePath.Should().Be("/internal/v1/users");
        _handler.LastRequest.RequestUri.Query.Should().Be($"?ids={UserId:D}");
    }

    [Fact]
    public async Task GetUsersAsync_MapsMalformedOrNonSuccessResponseToTransportFailure()
    {
        var malformed = BuildClient(HttpStatusCode.OK, "[{\"id\":\"not-a-uuid\"}]");
        var malformedOutcome = await malformed.GetUsersAsync([UserId]);
        malformedOutcome.Kind.Should().Be(IdentityUserBatchOutcomeKind.TransportError);

        var unavailable = BuildClient(HttpStatusCode.ServiceUnavailable, "{}");
        var unavailableOutcome = await unavailable.GetUsersAsync([UserId]);
        unavailableOutcome.Kind.Should().Be(IdentityUserBatchOutcomeKind.TransportError);
    }

    [Fact]
    public async Task GetUserInfoAsync_Sends_Request_To_Correct_Path()
    {
        var body = JsonSerializer.Serialize(new
        {
            id = UserId,
            role = "PASSENGER",
            operatorId = (string?)null,
            status = "ACTIVE",
        }, JsonOptions);

        var client = BuildClient(HttpStatusCode.OK, body);

        await client.GetUserInfoAsync(UserId);

        _handler.LastRequest.Should().NotBeNull();
        _handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be($"/internal/v1/users/{UserId:D}");
        _handler.LastRequest.Method.Should().Be(HttpMethod.Get);
    }

    [Fact]
    public async Task GetUserInfoAsync_Returns_Success_On_200()
    {
        var body = JsonSerializer.Serialize(new
        {
            id = UserId,
            role = "PASSENGER",
            operatorId = (string?)null,
            status = "ACTIVE",
        }, JsonOptions);

        var client = BuildClient(HttpStatusCode.OK, body);

        var result = await client.GetUserInfoAsync(UserId);

        result.Kind.Should().Be(UserLookupOutcomeKind.Success);
        result.UserInfo.Should().NotBeNull();
        result.UserInfo!.Id.Should().Be(UserId);
        result.UserInfo.Role.Should().Be("PASSENGER");
    }

    [Fact]
    public async Task GetUserInfoAsync_Returns_UserNotFound_On_404()
    {
        var client = BuildClient(HttpStatusCode.NotFound, "{}");

        var result = await client.GetUserInfoAsync(UserId);

        result.Kind.Should().Be(UserLookupOutcomeKind.UserNotFound);
        result.UserInfo.Should().BeNull();
    }

    [Fact]
    public async Task GetUserInfoAsync_Returns_Forbidden_On_403()
    {
        var body = JsonSerializer.Serialize(new
        {
            success = false,
            statusCode = 403,
            error = new { code = "FORBIDDEN", message = "Access denied." },
        }, JsonOptions);

        var client = BuildClient(HttpStatusCode.Forbidden, body);

        var result = await client.GetUserInfoAsync(UserId);

        result.Kind.Should().Be(UserLookupOutcomeKind.Forbidden);
    }

    [Fact]
    public async Task GetOperatorInfoAsync_Returns_Success_On_200()
    {
        var body = JsonSerializer.Serialize(new
        {
            operatorId = OperatorId,
            name = "Test Operator",
        }, JsonOptions);

        var client = BuildClient(HttpStatusCode.OK, body);

        var result = await client.GetOperatorInfoAsync(OperatorId);

        result.Kind.Should().Be(OperatorLookupOutcomeKind.Success);
        result.OperatorInfo.Should().NotBeNull();
        result.OperatorInfo!.Id.Should().Be(OperatorId);
        result.OperatorInfo.Name.Should().Be("Test Operator");
        result.OperatorInfo.ParcelNoShowPolicy.Should()
            .Be(ParcelNoShowPolicy.Default);
    }

    [Fact]
    public async Task GetOperatorInfoAsync_NullPolicy_UsesCanonicalDefault()
    {
        var body = $$"""
            {
              "operatorId": "{{OperatorId:D}}",
              "name": "Test Operator",
              "parcelNoShowPolicy": null
            }
            """;
        var client = BuildClient(HttpStatusCode.OK, body);

        var result = await client.GetOperatorInfoAsync(OperatorId);

        result.Kind.Should().Be(OperatorLookupOutcomeKind.Success);
        result.OperatorInfo!.ParcelNoShowPolicy.Should()
            .Be(ParcelNoShowPolicy.Default);
    }

    [Fact]
    public async Task GetOperatorInfoAsync_ValidPolicy_PreservesBothValues()
    {
        var body = $$"""
            {
              "operatorId": "{{OperatorId:D}}",
              "name": "Test Operator",
              "parcelNoShowPolicy": {
                "noShowFeePercent": 12.5,
                "additionalPaymentTimeoutMinutes": 0
              }
            }
            """;
        var client = BuildClient(HttpStatusCode.OK, body);

        var result = await client.GetOperatorInfoAsync(OperatorId);

        result.Kind.Should().Be(OperatorLookupOutcomeKind.Success);
        result.OperatorInfo!.ParcelNoShowPolicy.Should()
            .Be(new ParcelNoShowPolicy(12.5m, 0));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"noShowFeePercent":10}""")]
    [InlineData("""{"additionalPaymentTimeoutMinutes":30}""")]
    [InlineData("""{"noShowFeePercent":"10","additionalPaymentTimeoutMinutes":30}""")]
    [InlineData("""{"noShowFeePercent":10,"additionalPaymentTimeoutMinutes":"30"}""")]
    [InlineData("""{"noShowFeePercent":-1,"additionalPaymentTimeoutMinutes":30}""")]
    [InlineData("""{"noShowFeePercent":101,"additionalPaymentTimeoutMinutes":30}""")]
    [InlineData("""{"noShowFeePercent":10,"additionalPaymentTimeoutMinutes":-1}""")]
    [InlineData("""{"noShowFeePercent":10,"additionalPaymentTimeoutMinutes":1.5}""")]
    public async Task GetOperatorInfoAsync_MalformedPresentPolicy_FailsClosed(
        string policyJson)
    {
        var body = $$"""
            {
              "operatorId": "{{OperatorId:D}}",
              "name": "Test Operator",
              "parcelNoShowPolicy": {{policyJson}}
            }
            """;
        var client = BuildClient(HttpStatusCode.OK, body);

        var result = await client.GetOperatorInfoAsync(OperatorId);

        result.Kind.Should().Be(OperatorLookupOutcomeKind.TransportError);
        result.OperatorInfo.Should().BeNull();
    }

    [Fact]
    public async Task GetOperatorInfoAsync_Returns_OperatorNotFound_On_404()
    {
        var client = BuildClient(HttpStatusCode.NotFound, "{}");

        var result = await client.GetOperatorInfoAsync(OperatorId);

        result.Kind.Should().Be(OperatorLookupOutcomeKind.OperatorNotFound);
    }

    [Fact]
    public async Task GetOperatorInfoAsync_Returns_Forbidden_On_403()
    {
        var client = BuildClient(HttpStatusCode.Forbidden, "{}");

        var result = await client.GetOperatorInfoAsync(OperatorId);

        result.Kind.Should().Be(OperatorLookupOutcomeKind.Forbidden);
    }

    private IdentityServiceClient BuildClient(HttpStatusCode status, string body)
    {
        _handler = new FakeMessageHandler(status, body);
        var httpClient = new HttpClient(_handler)
        {
            BaseAddress = new Uri("http://identity-service"),
        };
        return new IdentityServiceClient(httpClient, NullLogger<IdentityServiceClient>.Instance);
    }
}
