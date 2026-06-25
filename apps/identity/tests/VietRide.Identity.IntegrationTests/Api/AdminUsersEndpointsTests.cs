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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Features.Admin.CreateAdminUser;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Infrastructure;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.IntegrationTests.Api;

public sealed class AdminUsersEndpointsTests : IClassFixture<AuthWebApplicationFactory>
{
    private static readonly Guid SystemAdminId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid PassengerId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly AuthWebApplicationFactory _factory;

    public AdminUsersEndpointsTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateAdminUser_HappyPath_Returns201Envelope()
    {
        using var client = CreateClientWithSender(new AuthorizingAdminUsersSender());
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/admin/users")
        {
            Content = JsonContent.Create(new
            {
                email = "new-admin@example.com",
                displayName = "New Admin",
                role = "SYSTEM_ADMIN",
            }),
        };
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateInternalJwt(SystemAdminId, "SYSTEM_ADMIN")}");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertSuccessEnvelope(doc, 201);
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("email").GetString().Should().Be("new-admin@example.com");
        data.GetProperty("displayName").GetString().Should().Be("New Admin");
        data.GetProperty("role").GetString().Should().Be("SYSTEM_ADMIN");
        data.GetProperty("status").GetString().Should().Be("PENDING_INITIAL_PASSWORD");
    }

    [Fact]
    public async Task CreateAdminUser_HappyPath_UsesRealHandlerDbTransaction_AndPersistsTokenEmailAndActivityLog()
    {
        var dbFactory = new DbBackedAdminUsersFactory();
        var email = $"new-admin-{Guid.NewGuid():N}@example.com";

        try
        {
            await dbFactory.InitializeAsync();

            using var client = dbFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/admin/users")
            {
                Content = JsonContent.Create(new
                {
                    email,
                    displayName = "New Admin",
                    role = "SYSTEM_ADMIN",
                }),
            };
            request.Headers.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateInternalJwt(SystemAdminId, "SYSTEM_ADMIN")}");
            var beforeSend = DateTimeOffset.UtcNow;

            var response = await client.SendAsync(request);

            var afterSend = DateTimeOffset.UtcNow;
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            AssertSuccessEnvelope(doc, 201);
            var userId = doc.RootElement.GetProperty("data").GetProperty("userId").GetGuid();

            await using var scope = dbFactory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var token = await db.EmailVerificationTokens
                .SingleAsync(t => t.UserId == userId && t.Purpose == EmailVerificationPurpose.SET_INITIAL_PASSWORD);
            token.UsedAt.Should().BeNull();
            token.Code.Should().NotBeNullOrWhiteSpace();
            Guid.TryParse(token.Code, out _).Should().BeTrue();
            token.ExpiresAt.Should().BeOnOrAfter(beforeSend.AddHours(48).AddMinutes(-1));
            token.ExpiresAt.Should().BeOnOrBefore(afterSend.AddHours(48).AddMinutes(1));

            var activityLog = await db.ActivityLogs
                .SingleAsync(log => log.UserId == userId && log.Action == ActivityLogAction.SET_INITIAL_PASSWORD);
            activityLog.Metadata.Should().Contain(SystemAdminId.ToString());

            dbFactory.EmailService.SentAccountCreatedLinks.Should().ContainSingle();
            var sentEmail = dbFactory.EmailService.SentAccountCreatedLinks.Single();
            sentEmail.To.Should().Be(email);
            sentEmail.Info.UserId.Should().Be(userId);
            sentEmail.Info.DisplayName.Should().Be("New Admin");
            sentEmail.Info.SetInitialPasswordUrl.Should().EndWith(token.Code);
            sentEmail.Info.ExpiresAt.Should().BeCloseTo(token.ExpiresAt, TimeSpan.FromMilliseconds(1));
        }
        finally
        {
            dbFactory.Dispose();
            await dbFactory.DropDatabaseAsync();
        }
    }

    [Fact]
    public async Task CreateAdminUser_NonSystemAdminCaller_Returns403ForbiddenEnvelope()
    {
        using var client = CreateClientWithSender(new AuthorizingAdminUsersSender());
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/admin/users")
        {
            Content = JsonContent.Create(new
            {
                email = "new-admin@example.com",
                displayName = "New Admin",
                role = "SYSTEM_ADMIN",
            }),
        };
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateInternalJwt(PassengerId, "PASSENGER")}");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("statusCode").GetInt32().Should().Be(403);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("FORBIDDEN");
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

    private static string CreateInternalJwt(Guid userId, string role)
    {
        var now = DateTime.UtcNow;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthWebApplicationFactory.InternalJwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim("role", role),
            ],
            notBefore: now.AddSeconds(-5),
            expires: now.AddSeconds(120),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class DbBackedAdminUsersFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString = BuildTestDatabaseConnectionString();
        private readonly string _databaseName;
        private bool _databaseCreated;

        public DbBackedAdminUsersFactory()
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
                Database = $"vietride_identity_task5_6a_{Guid.NewGuid():N}",
            };

            return builder.ConnectionString;
        }
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

    private sealed class AuthorizingAdminUsersSender : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
            => Task.FromResult((TResponse)Handle(request));

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => Task.FromResult<object?>(Handle(request));

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Admin user endpoint tests do not use streaming MediatR requests.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Admin user endpoint tests do not use streaming MediatR requests.");

        private static object Handle(object request)
            => request switch
            {
                CreateAdminUserCommand command when command.CallerRole != UserRole.SYSTEM_ADMIN.ToString()
                    => throw new ForbiddenException("FORBIDDEN", "Only SYSTEM_ADMIN can create admin users."),

                CreateAdminUserCommand command => new CreateAdminUserResponseDto(
                    UserId: Guid.Parse("55555555-5555-5555-5555-555555555555"),
                    Email: command.Email,
                    DisplayName: command.DisplayName,
                    Role: UserRole.SYSTEM_ADMIN.ToString(),
                    Status: UserStatus.PENDING_INITIAL_PASSWORD.ToString()),

                _ => throw new InvalidOperationException($"Unexpected request type {request.GetType().Name}."),
            };
    }
}
