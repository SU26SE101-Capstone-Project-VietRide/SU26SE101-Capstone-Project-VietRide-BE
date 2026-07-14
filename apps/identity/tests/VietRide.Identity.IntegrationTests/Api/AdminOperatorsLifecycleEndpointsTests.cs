using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using VietRide.Identity.Api.Controllers.Requests;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Infrastructure;
using VietRide.Shared.Persistence;
using VietRide.Shared.Persistence.Outbox;

namespace VietRide.Identity.IntegrationTests.Api;

[Collection(AdminOperatorsLifecycleEndpointsCollection.CollectionName)]
public sealed class AdminOperatorsLifecycleEndpointsTests : IClassFixture<AdminOperatorsLifecycleEndpointsTests.DbBackedLifecycleFactory>
{
    private static readonly Guid SystemAdminId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly DbBackedLifecycleFactory _factory;

    public AdminOperatorsLifecycleEndpointsTests(DbBackedLifecycleFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task List_SystemAdmin_ReturnsPagedOperatorsWithFilterSearchSortAndPage()
    {
        await _factory.ResetAsync();
        await _factory.SeedSystemAdminAsync(SystemAdminId);
        await _factory.SeedOperatorAsync("Alpha Transit", "BRN-ALPHA", "TAX-ALPHA", OperatorRegistrationStatus.APPROVED);
        await _factory.SeedOperatorAsync("Zebra Transit", "BRN-ZEBRA", "TAX-ZEBRA", OperatorRegistrationStatus.APPROVED);
        await _factory.SeedOperatorAsync("Beta Pending", "BRN-BETA", "TAX-BETA", OperatorRegistrationStatus.PENDING);
        using var client = _factory.CreateClient();
        using var request = AuthorizedGet("/v1/admin/operators?status=APPROVED&search=BRN-&sortBy=name&sortDir=asc&page=1&pageSize=1");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertSuccessEnvelope(doc, 200);
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("page").GetInt32().Should().Be(1);
        data.GetProperty("pageSize").GetInt32().Should().Be(1);
        data.GetProperty("totalItems").GetInt64().Should().Be(2);
        data.GetProperty("totalPages").GetInt32().Should().Be(2);
        data.GetProperty("hasNextPage").GetBoolean().Should().BeTrue();
        data.GetProperty("hasPreviousPage").GetBoolean().Should().BeFalse();
        var item = data.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        item.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            ["operatorId", "name", "contactEmail", "contactPhone", "businessRegistrationNumber", "taxCode", "registrationStatus", "isActive", "createdAt", "approvedAt", "suspendedAt"]);
        item.GetProperty("name").GetString().Should().Be("Alpha Transit");
        item.GetProperty("registrationStatus").GetString().Should().Be(OperatorRegistrationStatus.APPROVED.ToString());
    }

