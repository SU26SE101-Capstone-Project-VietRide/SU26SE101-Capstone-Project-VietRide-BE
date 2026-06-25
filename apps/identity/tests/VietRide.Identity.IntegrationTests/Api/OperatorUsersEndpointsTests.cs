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
using VietRide.Identity.Application.Features.Auth.ResendInitialPassword;
using VietRide.Identity.Application.Features.OperatorUsers.CreateOperatorUser;
using VietRide.Identity.Application.Features.OperatorUsers.ListOperatorUsers;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Domain.Exceptions;
using VietRide.Identity.Infrastructure;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence;

namespace VietRide.Identity.IntegrationTests.Api;

public sealed class OperatorUsersEndpointsTests : IClassFixture<AuthWebApplicationFactory>
{
    private static readonly Guid OperatorAdminId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OperatorId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid TargetUserId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid MissingUserId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid WrongStatusUserId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid CrossOperatorUserId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
    private static readonly Guid NonOperatorUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly AuthWebApplicationFactory _factory;

    public OperatorUsersEndpointsTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ResendInitialPassword_Anonymous_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync($"/v1/operator/users/{TargetUserId}/resend-initial-password", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateOperatorUser_HappyPath_Returns201ContractShape()
    {
        using var client = CreateClientWithSender(new OperatorUsersSender());
        using var request = CreateCreateRequest(UserRole.OPERATOR_ADMIN.ToString(), OperatorId);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertSuccessEnvelope(doc, 201);
        var data = doc.RootElement.GetProperty("data");
        data.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            ["userId", "email", "phone", "displayName", "role", "status", "operatorId", "initialPasswordExpiresAt"]);
        data.GetProperty("email").GetString().Should().Be("driver@example.com");
        data.GetProperty("phone").GetString().Should().Be("+84901112222");
        data.GetProperty("role").GetString().Should().Be(UserRole.DRIVER.ToString());
        data.GetProperty("status").GetString().Should().Be(UserStatus.PENDING_INITIAL_PASSWORD.ToString());
        data.GetProperty("operatorId").GetGuid().Should().Be(OperatorId);
    }

