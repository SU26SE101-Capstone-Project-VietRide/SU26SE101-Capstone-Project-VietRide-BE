using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Features.Auth.ResendInitialPassword;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Domain.Exceptions;
using VietRide.Identity.Infrastructure;
using VietRide.Shared.Application.Exceptions;

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

            var activityLogs = await db.ActivityLogs
                .Where(log => log.UserId == TargetUserId && log.Action == ActivityLogAction.RESEND_INITIAL_PASSWORD)
                .ToListAsync();
            activityLogs.Should().ContainSingle();

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

    private static void AssertSuccessEnvelope(JsonDocument doc, int expectedStatusCode)
    {
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("statusCode").GetInt32().Should().Be(expectedStatusCode);
        doc.RootElement.TryGetProperty("data", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("meta", out _).Should().BeTrue();
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

                ResendInitialPasswordCommand command => new ResendInitialPasswordResponseDto(
                    command.UserId,
                    UserStatus.PENDING_INITIAL_PASSWORD.ToString(),
                    new DateTimeOffset(2026, 6, 8, 10, 0, 0, TimeSpan.Zero)),

                _ => throw new InvalidOperationException($"Unexpected request type {request.GetType().Name}."),
            };
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

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEmailService>();
                services.AddSingleton<IEmailService>(EmailService);
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

        public async Task SeedResendHappyPathAsync(Guid operatorId, Guid targetUserId, string targetEmail, string oldCode)
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var operatorEntity = CreateOperator(operatorId);
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
                Database = $"vietride_identity_task5_3_{Guid.NewGuid():N}",
            };

            return builder.ConnectionString;
        }

        private static Operator CreateOperator(Guid operatorId)
        {
            var operatorEntity = (Operator)Activator.CreateInstance(typeof(Operator), nonPublic: true)!;
            SetPrivateProperty(operatorEntity, nameof(Operator.Id), operatorId);
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

    private sealed class CapturingEmailService : IEmailService
    {
        public List<(string To, AccountCreatedEmailDto Info)> SentAccountCreatedLinks { get; } = [];

        public Task SendOtpAsync(string to, string code, EmailOtpPurpose purpose, int ttlMinutes, CancellationToken ct = default)
            => throw new NotSupportedException();

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