    [Fact]
    public async Task List_Anonymous_Returns401()
    {
        await _factory.ResetAsync();
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/v1/admin/operators?page=1&pageSize=20");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_NonSystemAdmin_Returns403()
    {
        await _factory.ResetAsync();
        using var client = _factory.CreateClient();
        using var request = AuthorizedGet("/v1/admin/operators", UserRole.OPERATOR_ADMIN.ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_NumericStatus_Returns422ValidationError()
    {
        await _factory.ResetAsync();
        await _factory.SeedSystemAdminAsync(SystemAdminId);
        using var client = _factory.CreateClient();
        using var request = AuthorizedGet("/v1/admin/operators?status=1");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertErrorEnvelope(doc, 422, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Approve_PendingOperator_Returns200PersistsActiveTrialActivityLogAndOutboxEvent()
    {
        await _factory.ResetAsync();
        await _factory.SeedSystemAdminAsync(SystemAdminId);
        var operatorId = await _factory.SeedPendingOperatorAsync();
        using var client = _factory.CreateClient();
        using var request = AuthorizedPost($"/v1/admin/operators/{operatorId}/approve", new { });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertSuccessEnvelope(doc, 200);
        doc.RootElement.GetProperty("data").GetProperty("operatorId").GetGuid().Should().Be(operatorId);
        doc.RootElement.GetProperty("data").GetProperty("registrationStatus").GetString().Should().Be(OperatorRegistrationStatus.APPROVED.ToString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var operatorEntity = await db.Operators.SingleAsync(x => x.Id == operatorId);
        operatorEntity.RegistrationStatus.Should().Be(OperatorRegistrationStatus.APPROVED);
        operatorEntity.ApprovedByUserId.Should().Be(SystemAdminId);
        operatorEntity.ApprovedAt.Should().NotBeNull();
        var subscription = await db.OperatorSubscriptions.SingleAsync(x => x.OperatorId == operatorId);
        subscription.Status.Should().Be(SubscriptionStatus.ACTIVE);
        subscription.StartedAt.Should().Be(operatorEntity.ApprovedAt);
        subscription.ExpiresAt.Should().Be(operatorEntity.ApprovedAt!.Value.AddDays(30));
        var activityLog = await db.ActivityLogs.SingleAsync(x => x.UserId == SystemAdminId && x.Action == ActivityLogAction.APPROVE_OPERATOR);
        AssertActivityMetadata(activityLog.Metadata, operatorId, "SYSTEM_ADMIN_APPROVE_OPERATOR");

        // Task 10.2 — transactional outbox emits identity.operator.approved (BSOT §7.3).
        var outboxEvent = await db.Set<OutboxEvent>().SingleAsync();
        outboxEvent.EventType.Should().Be("identity.operator.approved");
        outboxEvent.Status.Should().Be(OutboxEventStatus.PENDING);
        using var approvedPayload = JsonDocument.Parse(outboxEvent.Payload);
        approvedPayload.RootElement.GetProperty("eventId").GetGuid().Should().NotBeEmpty();
        approvedPayload.RootElement.GetProperty("operatorId").GetGuid().Should().Be(operatorId);
        // Postgres timestamptz truncates to microseconds; the payload carries full .NET ticks.
        approvedPayload.RootElement.GetProperty("approvedAt").GetDateTimeOffset()
            .Should().BeCloseTo(operatorEntity.ApprovedAt!.Value, TimeSpan.FromMilliseconds(1));
        approvedPayload.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["eventId", "operatorId", "approvedAt"]);
    }

    [Fact]
    public async Task Approve_NonPendingOperator_Returns422ValidationEnvelope()
    {
        await _factory.ResetAsync();
        await _factory.SeedSystemAdminAsync(SystemAdminId);
        var operatorId = await _factory.SeedApprovedOperatorAsync(SystemAdminId);
        using var client = _factory.CreateClient();
        using var request = AuthorizedPost($"/v1/admin/operators/{operatorId}/approve", new { });

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertErrorEnvelope(doc, 422, "VALIDATION_ERROR");
        (await _factory.CountActivityLogsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Reject_PendingOperator_Returns200PersistsRejectedCancelSubscriptionActivityLogAndNoOutbox()
    {
        await _factory.ResetAsync();
        await _factory.SeedSystemAdminAsync(SystemAdminId);
        var operatorId = await _factory.SeedPendingOperatorAsync();
        using var client = _factory.CreateClient();
        using var request = AuthorizedPost($"/v1/admin/operators/{operatorId}/reject", new RejectOperatorRequest("Business registration documents are invalid."));

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertSuccessEnvelope(doc, 200);
        doc.RootElement.GetProperty("data").GetProperty("operatorId").GetGuid().Should().Be(operatorId);
        doc.RootElement.GetProperty("data").GetProperty("registrationStatus").GetString().Should().Be(OperatorRegistrationStatus.REJECTED.ToString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var operatorEntity = await db.Operators.SingleAsync(x => x.Id == operatorId);
        operatorEntity.RegistrationStatus.Should().Be(OperatorRegistrationStatus.REJECTED);
        operatorEntity.RejectedByUserId.Should().Be(SystemAdminId);
        operatorEntity.RejectedAt.Should().NotBeNull();
        operatorEntity.RejectReason.Should().Be("Business registration documents are invalid.");
        var subscription = await db.OperatorSubscriptions.SingleAsync(x => x.OperatorId == operatorId);
        subscription.Status.Should().Be(SubscriptionStatus.CANCELLED);
        var activityLog = await db.ActivityLogs.SingleAsync(x => x.UserId == SystemAdminId && x.Action == ActivityLogAction.REJECT_OPERATOR);
        AssertActivityMetadata(activityLog.Metadata, operatorId, "SYSTEM_ADMIN_REJECT_OPERATOR");
        (await db.Set<OutboxEvent>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Reject_NonPendingOperator_Returns422ValidationEnvelope()
    {
        await _factory.ResetAsync();
        await _factory.SeedSystemAdminAsync(SystemAdminId);
        var operatorId = await _factory.SeedApprovedOperatorAsync(SystemAdminId);
        using var client = _factory.CreateClient();
        using var request = AuthorizedPost($"/v1/admin/operators/{operatorId}/reject", new RejectOperatorRequest("Invalid documents."));

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertErrorEnvelope(doc, 422, "VALIDATION_ERROR");
        (await _factory.CountActivityLogsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Suspend_ApprovedOperator_Returns200PersistsSuspendedWithoutActivityLogAndWithOutboxEvent()
    {
        await _factory.ResetAsync();
        await _factory.SeedSystemAdminAsync(SystemAdminId);
        var operatorId = await _factory.SeedApprovedOperatorAsync(SystemAdminId);
        using var client = _factory.CreateClient();
        using var request = AuthorizedPost($"/v1/admin/operators/{operatorId}/suspend", new SuspendOperatorRequest("Policy violation"));

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertSuccessEnvelope(doc, 200);
        doc.RootElement.GetProperty("data").GetProperty("operatorId").GetGuid().Should().Be(operatorId);
        doc.RootElement.GetProperty("data").GetProperty("registrationStatus").GetString().Should().Be(OperatorRegistrationStatus.SUSPENDED.ToString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var operatorEntity = await db.Operators.SingleAsync(x => x.Id == operatorId);
        operatorEntity.RegistrationStatus.Should().Be(OperatorRegistrationStatus.SUSPENDED);
        operatorEntity.SuspendedAt.Should().NotBeNull();
        operatorEntity.SuspendReason.Should().Be("Policy violation");
        (await db.ActivityLogs.CountAsync()).Should().Be(0);

        // Task 10.2 — transactional outbox emits identity.operator.suspended (BSOT §7.3).
        var outboxEvent = await db.Set<OutboxEvent>().SingleAsync();
        outboxEvent.EventType.Should().Be("identity.operator.suspended");
        outboxEvent.Status.Should().Be(OutboxEventStatus.PENDING);
        using var suspendedPayload = JsonDocument.Parse(outboxEvent.Payload);
        suspendedPayload.RootElement.GetProperty("operatorId").GetGuid().Should().Be(operatorId);
        // Postgres timestamptz truncates to microseconds; the payload carries full .NET ticks.
        suspendedPayload.RootElement.GetProperty("suspendedAt").GetDateTimeOffset()
            .Should().BeCloseTo(operatorEntity.SuspendedAt!.Value, TimeSpan.FromMilliseconds(1));
        suspendedPayload.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["operatorId", "suspendedAt"]);
    }

    [Fact]
    public async Task Suspend_NonApprovedOperator_Returns422ValidationEnvelope()
    {
        await _factory.ResetAsync();
        await _factory.SeedSystemAdminAsync(SystemAdminId);
        var operatorId = await _factory.SeedPendingOperatorAsync();
        using var client = _factory.CreateClient();
        using var request = AuthorizedPost($"/v1/admin/operators/{operatorId}/suspend", new SuspendOperatorRequest("Policy violation"));

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertErrorEnvelope(doc, 422, "VALIDATION_ERROR");
        (await _factory.CountActivityLogsAsync()).Should().Be(0);
    }

    private static HttpRequestMessage AuthorizedPost<T>(string url, T payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateInternalJwt(SystemAdminId, UserRole.SYSTEM_ADMIN.ToString())}");
        return request;
    }

    private static HttpRequestMessage AuthorizedGet(string url, string role = "SYSTEM_ADMIN")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateInternalJwt(SystemAdminId, role)}");
        return request;
    }

    private static void AssertSuccessEnvelope(JsonDocument doc, int statusCode)
    {
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("statusCode").GetInt32().Should().Be(statusCode);
        doc.RootElement.TryGetProperty("data", out _).Should().BeTrue();
        doc.RootElement.GetProperty("meta").TryGetProperty("traceId", out _).Should().BeTrue();
    }

    private static void AssertErrorEnvelope(JsonDocument doc, int statusCode, string code)
    {
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("statusCode").GetInt32().Should().Be(statusCode);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be(code);
        doc.RootElement.GetProperty("meta").TryGetProperty("traceId", out _).Should().BeTrue();
    }

    private static void AssertActivityMetadata(string? metadata, Guid operatorId, string source)
    {
        using var document = JsonDocument.Parse(metadata!);
        document.RootElement.GetProperty("operatorId").GetGuid().Should().Be(operatorId);
        document.RootElement.GetProperty("actorUserId").GetGuid().Should().Be(SystemAdminId);
        document.RootElement.GetProperty("source").GetString().Should().Be(source);
    }

    private static string CreateInternalJwt(Guid userId, string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthWebApplicationFactory.InternalJwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim("role", role),
                new Claim(ClaimTypes.Role, role),
            ],
            notBefore: DateTime.UtcNow.AddSeconds(-5),
            expires: DateTime.UtcNow.AddSeconds(120),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public sealed class DbBackedLifecycleFactory : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
    {
        private readonly string _connectionString = BuildTestDatabaseConnectionString();
        private readonly string _databaseName;
        private bool _databaseCreated;
        private bool _initialized;

        public DbBackedLifecycleFactory()
        {
            _databaseName = new NpgsqlConnectionStringBuilder(_connectionString).Database!;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("INTERNAL_JWT_SECRET", AuthWebApplicationFactory.InternalJwtSecret);
            builder.UseSetting("ConnectionStrings:Default", _connectionString);
            builder.UseSetting("REDIS_URL", "localhost:6379,abortConnect=false");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<NpgsqlDataSource>();
                services.RemoveAll<DbContextOptions<IdentityDbContext>>();
                services.RemoveAll<IdentityDbContext>();
                services.RemoveAll<VietRideDbContextBase>();

                services.AddSingleton(_ =>
                {
                    var dataSourceBuilder = new NpgsqlDataSourceBuilder(_connectionString);
                    IdentityDbContext.ConfigurePostgresEnums(dataSourceBuilder);
                    // Map the shared outbox enum (normally wired by AddVietRideDbContext) so
                    // outbox_events INSERTs from the lifecycle handlers can serialize the status.
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
            });
        }

        public async Task InitializeAsync()
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

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await DropDatabaseAsync();
        }

        public async Task ResetAsync()
        {
            await InitializeAsync();

            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "TRUNCATE TABLE vietride_identity.activity_logs, vietride_identity.email_verification_tokens, vietride_identity.operator_subscriptions, vietride_identity.users, vietride_identity.operators, vietride_identity.outbox_events RESTART IDENTITY CASCADE;");
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

        public async Task<Guid> SeedPendingOperatorAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var now = DateTimeOffset.UtcNow;
            var operatorEntity = NewPendingOperator();
            var subscription = OperatorSubscription.CreatePendingApproval(operatorEntity.Id, SubscriptionPlan.StarterPlanId, now);
            await db.Operators.AddAsync(operatorEntity);
            await db.OperatorSubscriptions.AddAsync(subscription);
            await db.SaveChangesAsync();
            return operatorEntity.Id;
        }

        public async Task<Guid> SeedApprovedOperatorAsync(Guid approvedByUserId)
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var now = DateTimeOffset.UtcNow;
            var operatorEntity = NewPendingOperator();
            operatorEntity.Approve(approvedByUserId, now);
            var subscription = OperatorSubscription.CreateActiveTrial(operatorEntity.Id, SubscriptionPlan.StarterPlanId, now, now.AddDays(30));
            await db.Operators.AddAsync(operatorEntity);
            await db.OperatorSubscriptions.AddAsync(subscription);
            await db.SaveChangesAsync();
            return operatorEntity.Id;
        }

        public async Task<Guid> SeedOperatorAsync(
            string name,
            string businessRegistrationNumber,
            string taxCode,
            OperatorRegistrationStatus status)
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var now = DateTimeOffset.UtcNow;
            var phoneDigits = new string(businessRegistrationNumber.Where(char.IsDigit).ToArray());
            var phoneSuffix = phoneDigits.PadLeft(7, '0')[^7..];
            var operatorEntity = Operator.CreatePending(
                name,
                businessRegistrationNumber,
                taxCode,
                $"{businessRegistrationNumber.ToLowerInvariant()}@example.com",
                $"+8490{phoneSuffix}",
                "1 Street",
                "Ward",
                "District",
                "Province",
                "Operator Admin",
                "+84901234568");

            if (status == OperatorRegistrationStatus.APPROVED)
                operatorEntity.Approve(SystemAdminId, now);
            else if (status == OperatorRegistrationStatus.REJECTED)
                operatorEntity.Reject(SystemAdminId, "Rejected in test", now);
            else if (status == OperatorRegistrationStatus.SUSPENDED)
            {
                operatorEntity.Approve(SystemAdminId, now.AddMinutes(-5));
                operatorEntity.Suspend("Suspended in test", now);
            }

            await db.Operators.AddAsync(operatorEntity);
            await db.SaveChangesAsync();
            return operatorEntity.Id;
        }

        public async Task<int> CountActivityLogsAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            return await db.ActivityLogs.CountAsync();
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
                Database = $"vietride_identity_task6_2_{Guid.NewGuid():N}",
            };

            return builder.ConnectionString;
        }

        private static Operator NewPendingOperator()
            => Operator.CreatePending(
                "Operator Co",
                $"BRN-{Guid.NewGuid():N}",
                $"TAX-{Guid.NewGuid():N}",
                $"operator-{Guid.NewGuid():N}@example.com",
                "+84901234567",
                "1 Street",
                "Ward",
                "District",
                "Province",
                "Operator Admin",
                "+84901234568");

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
}