    [Fact]
    public async Task ListOperatorUsers_OperatorAdmin_Returns200EnvelopeWithPagedUsers()
    {
        using var client = CreateClientWithSender(new OperatorUsersSender());
        using var request = CreateListRequest(UserRole.OPERATOR_ADMIN.ToString(), OperatorId);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertSuccessEnvelope(doc, 200);
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("page").GetInt32().Should().Be(1);
        data.GetProperty("pageSize").GetInt32().Should().Be(20);
        data.GetProperty("totalItems").GetInt64().Should().Be(1);
        var item = data.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        item.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            ["userId", "email", "phone", "displayName", "role", "status", "operatorId", "createdAt", "avatarUrl"]);
        item.GetProperty("role").GetString().Should().Be(UserRole.DRIVER.ToString());
        item.GetProperty("operatorId").GetGuid().Should().Be(OperatorId);
    }

    [Fact]
    public async Task ListOperatorUsers_SystemAdmin_ReturnsAllOperatorEmployeesOnly()
    {
        using var client = CreateClientWithSender(new OperatorUsersSender());
        using var request = CreateAdminListRequest(UserRole.SYSTEM_ADMIN.ToString(), operatorId: null);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertSuccessEnvelope(doc, 200);
        var items = doc.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToArray();
        items.Should().HaveCount(3);
        items.Select(item => item.GetProperty("role").GetString()).Should().BeEquivalentTo(
            [UserRole.DRIVER.ToString(), UserRole.ASSISTANT.ToString(), UserRole.OPERATOR_STAFF.ToString()]);
        items.Should().OnlyContain(item => item.GetProperty("operatorId").GetGuid() != Guid.Empty);
    }

    [Fact]
    public async Task ListOperatorUsers_SystemAdminOnOperatorRoute_Returns403ForbiddenEnvelope()
    {
        using var client = CreateClientWithSender(new OperatorUsersSender());
        using var request = CreateListRequest(UserRole.SYSTEM_ADMIN.ToString(), operatorId: null);

        var response = await client.SendAsync(request);

        await AssertErrorCode(response, HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task ListOperatorUsers_OperatorAdminOnAdminRoute_Returns403ForbiddenEnvelope()
    {
        using var client = CreateClientWithSender(new OperatorUsersSender());
        using var request = CreateAdminListRequest(UserRole.OPERATOR_ADMIN.ToString(), OperatorId);

        var response = await client.SendAsync(request);

        await AssertErrorCode(response, HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task CreateOperatorUser_MissingCallerOperatorId_Returns403ForbiddenEnvelope()
    {
        using var client = CreateClientWithSender(new OperatorUsersSender());
        using var request = CreateCreateRequest(UserRole.OPERATOR_ADMIN.ToString(), null);

        var response = await client.SendAsync(request);

        await AssertErrorCode(response, HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task CreateOperatorUser_InvalidRole_Returns422ValidationErrorEnvelope()
    {
        using var client = _factory.CreateClient();
        using var request = CreateCreateRequest(
            UserRole.OPERATOR_ADMIN.ToString(),
            OperatorId,
            UniqueEmail("invalid-role"),
            "+84908880000",
            "Invalid Role",
            UserRole.OPERATOR_ADMIN.ToString());

        var response = await client.SendAsync(request);

        await AssertErrorCode(response, HttpStatusCode.UnprocessableEntity, "VALIDATION_ERROR");
    }

    [Theory]
    [InlineData(UserRole.DRIVER, nameof(OperatorSubscription.CurrentDrivers))]
    [InlineData(UserRole.ASSISTANT, nameof(OperatorSubscription.CurrentAssistants))]
    [InlineData(UserRole.OPERATOR_STAFF, nameof(OperatorSubscription.CurrentOperatorUsers))]
    public async Task CreateOperatorUser_RealMediatRDbPath_PersistsUserTokenActivityLogEmail_AndIncrementsRoleCounter(
        UserRole role,
        string counterPropertyName)
    {
        var dbFactory = new DbBackedOperatorUsersFactory();
        var targetEmail = UniqueEmail(role.ToString().ToLowerInvariant());
        var targetPhone = role switch
        {
            UserRole.DRIVER => "+84907770001",
            UserRole.ASSISTANT => "+84907770002",
            UserRole.OPERATOR_STAFF => "+84907770003",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
        const string Password = "Password123!";

        try
        {
            await dbFactory.InitializeAsync();
            await dbFactory.SeedCreateOperatorUserAsync(OperatorId, OperatorAdminId);
            using var client = dbFactory.CreateClient();
            using var request = CreateCreateRequest(
                UserRole.OPERATOR_ADMIN.ToString(),
                OperatorId,
                targetEmail,
                targetPhone,
                $"{role} One",
                role.ToString());

            var response = await client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            AssertSuccessEnvelope(doc, 201);
            var data = doc.RootElement.GetProperty("data");
            var userId = data.GetProperty("userId").GetGuid();
            data.GetProperty("role").GetString().Should().Be(role.ToString());
            data.GetProperty("operatorId").GetGuid().Should().Be(OperatorId);

            string initialPasswordToken;
            await using (var scope = dbFactory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var user = await db.Users.SingleAsync(u => u.Id == userId);
                user.Email.Should().Be(targetEmail);
                user.Phone!.Value.Value.Should().Be(targetPhone);
                user.Role.Should().Be(role);
                user.Status.Should().Be(UserStatus.PENDING_INITIAL_PASSWORD);
                user.OperatorId.Should().Be(OperatorId);
                user.PasswordHash.Should().BeNull();

                var token = await db.EmailVerificationTokens.SingleAsync(token =>
                    token.UserId == userId && token.Purpose == EmailVerificationPurpose.SET_INITIAL_PASSWORD);
                initialPasswordToken = token.Code;
                token.UsedAt.Should().BeNull();
                var activityLog = await db.ActivityLogs.SingleAsync(log =>
                    log.UserId == OperatorAdminId && log.Action == ActivityLogAction.SET_INITIAL_PASSWORD);
                using var metadata = JsonDocument.Parse(activityLog.Metadata!);
                metadata.RootElement.GetProperty("actorUserId").GetGuid().Should().Be(OperatorAdminId);
                metadata.RootElement.GetProperty("targetUserId").GetGuid().Should().Be(userId);
                metadata.RootElement.GetProperty("operatorId").GetGuid().Should().Be(OperatorId);
                metadata.RootElement.GetProperty("source").GetString().Should().Be("OPERATOR_USER_CREATE");

                var subscription = await db.OperatorSubscriptions.SingleAsync(s => s.OperatorId == OperatorId);
                GetCounter(subscription, counterPropertyName).Should().Be(role == UserRole.OPERATOR_STAFF ? 2 : 1);
                dbFactory.EmailService.SentAccountCreatedLinks.Should().ContainSingle(sent => sent.To == targetEmail);
            }

            if (role == UserRole.OPERATOR_STAFF)
            {
                var setPasswordResponse = await client.PostAsJsonAsync("/v1/auth/set-initial-password", new
                {
                    token = initialPasswordToken,
                    password = Password,
                });
                setPasswordResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                using var setPasswordDoc = JsonDocument.Parse(await setPasswordResponse.Content.ReadAsStringAsync());
                AssertSuccessEnvelope(setPasswordDoc, 200);
                setPasswordDoc.RootElement.GetProperty("data").GetProperty("userId").GetGuid().Should().Be(userId);
                setPasswordDoc.RootElement.GetProperty("data").GetProperty("status").GetString()
                    .Should().Be(UserStatus.ACTIVE.ToString());

                await using (var scope = dbFactory.Services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                    var user = await db.Users.SingleAsync(u => u.Id == userId);
                    user.Status.Should().Be(UserStatus.ACTIVE);
                    user.PasswordHash.Should().NotBeNullOrWhiteSpace();
                    var token = await db.EmailVerificationTokens.SingleAsync(token => token.Code == initialPasswordToken);
                    token.UsedAt.Should().NotBeNull();
                }

                var loginResponse = await client.PostAsJsonAsync("/v1/auth/login", new
                {
                    email = targetEmail,
                    password = Password,
                });

                loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                using var loginDoc = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
                AssertSuccessEnvelope(loginDoc, 200);
                AssertOperatorUserLogin(loginDoc, userId, targetEmail, role, OperatorId);
            }
        }
        finally
        {
            dbFactory.Dispose();
            await dbFactory.DropDatabaseAsync();
        }
    }

    [Fact]
    public async Task CreateOperatorUser_ConcurrentDriverCreates_WhenOneSlotRemains_AllowsOnlyOneAndDoesNotOverIncrement()
    {
        var dbFactory = new DbBackedOperatorUsersFactory();

        try
        {
            await dbFactory.InitializeAsync();
            await dbFactory.SeedCreateOperatorUserAsync(OperatorId, OperatorAdminId, currentDrivers: 4);
            using var client = dbFactory.CreateClient();
            using var firstRequest = CreateCreateRequest(
                UserRole.OPERATOR_ADMIN.ToString(),
                OperatorId,
                UniqueEmail("concurrent-driver-a"),
                "+84906660001",
                "Concurrent Driver A",
                UserRole.DRIVER.ToString());
            using var secondRequest = CreateCreateRequest(
                UserRole.OPERATOR_ADMIN.ToString(),
                OperatorId,
                UniqueEmail("concurrent-driver-b"),
                "+84906660002",
                "Concurrent Driver B",
                UserRole.DRIVER.ToString());

            var responses = await Task.WhenAll(client.SendAsync(firstRequest), client.SendAsync(secondRequest));

            responses.Should().ContainSingle(response => response.StatusCode == HttpStatusCode.Created);
            var failed = responses.Should().ContainSingle(response => response.StatusCode == HttpStatusCode.UnprocessableEntity).Subject;
            await AssertErrorCode(failed, HttpStatusCode.UnprocessableEntity, "SUBSCRIPTION_LIMIT_EXCEEDED");

            await using var scope = dbFactory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var subscription = await db.OperatorSubscriptions.SingleAsync(s => s.OperatorId == OperatorId);
            subscription.CurrentDrivers.Should().Be(5);
            var driverCount = await db.Users.CountAsync(u => u.OperatorId == OperatorId && u.Role == UserRole.DRIVER);
            driverCount.Should().Be(1);
            var tokenCount = await db.EmailVerificationTokens.CountAsync();
            tokenCount.Should().Be(1);
            var activityLogCount = await db.ActivityLogs.CountAsync(log => log.Action == ActivityLogAction.SET_INITIAL_PASSWORD);
            activityLogCount.Should().Be(1);
        }
        finally
        {
            dbFactory.Dispose();
            await dbFactory.DropDatabaseAsync();
        }
    }

    [Fact]
    public async Task ResendInitialPassword_HappyPath_UsesRealHandlerDbTransaction_AndPersistsTokenAndActivityLog()
    {
        var dbFactory = new DbBackedOperatorUsersFactory();
        var targetEmail = UniqueEmail("resend-target");
        const string oldCode = "old-resend-initial-password-token";

        try
        {
            await dbFactory.InitializeAsync();
            await dbFactory.SeedResendHappyPathAsync(OperatorId, TargetUserId, targetEmail, oldCode);

            using var client = dbFactory.CreateClient();
            using var request = CreateRequest(TargetUserId, UserRole.OPERATOR_ADMIN.ToString(), OperatorId);
            var beforeSend = DateTimeOffset.UtcNow;

            var response = await client.SendAsync(request);

            var afterSend = DateTimeOffset.UtcNow;
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            AssertSuccessEnvelope(doc, 200);
            var data = doc.RootElement.GetProperty("data");
            data.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
                ["userId", "status", "expiresAt"]);
            data.GetProperty("userId").GetGuid().Should().Be(TargetUserId);
            data.GetProperty("status").GetString().Should().Be(UserStatus.PENDING_INITIAL_PASSWORD.ToString());

            await using var scope = dbFactory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var tokens = await db.EmailVerificationTokens
                .Where(token => token.UserId == TargetUserId && token.Purpose == EmailVerificationPurpose.SET_INITIAL_PASSWORD)
                .OrderBy(token => token.CreatedAt)
                .ToListAsync();

            tokens.Should().HaveCount(2);
            tokens.Should().ContainSingle(token => token.Code == oldCode).Which.UsedAt.Should().NotBeNull();
            var freshToken = tokens.Should().ContainSingle(token => token.UsedAt == null).Subject;
            freshToken.Code.Should().NotBe(oldCode);
            freshToken.ExpiresAt.Should().BeOnOrAfter(beforeSend.AddHours(48).AddMinutes(-1));
            freshToken.ExpiresAt.Should().BeOnOrBefore(afterSend.AddHours(48).AddMinutes(1));

            var activityLog = await db.ActivityLogs.SingleAsync(log =>
                log.UserId == OperatorAdminId && log.Action == ActivityLogAction.RESEND_INITIAL_PASSWORD);
            using var metadata = JsonDocument.Parse(activityLog.Metadata!);
            metadata.RootElement.GetProperty("operatorId").GetGuid().Should().Be(OperatorId);
            metadata.RootElement.GetProperty("actorUserId").GetGuid().Should().Be(OperatorAdminId);
            metadata.RootElement.GetProperty("callerUserId").GetGuid().Should().Be(OperatorAdminId);
            metadata.RootElement.GetProperty("targetUserId").GetGuid().Should().Be(TargetUserId);
            metadata.RootElement.GetProperty("source").GetString().Should().Be("RESEND_INITIAL_PASSWORD");

            dbFactory.EmailService.SentAccountCreatedLinks.Should().ContainSingle();
            var sentEmail = dbFactory.EmailService.SentAccountCreatedLinks.Single();
            sentEmail.To.Should().Be(targetEmail);
            sentEmail.Info.UserId.Should().Be(TargetUserId);
            sentEmail.Info.SetInitialPasswordUrl.Should().EndWith(freshToken.Code);
            sentEmail.Info.ExpiresAt.Should().BeCloseTo(freshToken.ExpiresAt, TimeSpan.FromMilliseconds(1));
        }
        finally
        {
            dbFactory.Dispose();
            await dbFactory.DropDatabaseAsync();
        }
    }

    [Theory]
    [InlineData(OperatorRegistrationStatus.SUSPENDED)]
    [InlineData(OperatorRegistrationStatus.REJECTED)]
    public async Task ResendInitialPassword_NonApprovedOperator_Returns403AndDoesNotRevokeCreateEmailOrLogSideEffects(
        OperatorRegistrationStatus status)
    {
        var dbFactory = new DbBackedOperatorUsersFactory();
        var targetEmail = UniqueEmail($"resend-{status.ToString().ToLowerInvariant()}");
        const string oldCode = "old-non-approved-resend-token";

        try
        {
            await dbFactory.InitializeAsync();
            await dbFactory.SeedResendHappyPathAsync(OperatorId, TargetUserId, targetEmail, oldCode, status);

            using var client = dbFactory.CreateClient();
            using var request = CreateRequest(TargetUserId, UserRole.OPERATOR_ADMIN.ToString(), OperatorId);

            var response = await client.SendAsync(request);

            await AssertErrorCode(response, HttpStatusCode.Forbidden, "FORBIDDEN");
            await using var scope = dbFactory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var token = await db.EmailVerificationTokens.SingleAsync(token =>
                token.UserId == TargetUserId && token.Purpose == EmailVerificationPurpose.SET_INITIAL_PASSWORD);
            token.Code.Should().Be(oldCode);
            token.UsedAt.Should().BeNull();
            var activityLogCount = await db.ActivityLogs.CountAsync(log => log.Action == ActivityLogAction.RESEND_INITIAL_PASSWORD);
            activityLogCount.Should().Be(0);
            dbFactory.EmailService.SentAccountCreatedLinks.Should().BeEmpty();
        }
        finally
        {
            dbFactory.Dispose();
            await dbFactory.DropDatabaseAsync();
        }
    }

    [Fact]
    public async Task ResendInitialPassword_AuthenticatedWithoutRoleClaim_Returns403ForbiddenEnvelope()
    {
        var dbFactory = new DbBackedOperatorUsersFactory();

        try
        {
            await dbFactory.InitializeAsync();
            using var client = dbFactory.CreateClient();
            using var request = CreateRequest(TargetUserId, role: null, OperatorId);

            var response = await client.SendAsync(request);

            await AssertErrorCode(response, HttpStatusCode.Forbidden, "FORBIDDEN");
        }
        finally
        {
            dbFactory.Dispose();
            await dbFactory.DropDatabaseAsync();
        }
    }

    [Fact]
    public async Task ResendInitialPassword_WrongRole_Returns403ForbiddenEnvelope()
    {
        using var client = CreateClientWithSender(new OperatorUsersSender());
        using var request = CreateRequest(TargetUserId, UserRole.OPERATOR_STAFF.ToString(), OperatorId);

        var response = await client.SendAsync(request);

        await AssertErrorCode(response, HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task ResendInitialPassword_MissingCallerOperatorId_Returns403ForbiddenEnvelope()
    {
        using var client = CreateClientWithSender(new OperatorUsersSender());
        using var request = CreateRequest(TargetUserId, UserRole.OPERATOR_ADMIN.ToString(), null);

        var response = await client.SendAsync(request);

        await AssertErrorCode(response, HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task ResendInitialPassword_CrossOperatorTarget_Returns403ForbiddenEnvelope()
    {
        using var client = CreateClientWithSender(new OperatorUsersSender());
        using var request = CreateRequest(CrossOperatorUserId, UserRole.OPERATOR_ADMIN.ToString(), OperatorId);

        var response = await client.SendAsync(request);

        await AssertErrorCode(response, HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task ResendInitialPassword_NonOperatorTarget_Returns403ForbiddenEnvelope()
    {
        using var client = CreateClientWithSender(new OperatorUsersSender());
        using var request = CreateRequest(NonOperatorUserId, UserRole.OPERATOR_ADMIN.ToString(), OperatorId);

        var response = await client.SendAsync(request);

        await AssertErrorCode(response, HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task ResendInitialPassword_TargetNotFound_Returns404ResourceNotFoundEnvelope()
    {
        using var client = CreateClientWithSender(new OperatorUsersSender());
        using var request = CreateRequest(MissingUserId, UserRole.OPERATOR_ADMIN.ToString(), OperatorId);

        var response = await client.SendAsync(request);

        await AssertErrorCode(response, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");
    }

    [Fact]
    public async Task ResendInitialPassword_TargetWrongStatus_Returns422InvalidStatusTransitionEnvelope()
    {
        using var client = CreateClientWithSender(new OperatorUsersSender());
        using var request = CreateRequest(WrongStatusUserId, UserRole.OPERATOR_ADMIN.ToString(), OperatorId);

        var response = await client.SendAsync(request);

        await AssertErrorCode(response, HttpStatusCode.UnprocessableEntity, "USER_INVALID_STATUS_TRANSITION");
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

    private static HttpRequestMessage CreateRequest(Guid userId, string? role, Guid? operatorId)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/operator/users/{userId}/resend-initial-password");
        request.Headers.TryAddWithoutValidation(
            "X-Internal-Auth",
            $"Bearer {CreateInternalJwt(OperatorAdminId, role, operatorId)}");
        return request;
    }

    private static HttpRequestMessage CreateListRequest(string? callerRole, Guid? operatorId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/v1/operator/users?page=1&pageSize=20&sortBy=createdAt&sortDir=desc");
        request.Headers.TryAddWithoutValidation(
            "X-Internal-Auth",
            $"Bearer {CreateInternalJwt(OperatorAdminId, callerRole, operatorId)}");
        return request;
    }

    private static HttpRequestMessage CreateAdminListRequest(string? callerRole, Guid? operatorId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/v1/admin/operator-users?page=1&pageSize=20&sortBy=createdAt&sortDir=desc");
        request.Headers.TryAddWithoutValidation(
            "X-Internal-Auth",
            $"Bearer {CreateInternalJwt(OperatorAdminId, callerRole, operatorId)}");
        return request;
    }

    private static HttpRequestMessage CreateCreateRequest(
        string? callerRole,
        Guid? operatorId,
        string email = "driver@example.com",
        string phone = "+84901112222",
        string displayName = "Driver One",
        string role = "DRIVER")
    {
        var body = JsonSerializer.Serialize(new
        {
            email,
            phone,
            displayName,
            role,
        });
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/operator/users")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation(
            "X-Internal-Auth",
            $"Bearer {CreateInternalJwt(OperatorAdminId, callerRole, operatorId)}");
        return request;
    }

    private static void AssertSuccessEnvelope(JsonDocument doc, int expectedStatusCode)
    {
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("statusCode").GetInt32().Should().Be(expectedStatusCode);
        doc.RootElement.TryGetProperty("data", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("meta", out _).Should().BeTrue();
    }

    private static void AssertOperatorUserLogin(
        JsonDocument doc,
        Guid userId,
        string email,
        UserRole role,
        Guid operatorId)
    {
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
        data.GetProperty("refreshToken").GetString().Should().NotBeNullOrWhiteSpace();
        var user = data.GetProperty("user");
        user.GetProperty("id").GetGuid().Should().Be(userId);
        user.GetProperty("email").GetString().Should().Be(email);
        user.GetProperty("role").GetString().Should().Be(role.ToString());
        user.GetProperty("operatorId").GetGuid().Should().Be(operatorId);
        user.GetProperty("status").GetString().Should().Be(UserStatus.ACTIVE.ToString());
    }

    private static async Task AssertErrorCode(HttpResponseMessage response, HttpStatusCode statusCode, string errorCode)
    {
        response.StatusCode.Should().Be(statusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("statusCode").GetInt32().Should().Be((int)statusCode);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be(errorCode);
    }

    private static string CreateInternalJwt(Guid userId, string? role, Guid? operatorId)
    {
        var now = DateTime.UtcNow;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthWebApplicationFactory.InternalJwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
        ];

        if (!string.IsNullOrWhiteSpace(role))
            claims.Add(new Claim("role", role));

        if (operatorId.HasValue)
            claims.Add(new Claim("operatorId", operatorId.Value.ToString()));

        var token = new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims: claims,
            notBefore: now.AddSeconds(-5),
            expires: now.AddSeconds(120),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string UniqueEmail(string prefix)
        => $"{prefix}-{Guid.NewGuid():N}@example.com";

    private static int GetCounter(OperatorSubscription subscription, string propertyName)
        => (int)typeof(OperatorSubscription).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)!
            .GetValue(subscription)!;

    private sealed class OperatorUsersSender : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
            => Task.FromResult((TResponse)Handle(request));

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => Task.FromResult<object?>(Handle(request));

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Operator user endpoint tests do not use streaming MediatR requests.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Operator user endpoint tests do not use streaming MediatR requests.");

        private static object Handle(object request)
            => request switch
            {
                ResendInitialPasswordCommand command when command.CallerRole != UserRole.OPERATOR_ADMIN.ToString()
                    => throw new ForbiddenException("FORBIDDEN", "Only OPERATOR_ADMIN can resend initial-password links."),

                ResendInitialPasswordCommand command when !command.CallerOperatorId.HasValue
                    => throw new ForbiddenException("FORBIDDEN", "Operator scope is required."),

                ResendInitialPasswordCommand command when command.UserId == CrossOperatorUserId
                    => throw new ForbiddenException("FORBIDDEN", "Target user belongs to another operator."),

                ResendInitialPasswordCommand command when command.UserId == NonOperatorUserId
                    => throw new ForbiddenException("FORBIDDEN", "Target user is not scoped to an operator."),

                ResendInitialPasswordCommand command when command.UserId == MissingUserId
                    => throw new NotFoundException("User", command.UserId),

                ResendInitialPasswordCommand command when command.UserId == WrongStatusUserId
                    => throw new IdentityDomainException(
                        "USER_INVALID_STATUS_TRANSITION",
                        "Initial-password link can only be resent for users pending initial password setup."),

                ListOperatorUsersQuery command when command.Scope == ListOperatorUsersScope.Operator && command.CallerRole != UserRole.OPERATOR_ADMIN.ToString()
                    => throw new ForbiddenException("FORBIDDEN", "Only OPERATOR_ADMIN can list operator users."),

                ListOperatorUsersQuery command when command.Scope == ListOperatorUsersScope.Operator && !command.CallerOperatorId.HasValue
                    => throw new ForbiddenException("FORBIDDEN", "Operator scope is required."),

                ListOperatorUsersQuery command when command.Scope == ListOperatorUsersScope.Admin && command.CallerRole != UserRole.SYSTEM_ADMIN.ToString()
                    => throw new ForbiddenException("FORBIDDEN", "Only SYSTEM_ADMIN can list operator users."),

                ListOperatorUsersQuery command => CreateListOperatorUsersResponse(command),

                CreateOperatorUserCommand command when !command.CallerOperatorId.HasValue
                    => throw new ForbiddenException("FORBIDDEN", "Operator scope is required."),

                CreateOperatorUserCommand command when command.CallerRole != UserRole.OPERATOR_ADMIN.ToString()
                    => throw new ForbiddenException("FORBIDDEN", "Only OPERATOR_ADMIN can create operator-scoped users."),

                CreateOperatorUserCommand command => new CreateOperatorUserResponseDto(
                    Guid.Parse("99999999-9999-9999-9999-999999999999"),
                    command.Email,
                    command.Phone,
                    command.DisplayName,
                    command.Role,
                    UserStatus.PENDING_INITIAL_PASSWORD.ToString(),
                    command.CallerOperatorId!.Value,
                    new DateTimeOffset(2026, 6, 8, 10, 0, 0, TimeSpan.Zero)),

                ResendInitialPasswordCommand command => new ResendInitialPasswordResponseDto(
                    command.UserId,
                    UserStatus.PENDING_INITIAL_PASSWORD.ToString(),
                    new DateTimeOffset(2026, 6, 8, 10, 0, 0, TimeSpan.Zero)),

                _ => throw new InvalidOperationException($"Unexpected request type {request.GetType().Name}."),
            };

        private static PagedResult<OperatorUserListItemDto> CreateListOperatorUsersResponse(ListOperatorUsersQuery command)
        {
            var operatorId = command.Scope == ListOperatorUsersScope.Operator
                ? command.CallerOperatorId!.Value
                : command.OperatorId ?? OperatorId;
            var items = command.Scope == ListOperatorUsersScope.Admin
                ? new[]
                {
                    CreateOperatorUserListItem(UserRole.DRIVER, operatorId, "driver@example.com"),
                    CreateOperatorUserListItem(UserRole.ASSISTANT, operatorId, "assistant@example.com"),
                    CreateOperatorUserListItem(UserRole.OPERATOR_STAFF, operatorId, "staff@example.com"),
                }
                : new[] { CreateOperatorUserListItem(UserRole.DRIVER, operatorId, "driver@example.com") };

            return PagedResult<OperatorUserListItemDto>.Create(items, 1, 20, items.Length);
        }

        private static OperatorUserListItemDto CreateOperatorUserListItem(
            UserRole role,
            Guid operatorId,
            string email)
            => new(
                Guid.Parse("99999999-9999-9999-9999-999999999999"),
                email,
                "+84901112222",
                role == UserRole.OPERATOR_STAFF ? "Operator Staff" : role.ToString(),
                role.ToString(),
                UserStatus.PENDING_INITIAL_PASSWORD.ToString(),
                operatorId,
                new DateTimeOffset(2026, 6, 8, 10, 0, 0, TimeSpan.Zero),
                null);
    }

    private sealed class DbBackedOperatorUsersFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString = BuildTestDatabaseConnectionString();
        private readonly string _databaseName;
        private bool _databaseCreated;

        public DbBackedOperatorUsersFactory()
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

        public async Task InitializeAsync()
        {
            await CreateDatabaseAsync();

            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Database.MigrateAsync();
            await ReloadPostgresTypesAsync();
        }

        public async Task SeedCreateOperatorUserAsync(
            Guid operatorId,
            Guid operatorAdminId,
            int currentDrivers = 0,
            int currentAssistants = 0,
            int currentOperatorUsers = 1,
            OperatorRegistrationStatus operatorStatus = OperatorRegistrationStatus.APPROVED)
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var operatorEntity = CreateOperator(operatorId, operatorStatus);
            var adminUser = User.CreateOperatorAdminPendingPassword(
                $"operator-admin-{Guid.NewGuid():N}@example.com",
                PhoneNumber.Parse("+84905550000"),
                "Operator Admin",
                operatorId);
            SetPrivateProperty(adminUser, nameof(User.Id), operatorAdminId);
            var subscription = OperatorSubscription.CreateActiveTrial(
                operatorId,
                SubscriptionPlan.StarterPlanId,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddDays(30));
            if (currentDrivers > 0)
                subscription.IncrementUsage(SubscriptionUsageResource.DRIVERS, currentDrivers);
            if (currentAssistants > 0)
                subscription.IncrementUsage(SubscriptionUsageResource.ASSISTANTS, currentAssistants);
            if (currentOperatorUsers > 0)
                subscription.IncrementUsage(SubscriptionUsageResource.OPERATOR_USERS, currentOperatorUsers);

            await db.Operators.AddAsync(operatorEntity);
            await db.Users.AddAsync(adminUser);
            await db.OperatorSubscriptions.AddAsync(subscription);
            await db.SaveChangesAsync();
        }

        public async Task SeedResendHappyPathAsync(
            Guid operatorId,
            Guid targetUserId,
            string targetEmail,
            string oldCode,
            OperatorRegistrationStatus operatorStatus = OperatorRegistrationStatus.APPROVED)
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var operatorEntity = CreateOperator(operatorId, operatorStatus);
            var operatorAdmin = User.CreateOperatorAdminPendingPassword(
                $"resend-operator-admin-{Guid.NewGuid():N}@example.com",
                PhoneNumber.Parse("+84905550001"),
                "Operator Admin",
                operatorId);
            SetPrivateProperty(operatorAdmin, nameof(User.Id), OperatorAdminId);
            var targetUser = User.CreateAdminPendingPassword(targetEmail, "Driver One");
            SetPrivateProperty(targetUser, nameof(User.Id), targetUserId);
            SetPrivateProperty(targetUser, nameof(User.Role), UserRole.DRIVER);
            SetPrivateProperty(targetUser, nameof(User.OperatorId), operatorId);
            var oldToken = EmailVerificationToken.Create(
                targetUserId,
                EmailVerificationPurpose.SET_INITIAL_PASSWORD,
                oldCode,
                DateTimeOffset.UtcNow.AddHours(1));

            await db.Operators.AddAsync(operatorEntity);
            await db.Users.AddAsync(operatorAdmin);
            await db.Users.AddAsync(targetUser);
            await db.EmailVerificationTokens.AddAsync(oldToken);
            await db.SaveChangesAsync();
        }

        public async Task DropDatabaseAsync()
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
                Database = $"vietride_identity_task6_3_{Guid.NewGuid():N}",
            };

            return builder.ConnectionString;
        }

        private static Operator CreateOperator(
            Guid operatorId,
            OperatorRegistrationStatus status = OperatorRegistrationStatus.APPROVED)
        {
            var operatorEntity = (Operator)Activator.CreateInstance(typeof(Operator), nonPublic: true)!;
            SetPrivateProperty(operatorEntity, nameof(Operator.Id), operatorId);
            SetPrivateProperty(operatorEntity, nameof(Operator.RegistrationStatus), status);
            return operatorEntity;
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

    private sealed class CapturingEmailService : IEmailService
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
