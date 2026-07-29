using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Shared.Http.Handlers;
using VietRide.Trip.Infrastructure.ExternalClients;

namespace VietRide.Trip.UnitTests.ExternalClients;

public sealed class IdentityInternalClientTests
{
    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task ValidateOperatorCanWriteAsync_ReturnsAllowed_WhenOperatorApprovedAndActive()
    {
        using var httpClient = CreateHttpClient(new JsonResponseHandler(HttpStatusCode.OK,
            """
            {"registrationStatus":"APPROVED","isActive":true}
            """));
        var client = new IdentityInternalClient(httpClient);

        var result = await client.ValidateOperatorCanWriteAsync(OperatorId);

        result.IsAllowed.Should().BeTrue();
        result.FailureStatusCode.Should().BeNull();
        result.ErrorCode.Should().BeNull();
    }

    [Theory]
    [InlineData("PENDING", true)]
    [InlineData("REJECTED", true)]
    [InlineData("SUSPENDED", true)]
    [InlineData("APPROVED", false)]
    public async Task ValidateOperatorCanWriteAsync_ReturnsForbidden_WhenOperatorCannotWrite(
        string registrationStatus,
        bool isActive)
    {
        using var httpClient = CreateHttpClient(new JsonResponseHandler(HttpStatusCode.OK,
            $$"""
            {"registrationStatus":"{{registrationStatus}}","isActive":{{isActive.ToString().ToLowerInvariant()}}}
            """));
        var client = new IdentityInternalClient(httpClient);

        var result = await client.ValidateOperatorCanWriteAsync(OperatorId);

        result.IsAllowed.Should().BeFalse();
        result.FailureStatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task ValidateOperatorCanWriteAsync_ReturnsValidationError_WhenIdentityReturnsNotFound()
    {
        using var httpClient = CreateHttpClient(new JsonResponseHandler(HttpStatusCode.NotFound, "{}"));
        var client = new IdentityInternalClient(httpClient);

        var result = await client.ValidateOperatorCanWriteAsync(OperatorId);

        result.IsAllowed.Should().BeFalse();
        result.FailureStatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task ValidateOperatorCanWriteAsync_ReturnsValidationError_WhenIdentityReturnsServerError()
    {
        using var httpClient = CreateHttpClient(new JsonResponseHandler(HttpStatusCode.InternalServerError, "{}"));
        var client = new IdentityInternalClient(httpClient);

        var result = await client.ValidateOperatorCanWriteAsync(OperatorId);

        result.IsAllowed.Should().BeFalse();
        result.FailureStatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task ValidateOperatorCanWriteAsync_ReturnsValidationError_WhenTransportFails()
    {
        using var httpClient = CreateHttpClient(new ThrowingHandler());
        var client = new IdentityInternalClient(httpClient);

        var result = await client.ValidateOperatorCanWriteAsync(OperatorId);

        result.IsAllowed.Should().BeFalse();
        result.FailureStatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task GetUserAsync_ReturnsUserLookup_WhenIdentityReturnsRawUser()
    {
        var userId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        using var httpClient = CreateHttpClient(new JsonResponseHandler(HttpStatusCode.OK,
            $$"""
            {"id":"{{userId:D}}","role":"DRIVER","operatorId":"{{OperatorId:D}}","status":"ACTIVE"}
            """));
        var client = new IdentityInternalClient(httpClient);

        var result = await client.GetUserAsync(userId);

        result.Found.Should().BeTrue();
        result.Id.Should().Be(userId);
        result.Role.Should().Be("DRIVER");
        result.OperatorId.Should().Be(OperatorId);
        result.Status.Should().Be("ACTIVE");
    }

    [Fact]
    public async Task GetUserAsync_ReturnsValidationError_WhenIdentityOmitsStatus()
    {
        var userId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        using var httpClient = CreateHttpClient(new JsonResponseHandler(HttpStatusCode.OK,
            $$"""
            {"id":"{{userId:D}}","role":"DRIVER","operatorId":"{{OperatorId:D}}"}
            """));
        var client = new IdentityInternalClient(httpClient);

        var result = await client.GetUserAsync(userId);

        result.Found.Should().BeFalse();
        result.FailureStatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task GetOperatorAsync_ReturnsOperatorLookup_WhenIdentityReturnsContractPayload()
    {
        using var httpClient = CreateHttpClient(new JsonResponseHandler(HttpStatusCode.OK,
            $$"""
            {"operatorId":"{{OperatorId:D}}","name":"VietRide Express","registrationStatus":"APPROVED","isActive":true,"code":"OP-001"}
            """));
        var client = new IdentityInternalClient(httpClient);

        var result = await client.GetOperatorAsync(OperatorId);

        result.Found.Should().BeTrue();
        result.Id.Should().Be(OperatorId);
        result.Name.Should().Be("VietRide Express");
    }

    [Fact]
    public async Task GetOperatorAsync_FallsBackToId_WhenOperatorIdMissing()
    {
        using var httpClient = CreateHttpClient(new JsonResponseHandler(HttpStatusCode.OK,
            $$"""
            {"id":"{{OperatorId:D}}","name":"Legacy Operator","registrationStatus":"APPROVED","isActive":true}
            """));
        var client = new IdentityInternalClient(httpClient);

        var result = await client.GetOperatorAsync(OperatorId);

        result.Found.Should().BeTrue();
        result.Id.Should().Be(OperatorId);
        result.Name.Should().Be("Legacy Operator");
    }

    [Fact]
    public async Task GetOperatorAsync_ReturnsValidationError_WhenIdentityOmitsOperatorIdAndId()
    {
        using var httpClient = CreateHttpClient(new JsonResponseHandler(HttpStatusCode.OK,
            $$"""
            {"name":"Broken Operator","registrationStatus":"APPROVED","isActive":true}
            """));
        var client = new IdentityInternalClient(httpClient);

        var result = await client.GetOperatorAsync(OperatorId);

        result.Found.Should().BeFalse();
        result.FailureStatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task GetUserAsync_ReturnsValidationError_WhenIdentityReturnsNotFound()
    {
        using var httpClient = CreateHttpClient(new JsonResponseHandler(HttpStatusCode.NotFound, "{}"));
        var client = new IdentityInternalClient(httpClient);

        var result = await client.GetUserAsync(Guid.NewGuid());

        result.Found.Should().BeFalse();
        result.FailureStatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task InternalJwtHandler_AddsExpectedInternalAuthHeader()
    {
        var captureHandler = new CapturingHandler(HttpStatusCode.OK,
            """
            {"registrationStatus":"APPROVED","isActive":true}
            """);
        var jwtHandler = new InternalJwtDelegatingHandler(
            CreateTokenFactory(),
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            NullLogger<InternalJwtDelegatingHandler>.Instance)
        {
            InnerHandler = captureHandler
        };
        using var httpClient = CreateHttpClient(jwtHandler);
        var client = new IdentityInternalClient(httpClient);

        var result = await client.ValidateOperatorCanWriteAsync(OperatorId);

        result.IsAllowed.Should().BeTrue();
        captureHandler.LastRequest.Should().NotBeNull();
        captureHandler.LastRequest!.RequestUri!.PathAndQuery.Should().Be($"/internal/v1/operators/{OperatorId:D}");
        captureHandler.LastRequest.Headers.TryGetValues("X-Internal-Auth", out var values).Should().BeTrue();
        var header = values!.Single();
        header.Should().StartWith("Bearer ");

        var claims = DecodePayload(header["Bearer ".Length..]);
        claims.GetProperty("iss").GetString().Should().Be("vietride-gateway");
        claims.GetProperty("aud").GetString().Should().Be("vietride-internal");
        claims.GetProperty("sub").GetString().Should().Be("vietride-system");
        claims.GetProperty("role").GetString().Should().Be("SERVICE");
        claims.GetProperty("callerService").GetString().Should().Be("trip");

        var ttlSeconds = claims.GetProperty("exp").GetInt64() - claims.GetProperty("iat").GetInt64();
        ttlSeconds.Should().BeLessThanOrEqualTo(120);
    }

    [Fact]
    public async Task GetUsersAsync_ProjectsPhoneFromAdditiveIdentityBatchField()
    {
        var userId = Guid.NewGuid();
        using var httpClient = CreateHttpClient(new JsonResponseHandler(HttpStatusCode.OK,
            $$"""
            [{"id":"{{userId:D}}","displayName":"Crew Member","phone":"0900000000","role":"DRIVER","operatorId":"{{OperatorId:D}}","status":"ACTIVE"}]
            """));
        var client = new IdentityInternalClient(httpClient);

        var result = await client.GetUsersAsync([userId]);

        result.Should().ContainKey(userId);
        result[userId].Phone.Should().Be("0900000000");
    }

    [Fact]
    public async Task GetUsersAsync_ChunksRequestsAtIdentityContractLimitWithoutDroppingIds()
    {
        var userIds = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray();
        var handler = new BatchCapturingHandler();
        using var httpClient = CreateHttpClient(handler);
        var client = new IdentityInternalClient(httpClient);

        await client.GetUsersAsync(userIds);

        handler.Paths.Should().HaveCount(2);
        handler.Paths.Select(path => path.Split("ids=", StringSplitOptions.None).Length - 1)
            .Should().BeEquivalentTo([100, 1]);
        var requested = handler.Paths
            .SelectMany(path => new Uri("http://identity.local" + path).Query
                .TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => Guid.Parse(part["ids=".Length..])))
            .ToArray();
        requested.Should().BeEquivalentTo(userIds);
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler)
    {
        return new HttpClient(handler)
        {
            BaseAddress = new Uri("http://identity.local")
        };
    }

    private static InternalJwtTokenFactory CreateTokenFactory()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InternalJwt:Secret"] = "0123456789abcdef0123456789abcdef",
            })
            .Build();

        return new InternalJwtTokenFactory(configuration);
    }

    private static JsonElement DecodePayload(string token)
    {
        var payload = token.Split('.')[1];
        var padded = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=')
            .Replace('-', '+')
            .Replace('_', '/');
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private class JsonResponseHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _json;

        public JsonResponseHandler(HttpStatusCode statusCode, string json)
        {
            _statusCode = statusCode;
            _json = json;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class CapturingHandler : JsonResponseHandler
    {
        public CapturingHandler(HttpStatusCode statusCode, string json) : base(statusCode, json)
        {
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Identity is unavailable.");
        }
    }

    private sealed class BatchCapturingHandler : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            });
        }
    }
}
