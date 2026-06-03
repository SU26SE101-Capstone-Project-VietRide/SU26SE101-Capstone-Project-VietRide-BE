using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using VietRide.Identity.Application.Features.Auth.Login;
using VietRide.Identity.Application.Features.Auth.Logout;
using VietRide.Identity.Application.Features.Auth.Refresh;
using VietRide.Identity.Application.Features.Auth.Register;
using VietRide.Identity.Application.Features.Auth.VerifyEmail;
using VietRide.Shared.Application.UnitOfWork;
using Xunit;

namespace VietRide.Identity.IntegrationTests.Api;

/// <summary>
/// Integration tests for auth endpoints using WebApplicationFactory.
/// These tests exercise controller → MediatR pipeline routing.
/// Full register-verify-login-refresh-logout flow requires a real DB;
/// these tests validate that the endpoints boot, route correctly, and
/// return properly shaped <c>ApiResponse</c> envelope responses (ADR 0004).
/// (End-to-end flow tests are run manually against the dev stack.)
/// </summary>
public sealed class AuthEndpointsTests : IClassFixture<AuthWebApplicationFactory>
{
    private readonly AuthWebApplicationFactory _factory;

    public AuthEndpointsTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // -------------------------------------------------------------------------
    // JWKS endpoint — exempt from envelope per Q-v7.5.1 (RFC 7517 standard format)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetJwks_Returns200_WithRsaKeyShape()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/v1/.well-known/jwks.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        // JWKS is exempt from ApiResponse envelope — must return raw {keys:[...]} shape.
        body.Should().Contain("\"kty\":\"RSA\"");
        body.Should().Contain("\"alg\":\"RS256\"");
        body.Should().Contain("\"use\":\"sig\"");
        body.Should().Contain("\"kid\":");
        body.Should().Contain("\"n\":");
        body.Should().Contain("\"e\":");
        // Must NOT be wrapped in the ApiResponse envelope.
        body.Should().NotContain("\"success\":");
    }

    // -------------------------------------------------------------------------
    // Auth endpoints — happy paths with controller → MediatR routing stubbed
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostRegister_HappyPath_Returns201Envelope()
    {
        using var client = CreateClientWithSender(new HappyPathAuthSender());

        var response = await client.PostAsJsonAsync("/v1/auth/register", new
        {
            email = "user@example.com",
            password = "password123",
            displayName = "Test User",
            phone = "0901234567",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        AssertSuccessEnvelope(doc, 201);
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("email").GetString().Should().Be("user@example.com");
        data.GetProperty("status").GetString().Should().Be("PENDING_EMAIL_VERIFICATION");
        data.GetProperty("otpTtlMinutes").GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task PostVerifyEmail_HappyPath_Returns200Envelope()
    {
        using var client = CreateClientWithSender(new HappyPathAuthSender());

        var response = await client.PostAsJsonAsync("/v1/auth/verify-email", new
        {
            email = "user@example.com",
            code = "123456",
            purpose = "REGISTRATION",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        AssertSuccessEnvelope(doc, 200);
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("status").GetString().Should().Be("ACTIVE");
    }

    [Fact]
    public async Task PostLogin_HappyPath_Returns200EnvelopeWithTokens()
    {
        using var client = CreateClientWithSender(new HappyPathAuthSender());

        var response = await client.PostAsJsonAsync("/v1/auth/login", new
        {
            email = "user@example.com",
            password = "password123",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        AssertSuccessEnvelope(doc, 200);
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("accessToken").GetString().Should().Be("access-token");
        data.GetProperty("refreshToken").GetString().Should().Be("refresh-token");
        data.GetProperty("user").GetProperty("email").GetString().Should().Be("user@example.com");
    }

    [Fact]
    public async Task PostRefresh_HappyPath_Returns200EnvelopeWithRotatedTokens()
    {
        using var client = CreateClientWithSender(new HappyPathAuthSender());

        var response = await client.PostAsJsonAsync("/v1/auth/refresh", new
        {
            refreshToken = "refresh-token",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        AssertSuccessEnvelope(doc, 200);
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("accessToken").GetString().Should().Be("rotated-access-token");
        data.GetProperty("refreshToken").GetString().Should().Be("rotated-refresh-token");
    }

    [Fact]
    public async Task PostLogout_HappyPathWithInternalAuth_Returns204NoContent()
    {
        using var client = CreateClientWithSender(new HappyPathAuthSender());
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/logout")
        {
            Content = JsonContent.Create(new
            {
                refreshToken = "refresh-token",
            }),
        };
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", "Bearer " + CreateInternalJwt());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // Register — validation (no DB required for invalid input rejection)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostRegister_MissingEmail_ReturnsClientError()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/auth/register", new
        {
            password = "password123",
            displayName = "Test User",
            phone = "0901234567",
        });

        // ASP.NET Core 8 returns 400 for missing non-nullable fields before FluentValidation runs;
        // OR FluentValidation returns 422. Both are valid client-error responses (4xx).
        // Response uses ApiResponse envelope (ADR 0004).
        ((int)response.StatusCode).Should().BeInRange(400, 422);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        // Envelope: {success:false, statusCode, error:{code, message}, meta}
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task PostRegister_InvalidPhone_Returns400_WithAuthPhoneInvalidFormat()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/auth/register", new
        {
            email = "user@example.com",
            password = "password123",
            displayName = "Test User",
            phone = "not-a-phone",
        });

        // All fields present but phone format is invalid — domain validation returns 400.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        // Envelope shape (ADR 0004): error.code (not root-level errorCode).
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("statusCode").GetInt32().Should().Be(400);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("AUTH_PHONE_INVALID_FORMAT");
    }

    // -------------------------------------------------------------------------
    // VerifyEmail — validation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostVerifyEmail_MissingCode_ReturnsClientError()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/auth/verify-email", new
        {
            email = "user@example.com",
            purpose = "REGISTRATION",
        });

        // Missing required field — 400 (model binding) or 422 (FluentValidation), both are 4xx.
        // Response uses ApiResponse envelope (ADR 0004).
        ((int)response.StatusCode).Should().BeInRange(400, 422);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("VALIDATION_ERROR");
    }

    // -------------------------------------------------------------------------
    // Login — validation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostLogin_MissingPassword_ReturnsClientError()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/auth/login", new
        {
            email = "user@example.com",
        });

        // Missing required field — 400 (model binding) or 422 (FluentValidation), both are 4xx.
        // Response uses ApiResponse envelope (ADR 0004).
        ((int)response.StatusCode).Should().BeInRange(400, 422);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("VALIDATION_ERROR");
    }

    // -------------------------------------------------------------------------
    // Logout — auth required before validation/handler
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostLogout_WithoutAuth_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/auth/logout", new
        {
            refreshToken = "valid-shape-refresh-token",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostLogout_TamperedInternalAuth_Returns401()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/logout")
        {
            Content = JsonContent.Create(new
            {
                refreshToken = "valid-shape-refresh-token",
            }),
        };
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", "Bearer tampered");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private HttpClient CreateClientWithSender(ISender sender)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISender>();
                services.RemoveAll<IMediator>();
                services.AddSingleton(sender);
            });
        }).CreateClient();
    }

    private static void AssertSuccessEnvelope(JsonDocument doc, int expectedStatusCode)
    {
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("statusCode").GetInt32().Should().Be(expectedStatusCode);
        doc.RootElement.TryGetProperty("data", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("meta", out _).Should().BeTrue();
    }

    private static string CreateInternalJwt()
    {
        var now = DateTime.UtcNow;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthWebApplicationFactory.InternalJwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims: [new Claim(JwtRegisteredClaimNames.Sub, "integration-test")],
            notBefore: now.AddSeconds(-5),
            expires: now.AddSeconds(120),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

internal sealed class HappyPathAuthSender : ISender
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        => Task.FromResult((TResponse)Handle(request));

    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        => Task.FromResult<object?>(Handle(request));

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Auth endpoint tests do not use streaming MediatR requests.");

    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Auth endpoint tests do not use streaming MediatR requests.");

    private static object Handle(object request)
        => request switch
        {
            RegisterCommand command => new RegisterResponseDto(
                UserId,
                command.Email,
                "PENDING_EMAIL_VERIFICATION",
                5),
            VerifyEmailCommand => new VerifyEmailResponseDto(UserId, "ACTIVE"),
            LoginCommand command => CreateTokenBundle(command.Email, "access-token", "refresh-token"),
            RefreshCommand => CreateTokenBundle("user@example.com", "rotated-access-token", "rotated-refresh-token"),
            LogoutCommand => Unit.Value,
            _ => throw new InvalidOperationException($"Unexpected request type {request.GetType().Name}."),
        };

    private static TokenBundleDto CreateTokenBundle(string email, string accessToken, string refreshToken)
        => new(
            accessToken,
            refreshToken,
            900,
            new UserSummaryDto(
                UserId,
                email,
                "Test User",
                "PASSENGER",
                null,
                "ACTIVE"));
}

public sealed class AuthWebApplicationFactory : WebApplicationFactory<Program>
{
    internal const string InternalJwtSecret = "test-secret-at-least-32-chars-long-xxxxx";

    // Dev-only RSA 2048 private key (PKCS#8 PEM) — same as appsettings.Development.json placeholder.
    private const string DevPrivateKeyPem =
        "-----BEGIN PRIVATE KEY-----\n" +
        "MIIEuwIBADANBgkqhkiG9w0BAQEFAASCBKUwggShAgEAAoIBAQC+6Nk4TLBS4Hm3\n" +
        "p3/urqAAa+/eC1o+W4sbvmKEv2mZb9kxnTWwGudixb3bIxTD/5b468eI3cBftXZB\n" +
        "NMkgUBIeqC2KwYXdLE5uiDuhRTBNo21cY9mWRA9UocYiW8zEegoPevj9sbIvWATG\n" +
        "hvLVwkqi4j0UZhEwG7fmXKeJuGZfFGUXjnfKscNTVnV6hxcvtz9Txa9IgZdJyICr\n" +
        "Tk+MGh+qkrnt6iK3gx6NYufY9S+6ZkV0qA9tmLBVWMXAUg/VnNhRcfUbRM1HQLmG\n" +
        "HnC6w1ttMzsc8sbOI8Xt3/EXQDQaJjJfWgvaa1CnQx/AJz9co/qansxHFP37GueL\n" +
        "DAl0Th6BAgMBAAECgf9R7bLtx8z7Cf2PaqQrBIAaOdsITKhiMbiM3gSYIhjGtLle\n" +
        "EWsEkUFeitspmMGiFaU0ucxQh5QS8zXYUZS5Dxgr+KOSxcAtB8r+GNYJ9vjPcBkV\n" +
        "9fJ2le1EKoYAIXycGtrZVYQoct+zt3sPWsNQVhzgPnz38qb5T9SntkowCnLNK7R7\n" +
        "REXlJcRs2pOePukpKmFJwttZEmIkMv1zk1xmj0uAmM/SRr/Vir11d9uvz5UcRGYL\n" +
        "N+ig7qyQw7NSyv71EubDvvPcunM88whZ9oyTCQE7JcQBpq9QQ5+PBtEKLFv9Qddh\n" +
        "D9YT/Ys52hI92zhQNoi9+UTUpRlU6K8To2ZXr2cCgYEA+ASMtmTZU0giIi3zAl2P\n" +
        "5ysLwIXZzTmgPZXttCTfDNpqxaEY0tGLEP06JsPx5Us/bhLRmpQDgXwgMe8J6fi9\n" +
        "jk+n6rfoeTed3VhOAGGcABThzGU325JiCdIMpPMlrGsltisrgj8WJEdZYhtqx8W/\n" +
        "sCg/GWIe3+Qz7ceJSaYtXf8CgYEAxQ3G6IcTlZSdEosqTy/RWdsUDCmqo8EOpEgQ\n" +
        "cReN+Vq8JAwAz0UlABwee6na8dwRAGN4uaDdPf/q9NgTZhm3sBArFV0B/sVOIbNi\n" +
        "hH21136ER7MNTMJIm5TbNs2X9VoaZ94xAFSqrncj1PBsYL1jvvQaZ2h/p7q8kqJ0\n" +
        "nHlNg38CgYEAuWPNOtmPia01to7aQz5kvstycWqcL8ePe/mCQVH+WME7ZpbQ02VG\n" +
        "qmBfA3McceUZeNIgU4eoRzXdavXfV0FTj/kC73ShFVr5aecEB0zvKzBwyDQw2LRH\n" +
        "DEgyo2oNEyDUg6MpVqaJiny615be7o1mh+rNn8+0fG88UdUBTkglSUkCgYArfukC\n" +
        "9p3qDI3HRBSougNZ9DOuo5vY3Ypf1NBcRji+a7rPsh6Toc2TAqHv5gRAErVmAo7p\n" +
        "Woq7Xrv8I53UkaSsJkV8R7VjCSY/5hq+6Ai1cmW8dddftBrWzLq+lA8QxzzA5Jio\n" +
        "XAf4zq+IFzG1ANj9k2AopzZWTa/GJjnbOCNV/QKBgGm41nw1NY1mHvxTZOPwxvqL\n" +
        "ahIYVtjVZqKLkFhpfQ8rDziyjJMenOJrN1bNVFr5rp8qJooYgA7U8PUqFpznyMOy\n" +
        "a8Mnqco+K0o4Y25lSbqJBiE4uob0HBEHuRxtAKJGaT3S6uBQVrshFAnrUE9YttEh\n" +
        "3G9IZTcD9Xf6wKkFCbum\n" +
        "-----END PRIVATE KEY-----";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("INTERNAL_JWT_SECRET", InternalJwtSecret);
        builder.UseSetting("INTERNAL_JWT_SECRET", InternalJwtSecret);
        builder.UseSetting("ConnectionStrings:Default", "Host=localhost;Port=5432;Database=test;Username=postgres;Password=postgres");
        builder.UseSetting("REDIS_URL", "localhost:6379,abortConnect=false");
        // RS256 keypair for tests — dev placeholder only (NOT a production key).
        builder.UseSetting("IdentityJwt:Kid", "test-kid");
        builder.UseSetting("IdentityJwt:PrivateKey", DevPrivateKeyPem);
        builder.UseEnvironment("Testing");

        // Replace EfUnitOfWork with a no-op stub so that TransactionBehavior does not
        // attempt to open a real Postgres connection during tests that exercise
        // non-DB endpoints (e.g. GetJwks). Real DB round-trips require a live Postgres
        // and are explicitly out of the Day-3 test-harness scope.
        builder.ConfigureServices(services =>
        {
            services.AddScoped<IUnitOfWork, NoOpUnitOfWork>();
        });
    }

    /// <summary>
    /// No-op unit-of-work for integration test scenarios that do not touch the DB.
    /// Prevents <see cref="VietRide.Shared.Application.Behaviors.TransactionBehavior{TRequest,TResponse}"/>
    /// from opening a real Postgres connection when exercising non-DB handlers such as JWKS.
    /// </summary>
    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(0);
        public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct) => await operation();
        public Task BeginTransactionAsync(CancellationToken ct) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
