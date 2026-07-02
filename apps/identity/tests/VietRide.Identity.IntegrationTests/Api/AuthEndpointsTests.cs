using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Features.Auth.GoogleLogin;
using VietRide.Identity.Application.Features.Auth.Login;
using VietRide.Identity.Application.Features.Auth.Logout;
using VietRide.Identity.Application.Features.Auth.Refresh;
using VietRide.Identity.Application.Features.Auth.Register;
using VietRide.Identity.Application.Features.Auth.SetInitialPassword;
using VietRide.Identity.Application.Features.Auth.VerifyEmail;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Infrastructure;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Persistence;
using VietRide.Shared.Persistence.Outbox;
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
public sealed class AuthEndpointsTests :
    IClassFixture<AuthWebApplicationFactory>,
    IClassFixture<AuthEndpointsTests.DbBackedAuthFactory>
{
    private static readonly Guid SystemAdminId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly AuthWebApplicationFactory _factory;
    private readonly DbBackedAuthFactory _dbFactory;

    public AuthEndpointsTests(AuthWebApplicationFactory factory, DbBackedAuthFactory dbFactory)
    {
        _factory = factory;
        _dbFactory = dbFactory;
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
    public async Task PostSetInitialPassword_HappyPath_Returns200EnvelopeWithoutTokens()
    {
        using var client = CreateClientWithSender(new HappyPathAuthSender());

        var response = await client.PostAsJsonAsync("/v1/auth/set-initial-password", new
        {
            token = "initial-password-token",
            password = "StrongPassword123",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        AssertSuccessEnvelope(doc, 200);
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("userId").GetGuid().Should().Be(HappyPathAuthSender.UserId);
        data.GetProperty("status").GetString().Should().Be("ACTIVE");
        data.TryGetProperty("accessToken", out _).Should().BeFalse();
        data.TryGetProperty("refreshToken", out _).Should().BeFalse();
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
    public async Task PostLogin_NonApprovedOperator_Returns403ForbiddenEnvelopeWithoutTokens()
    {
        using var client = CreateClientWithSender(new NonApprovedOperatorLoginAuthSender());

        var response = await client.PostAsJsonAsync("/v1/auth/login", new
        {
            email = "operator@example.com",
            password = "password123",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("statusCode").GetInt32().Should().Be(403);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("FORBIDDEN");
        doc.RootElement.TryGetProperty("data", out _).Should().BeFalse();
        body.Should().NotContain("accessToken");
        body.Should().NotContain("refreshToken");
    }

    [Fact]
    public async Task OperatorSelfRegisterVerifyApproveThenLogin_UsesRealHandlersDbAndReturnsOperatorAdminTokens()
    {
        await _dbFactory.ResetAsync();
        await _dbFactory.SeedSystemAdminAsync(SystemAdminId);
        var email = UniqueEmail("operator-self-register-login");
        const string Password = "Password123!";
        using var client = _dbFactory.CreateClient();

        var registerResponse = await client.PostAsJsonAsync("/v1/operators/register", ValidOperatorRegisterPayload(email, Password));

        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using var registerDoc = JsonDocument.Parse(await registerResponse.Content.ReadAsStringAsync());
        AssertSuccessEnvelope(registerDoc, 201);
        var operatorId = registerDoc.RootElement.GetProperty("data").GetProperty("operatorId").GetGuid();

        var verificationCode = await _dbFactory.GetRegistrationCodeAsync(operatorId);
        // OTP delivery is now via Outbox (identity.otp.requested) — the code is retrieved directly
        // from the persisted EmailVerificationToken, not from a captured email send.
        var verifyResponse = await client.PostAsJsonAsync("/v1/auth/verify-email", new
        {
            email,
            code = verificationCode,
            purpose = EmailVerificationPurpose.REGISTRATION.ToString(),
        });
        verifyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var approveRequest = AuthorizedPost($"/v1/admin/operators/{operatorId}/approve", new { });
        var approveResponse = await client.SendAsync(approveRequest);
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginResponse = await client.PostAsJsonAsync("/v1/auth/login", new
        {
            email,
            password = Password,
        });

        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var loginDoc = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        AssertSuccessEnvelope(loginDoc, 200);
        AssertOperatorAdminLogin(loginDoc, email, operatorId);
    }

    [Fact]
    public async Task AdminCreateSetInitialPasswordThenLogin_UsesRealHandlersDbAndReturnsOperatorAdminTokens()
    {
        await _dbFactory.ResetAsync();
        await _dbFactory.SeedSystemAdminAsync(SystemAdminId);
        var email = UniqueEmail("operator-admin-create-login");
        const string Password = "Password123!";
        using var client = _dbFactory.CreateClient();
        using var createRequest = AuthorizedPost("/v1/admin/operators", ValidAdminCreatePayload(email));

        var createResponse = await client.SendAsync(createRequest);

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using var createDoc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        AssertSuccessEnvelope(createDoc, 201);
        var operatorId = createDoc.RootElement.GetProperty("data").GetProperty("operator").GetProperty("operatorId").GetGuid();
        var adminUserId = createDoc.RootElement.GetProperty("data").GetProperty("adminUser").GetProperty("userId").GetGuid();

        var initialPasswordToken = await _dbFactory.GetSetInitialPasswordTokenAsync(adminUserId);
        _dbFactory.EmailService.SentAccountCreatedLinks.Should().ContainSingle(message =>
            message.To == email
            && message.Info.UserId == adminUserId
            && message.Info.SetInitialPasswordUrl.EndsWith(initialPasswordToken, StringComparison.Ordinal));
        var setPasswordResponse = await client.PostAsJsonAsync("/v1/auth/set-initial-password", new
        {
            token = initialPasswordToken,
            password = Password,
        });
        setPasswordResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginResponse = await client.PostAsJsonAsync("/v1/auth/login", new
        {
            email,
            password = Password,
        });

        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var loginDoc = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        AssertSuccessEnvelope(loginDoc, 200);
        AssertOperatorAdminLogin(loginDoc, email, operatorId);
    }

    [Fact]
    public async Task PostRegister_Passenger_PersistsBothUserCreatedAndOtpRequestedOutboxEventsInSameTransaction()
    {
        await _dbFactory.ResetAsync();
        var email = UniqueEmail("passenger-outbox");
        using var client = _dbFactory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/auth/register", new
        {
            email,
            password = "Password123!",
            displayName = "Passenger User",
            phone = $"09{Random.Shared.Next(10000000, 99999999)}",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var userId = doc.RootElement.GetProperty("data").GetProperty("userId").GetGuid();

        await using var scope = _dbFactory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        // Exactly 2 outbox events: identity.user.created + identity.otp.requested.
        (await db.Set<OutboxEvent>().CountAsync()).Should().Be(2);

        var userCreatedEvent = await db.Set<OutboxEvent>().SingleAsync(x => x.EventType == "identity.user.created");
        userCreatedEvent.Status.Should().Be(OutboxEventStatus.PENDING);
        using var userCreatedPayload = JsonDocument.Parse(userCreatedEvent.Payload);
        userCreatedPayload.RootElement.GetProperty("userId").GetGuid().Should().Be(userId);
        userCreatedPayload.RootElement.GetProperty("role").GetString().Should().Be(UserRole.PASSENGER.ToString());
        userCreatedPayload.RootElement.GetProperty("email").GetString().Should().Be(email);
        userCreatedPayload.RootElement.TryGetProperty("createdAt", out _).Should().BeTrue();
        userCreatedPayload.RootElement.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(["userId", "role", "email", "createdAt"]);

        var otpEvent = await db.Set<OutboxEvent>().SingleAsync(x => x.EventType == "identity.otp.requested");
        otpEvent.Status.Should().Be(OutboxEventStatus.PENDING);
        using var otpPayload = JsonDocument.Parse(otpEvent.Payload);
        otpPayload.RootElement.GetProperty("userId").GetGuid().Should().Be(userId);
        otpPayload.RootElement.GetProperty("email").GetString().Should().Be(email);
        otpPayload.RootElement.GetProperty("purpose").GetString().Should().Be("REGISTRATION");
        otpPayload.RootElement.GetProperty("ttlMinutes").GetInt32().Should().Be(5);
        otpPayload.RootElement.GetProperty("code").GetString().Should().HaveLength(6);

        // The user was committed in the same transaction as both outbox rows.
        (await db.Users.CountAsync(u => u.Id == userId)).Should().Be(1);
    }

    [Fact]
    public async Task PostRegister_OtpIsDeliveredViaOutbox_NotDirectEmailCall()
    {
        await _dbFactory.ResetAsync();
        var email = UniqueEmail("passenger-otp-outbox");
        using var client = _dbFactory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/auth/register", new
        {
            email,
            password = "Password123!",
            displayName = "Outbox OTP User",
            phone = $"09{Random.Shared.Next(10000000, 99999999)}",
        });

        ((int)response.StatusCode).Should().Be(201);
        using var responseDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var userId = responseDoc.RootElement.GetProperty("data").GetProperty("userId").GetGuid();

        await using var scope = _dbFactory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var otpEvent = await db.Set<OutboxEvent>().SingleAsync(x => x.EventType == "identity.otp.requested");
        otpEvent.Status.Should().Be(OutboxEventStatus.PENDING);
        using var otpPayload = JsonDocument.Parse(otpEvent.Payload);
        otpPayload.RootElement.GetProperty("userId").GetGuid().Should().Be(userId);
        otpPayload.RootElement.GetProperty("email").GetString().Should().Be(email);
        otpPayload.RootElement.GetProperty("purpose").GetString().Should().Be("REGISTRATION");
        otpPayload.RootElement.GetProperty("ttlMinutes").GetInt32().Should().Be(5);
        otpPayload.RootElement.GetProperty("code").GetString().Should().HaveLength(6);
    }

    [Fact]
    public async Task PostGoogle_HappyPath_Returns200EnvelopeWithTokens()
    {
        using var client = CreateClientWithSender(new HappyPathAuthSender());

        var response = await client.PostAsJsonAsync("/v1/auth/google", new
        {
            idToken = "google-id-token",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        AssertSuccessEnvelope(doc, 200);
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("accessToken").GetString().Should().Be("google-access-token");
        data.GetProperty("refreshToken").GetString().Should().Be("google-refresh-token");
        data.GetProperty("user").GetProperty("email").GetString().Should().Be("google.user@example.com");
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
        using var client = CreateClientWithSender(new InvalidPhoneRegisterAuthSender());

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
    // Google login — handler errors
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostGoogle_InvalidToken_Returns401AuthGoogleTokenInvalid()
    {
        using var client = CreateClientWithSender(new InvalidGoogleTokenAuthSender());

        var response = await client.PostAsJsonAsync("/v1/auth/google", new
        {
            idToken = "invalid-google-id-token",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("statusCode").GetInt32().Should().Be(401);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("AUTH_GOOGLE_TOKEN_INVALID");
    }

    // -------------------------------------------------------------------------
    // SetInitialPassword — token errors are 400 per Day-5 Q1
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PostSetInitialPassword_InvalidToken_Returns400AuthInitialPasswordTokenInvalid()
    {
        using var client = CreateClientWithSender(new SetInitialPasswordErrorAuthSender(
            "invalid-token",
            "AUTH_INITIAL_PASSWORD_TOKEN_INVALID"));

        var response = await client.PostAsJsonAsync("/v1/auth/set-initial-password", new
        {
            token = "invalid-token",
            password = "StrongPassword123",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("statusCode").GetInt32().Should().Be(400);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("AUTH_INITIAL_PASSWORD_TOKEN_INVALID");
    }

    [Fact]
    public async Task PostSetInitialPassword_BlankPassword_Returns422ValidationError()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/auth/set-initial-password", new
        {
            token = "initial-password-token",
            password = " ",
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("statusCode").GetInt32().Should().Be(422);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("VALIDATION_ERROR");
    }

    [Theory]
    [InlineData("OnlyLetters")]
    [InlineData("12345678")]
    public async Task PostSetInitialPassword_WeakPassword_Returns422ValidationError(string password)
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/auth/set-initial-password", new
        {
            token = "initial-password-token",
            password,
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("statusCode").GetInt32().Should().Be(422);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task PostSetInitialPassword_ExpiredToken_Returns400AuthInitialPasswordTokenExpired()
    {
        using var client = CreateClientWithSender(new SetInitialPasswordErrorAuthSender(
            "expired-token",
            "AUTH_INITIAL_PASSWORD_TOKEN_EXPIRED"));

        var response = await client.PostAsJsonAsync("/v1/auth/set-initial-password", new
        {
            token = "expired-token",
            password = "StrongPassword123",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("statusCode").GetInt32().Should().Be(400);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("AUTH_INITIAL_PASSWORD_TOKEN_EXPIRED");
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

    private static object ValidOperatorRegisterPayload(string email, string password)
        => new
        {
            name = "Operator Co",
            contactEmail = email,
            contactPhone = "+84901234567",
            businessRegistrationNumber = $"BRN-{Guid.NewGuid():N}",
            taxCode = $"TAX-{Guid.NewGuid():N}",
            addressStreet = "1 Street",
            addressWard = "Ward",
            addressDistrict = "District",
            addressProvince = "Province",
            representativeName = "Operator Admin",
            representativePhone = "+84901234568",
            password,
        };

    private static object ValidAdminCreatePayload(string email)
        => new
        {
            name = "Operator Co",
            contactEmail = email,
            contactPhone = "+84901234567",
            businessRegistrationNumber = $"BRN-{Guid.NewGuid():N}",
            taxCode = $"TAX-{Guid.NewGuid():N}",
            addressStreet = "1 Street",
            addressWard = "Ward",
            addressDistrict = "District",
            addressProvince = "Province",
            representativeName = "Operator Admin",
            representativePhone = "+84901234568",
        };

    private static HttpRequestMessage AuthorizedPost(string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation(
            "X-Internal-Auth",
            $"Bearer {CreateInternalJwt(SystemAdminId, UserRole.SYSTEM_ADMIN.ToString())}");

        return request;
    }

    private static string UniqueEmail(string prefix)
        => $"{prefix}-{Guid.NewGuid():N}@example.com";

    private static void AssertOperatorAdminLogin(JsonDocument doc, string email, Guid operatorId)
    {
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
        data.GetProperty("refreshToken").GetString().Should().NotBeNullOrWhiteSpace();
        var user = data.GetProperty("user");
        user.GetProperty("email").GetString().Should().Be(email);
        user.GetProperty("role").GetString().Should().Be(UserRole.OPERATOR_ADMIN.ToString());
        user.GetProperty("operatorId").GetGuid().Should().Be(operatorId);
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
        => CreateInternalJwt("integration-test", role: null);

    private static string CreateInternalJwt(Guid userId, string role)
        => CreateInternalJwt(userId.ToString(), role);

    private static string CreateInternalJwt(string subject, string? role)
    {
        var now = DateTime.UtcNow;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthWebApplicationFactory.InternalJwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
        };
        if (role is not null)
        {
            claims.Add(new Claim("role", role));
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var token = new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims: claims,
            notBefore: now.AddSeconds(-5),
            expires: now.AddSeconds(120),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public sealed class DbBackedAuthFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString = BuildTestDatabaseConnectionString();
        private readonly string _databaseName;
        private bool _databaseCreated;
        private bool _initialized;

        public DbBackedAuthFactory()
        {
            _databaseName = new NpgsqlConnectionStringBuilder(_connectionString).Database!;
        }

        public CapturingEmailService EmailService { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Environment.SetEnvironmentVariable("INTERNAL_JWT_SECRET", AuthWebApplicationFactory.InternalJwtSecret);
            builder.UseEnvironment("Testing");
            builder.UseSetting("INTERNAL_JWT_SECRET", AuthWebApplicationFactory.InternalJwtSecret);
            builder.UseSetting("ConnectionStrings:Default", _connectionString);
            builder.UseSetting("REDIS_URL", "localhost:6379,abortConnect=false");
            builder.UseSetting("IdentityJwt:Kid", "test-kid");
            builder.UseSetting("IdentityJwt:PrivateKey", AuthWebApplicationFactory.DevPrivateKeyPem);

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<NpgsqlDataSource>();
                services.RemoveAll<DbContextOptions<IdentityDbContext>>();
                services.RemoveAll<IdentityDbContext>();
                services.RemoveAll<VietRideDbContextBase>();
                services.RemoveAll<IEmailService>();
                services.RemoveAll<ILoginLockoutCounter>();

                services.AddSingleton(_ =>
                {
                    var dataSourceBuilder = new NpgsqlDataSourceBuilder(_connectionString);
                    IdentityDbContext.ConfigurePostgresEnums(dataSourceBuilder);
                    // Map the shared outbox enum (normally wired by AddVietRideDbContext) so
                    // outbox_events INSERTs from the Register handler can serialize the status.
                    dataSourceBuilder.MapEnum<OutboxEventStatus>(
                        "outbox_event_status",
                        new Npgsql.NameTranslation.NpgsqlNullNameTranslator());
                    return dataSourceBuilder.Build();
                });

                services.AddDbContext<IdentityDbContext>((sp, options) =>
                {
                    options
                        .UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>())
                        .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
                });
                services.AddScoped<VietRideDbContextBase>(sp => sp.GetRequiredService<IdentityDbContext>());
                services.AddSingleton<IEmailService>(EmailService);
                services.AddSingleton<ILoginLockoutCounter, NoOpLoginLockoutCounter>();
            });
        }

        public async Task ResetAsync()
        {
            await InitializeAsync();
            EmailService.SentAccountCreatedLinks.Clear();

            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "TRUNCATE TABLE vietride_identity.activity_logs, vietride_identity.email_verification_tokens, vietride_identity.refresh_tokens, vietride_identity.operator_subscriptions, vietride_identity.users, vietride_identity.operators, vietride_identity.outbox_events RESTART IDENTITY CASCADE;");
        }

        public async Task SeedSystemAdminAsync(Guid userId)
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var systemAdmin = User.CreateAdminPendingPassword("system-admin@example.com", "System Admin");
            SetPrivateProperty(systemAdmin, nameof(User.Id), userId);
            await db.Users.AddAsync(systemAdmin);
            await db.SaveChangesAsync();
        }

        public async Task<string> GetRegistrationCodeAsync(Guid operatorId)
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            return await (
                from token in db.EmailVerificationTokens
                join user in db.Users on token.UserId equals user.Id
                where token.Purpose == EmailVerificationPurpose.REGISTRATION && user.OperatorId == operatorId
                select token.Code)
                .SingleAsync();
        }

        public async Task<string> GetSetInitialPasswordTokenAsync(Guid userId)
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            return await db.EmailVerificationTokens
                .Where(token => token.UserId == userId && token.Purpose == EmailVerificationPurpose.SET_INITIAL_PASSWORD)
                .Select(token => token.Code)
                .SingleAsync();
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await DropDatabaseAsync();
        }

        private async Task InitializeAsync()
        {
            if (_initialized)
            {
                return;
            }

            await CreateDatabaseAsync();

            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Database.MigrateAsync();
            await ReloadPostgresTypesAsync();
            _initialized = true;
        }

        private async Task DropDatabaseAsync()
        {
            if (!_databaseCreated)
            {
                return;
            }

            await using var connection = new NpgsqlConnection(BuildMaintenanceConnectionString());
            await connection.OpenAsync();
            await using var terminateCommand = connection.CreateCommand();
            terminateCommand.CommandText =
                "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @databaseName AND pid <> pg_backend_pid();";
            terminateCommand.Parameters.AddWithValue("databaseName", _databaseName);
            await terminateCommand.ExecuteNonQueryAsync();

            await using var dropCommand = connection.CreateCommand();
            dropCommand.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\"";
            await dropCommand.ExecuteNonQueryAsync();
            _databaseCreated = false;
        }

        private async Task CreateDatabaseAsync()
        {
            if (_databaseCreated)
            {
                return;
            }

            await using var connection = new NpgsqlConnection(BuildMaintenanceConnectionString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{_databaseName}\"";
            await command.ExecuteNonQueryAsync();
            _databaseCreated = true;
        }

        private async Task ReloadPostgresTypesAsync()
        {
            var dataSource = Services.GetRequiredService<NpgsqlDataSource>();
            await using var connection = await dataSource.OpenConnectionAsync();
            await connection.ReloadTypesAsync();
        }

        private string BuildMaintenanceConnectionString()
        {
            var builder = new NpgsqlConnectionStringBuilder(_connectionString)
            {
                Database = "postgres",
            };

            return builder.ConnectionString;
        }

        private static string BuildTestDatabaseConnectionString()
        {
            var configured = Environment.GetEnvironmentVariable("VIETRIDE_IDENTITY_TEST_CONNECTION_STRING")
                ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                ?? "Host=localhost;Port=5432;Database=vietride_identity_tests;Username=vietride;Password=vietride_dev";
            var builder = new NpgsqlConnectionStringBuilder(configured)
            {
                Database = $"vietride_identity_task6_2b_{Guid.NewGuid():N}",
            };

            return builder.ConnectionString;
        }

        private static void SetPrivateProperty<T>(object entity, string propertyName, T value)
        {
            var type = entity.GetType();
            while (type is not null)
            {
                var property = type.GetProperty(
                    propertyName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property is not null)
                {
                    property.SetValue(entity, value);
                    return;
                }

                type = type.BaseType;
            }

            throw new InvalidOperationException($"Property {propertyName} was not found on {entity.GetType().Name}.");
        }
    }

    private sealed class NoOpLoginLockoutCounter : ILoginLockoutCounter
    {
        public Task<long> IncrementAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(1L);

        public Task ResetAsync(Guid userId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    public sealed class CapturingEmailService : IEmailService
    {
        public List<(string To, AccountCreatedEmailDto Info)> SentAccountCreatedLinks { get; } = [];

        public Task SendAccountCreatedLinkAsync(
            string to,
            AccountCreatedEmailDto accountInfo,
            CancellationToken ct = default)
        {
            SentAccountCreatedLinks.Add((to, accountInfo));
            return Task.CompletedTask;
        }

        public Task SendParcelDeliveryLinkAsync(
            string to,
            string deliveryToken,
            ParcelDeliveryEmailDto parcelInfo,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}

internal sealed class HappyPathAuthSender : ISender
{
    internal static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

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
            SetInitialPasswordCommand => new SetInitialPasswordResponseDto(UserId, "ACTIVE"),
            LoginCommand command => CreateTokenBundle(command.Email, "access-token", "refresh-token"),
            GoogleLoginCommand command when command.IdToken == "google-id-token" => CreateTokenBundle(
                "google.user@example.com",
                "google-access-token",
                "google-refresh-token"),
            GoogleLoginCommand command => throw new InvalidOperationException(
                $"Unexpected Google ID token '{command.IdToken}' for happy-path test sender."),
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
                "+84901234567",
                "Test User",
                "PASSENGER",
                null,
                "ACTIVE"));
}

internal sealed class InvalidPhoneRegisterAuthSender : ISender
{
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        if (request is RegisterCommand command)
        {
            ThrowInvalidPhone(command);
        }

        throw new InvalidOperationException($"Unexpected request type {request.GetType().Name}.");
    }

    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        if (request is RegisterCommand command)
        {
            ThrowInvalidPhone(command);
        }

        throw new InvalidOperationException($"Unexpected request type {request.GetType().Name}.");
    }

    private static void ThrowInvalidPhone(RegisterCommand command)
    {
        if (command.Phone != "not-a-phone")
        {
            throw new InvalidOperationException(
                $"Unexpected register phone '{command.Phone}' for invalid-phone test sender.");
        }

        throw new BadRequestException("AUTH_PHONE_INVALID_FORMAT", "Invalid phone number format.");
    }

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Auth endpoint tests do not use streaming MediatR requests.");

    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Auth endpoint tests do not use streaming MediatR requests.");
}

internal sealed class NonApprovedOperatorLoginAuthSender : ISender
{
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        if (request is LoginCommand command)
        {
            ThrowForbidden(command);
        }

        throw new InvalidOperationException($"Unexpected request type {request.GetType().Name}.");
    }

    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        if (request is LoginCommand command)
        {
            ThrowForbidden(command);
        }

        throw new InvalidOperationException($"Unexpected request type {request.GetType().Name}.");
    }

    private static void ThrowForbidden(LoginCommand command)
    {
        if (command.Email != "operator@example.com")
        {
            throw new InvalidOperationException(
                $"Unexpected login email '{command.Email}' for non-approved operator test sender.");
        }

        throw new ForbiddenException("FORBIDDEN", "Operator registration is not approved.");
    }

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Auth endpoint tests do not use streaming MediatR requests.");

    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Auth endpoint tests do not use streaming MediatR requests.");
}

internal sealed class SetInitialPasswordErrorAuthSender : ISender
{
    private readonly string _expectedToken;
    private readonly string _errorCode;

    public SetInitialPasswordErrorAuthSender(string expectedToken, string errorCode)
    {
        _expectedToken = expectedToken;
        _errorCode = errorCode;
    }

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        if (request is SetInitialPasswordCommand command)
        {
            ThrowTokenError(command);
        }

        throw new InvalidOperationException($"Unexpected request type {request.GetType().Name}.");
    }

    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        if (request is SetInitialPasswordCommand command)
        {
            ThrowTokenError(command);
        }

        throw new InvalidOperationException($"Unexpected request type {request.GetType().Name}.");
    }

    private void ThrowTokenError(SetInitialPasswordCommand command)
    {
        if (command.Token != _expectedToken)
        {
            throw new InvalidOperationException(
                $"Unexpected set-initial-password token '{command.Token}' for token-error test sender.");
        }

        throw new BadRequestException(_errorCode, "Initial password token error.");
    }

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Auth endpoint tests do not use streaming MediatR requests.");

    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Auth endpoint tests do not use streaming MediatR requests.");
}

internal sealed class InvalidGoogleTokenAuthSender : ISender
{
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        if (request is GoogleLoginCommand command)
        {
            ThrowIfInvalidGoogleToken(command);
        }

        throw new InvalidOperationException($"Unexpected request type {request.GetType().Name}.");
    }

    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
    {
        if (request is GoogleLoginCommand command)
        {
            ThrowIfInvalidGoogleToken(command);
        }

        throw new InvalidOperationException($"Unexpected request type {request.GetType().Name}.");
    }

    private static void ThrowIfInvalidGoogleToken(GoogleLoginCommand command)
    {
        if (command.IdToken != "invalid-google-id-token")
        {
            throw new InvalidOperationException(
                $"Unexpected Google ID token '{command.IdToken}' for invalid-token test sender.");
        }

        throw new UnauthorizedException(
            "AUTH_GOOGLE_TOKEN_INVALID",
            "Google ID token signature/expiry/audience invalid.");
    }

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Auth endpoint tests do not use streaming MediatR requests.");

    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Auth endpoint tests do not use streaming MediatR requests.");
}

public sealed class AuthWebApplicationFactory : WebApplicationFactory<Program>
{
    internal const string InternalJwtSecret = "test-secret-at-least-32-chars-long-xxxxx";

    // Dev-only RSA 2048 private key (PKCS#8 PEM) — same as appsettings.Development.json placeholder.
    internal const string DevPrivateKeyPem =
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
