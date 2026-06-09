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
}
