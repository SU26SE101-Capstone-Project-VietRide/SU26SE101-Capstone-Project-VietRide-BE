using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Infrastructure;
using VietRide.Shared.Web.Authentication;

namespace VietRide.Identity.IntegrationTests.Api;

public sealed class InternalOperatorsEndpointsTests
{
    private static readonly Guid OperatorId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid MissingOperatorId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InternalEndpoints_RejectAnonymousAndAuthorizationUserJwt_ButAllowInternalJwt()
    {
        var factory = new DbBackedInternalOperatorsFactory();
        try
        {
            await factory.InitializeAsync();
            await factory.SeedOperatorSubscriptionAsync(OperatorId);
            using var anonymousClient = factory.CreateClient();

            var anonymous = await anonymousClient.GetAsync($"/internal/v1/operators/{OperatorId}");

            anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            using (var doc = JsonDocument.Parse(await anonymous.Content.ReadAsStringAsync()))
            {
                doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
            }

            using var userJwtRequest = new HttpRequestMessage(HttpMethod.Get, $"/internal/v1/operators/{OperatorId}");
            userJwtRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "not-an-internal-token");
            var userJwt = await anonymousClient.SendAsync(userJwtRequest);
            userJwt.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            using var internalClient = factory.CreateClient();
            AddInternalJwt(internalClient);

            var allowed = await internalClient.GetAsync($"/internal/v1/operators/{OperatorId}");

            allowed.StatusCode.Should().Be(HttpStatusCode.OK);
            var json = await allowed.Content.ReadAsStringAsync();
            json.Should().NotContain("\"success\"");
            using var allowedDoc = JsonDocument.Parse(json);
            allowedDoc.RootElement.GetProperty("operatorId").GetGuid().Should().Be(OperatorId);
        }
        finally
        {
            await factory.DropDatabaseAsync();
            factory.Dispose();
        }
    }

    [Fact]
    public async Task GetOperator_MissingOperator_Returns404ResourceNotFoundEnvelope()
    {
        var factory = new DbBackedInternalOperatorsFactory();
        try
        {
            await factory.InitializeAsync();
            using var client = factory.CreateClient();
            AddInternalJwt(client);

            var response = await client.GetAsync($"/internal/v1/operators/{MissingOperatorId}");

            await AssertErrorCode(response, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");
        }
        finally
        {
            await factory.DropDatabaseAsync();
            factory.Dispose();
        }
    }

    [Fact]
    public async Task GetSubscription_ReturnsRawPlanLimitsModulesAndUsageCounters()
    {
        var factory = new DbBackedInternalOperatorsFactory();
        try
        {
            await factory.InitializeAsync();
            await factory.SeedOperatorSubscriptionAsync(OperatorId, currentOperatorUsers: 1, currentDrivers: 2);
            using var client = factory.CreateClient();
            AddInternalJwt(client);

            var response = await client.GetAsync($"/internal/v1/operators/{OperatorId}/subscription");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var json = await response.Content.ReadAsStringAsync();
            json.Should().NotContain("\"success\"");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            root.GetProperty("operatorId").GetGuid().Should().Be(OperatorId);
            root.GetProperty("status").GetString().Should().Be(SubscriptionStatus.ACTIVE.ToString());
            root.GetProperty("plan").GetProperty("planId").GetGuid().Should().Be(SubscriptionPlan.StarterPlanId);
            root.GetProperty("plan").GetProperty("limits").GetProperty("maxDrivers").GetInt32().Should().Be(5);
            root.GetProperty("plan").GetProperty("modules").GetProperty("enableRag").GetBoolean().Should().BeTrue();
            root.GetProperty("usage").GetProperty("currentOperatorUsers").GetInt32().Should().Be(1);
            root.GetProperty("usage").GetProperty("currentDrivers").GetInt32().Should().Be(2);
        }
        finally
        {
            await factory.DropDatabaseAsync();
            factory.Dispose();
        }
    }

    [Theory]
    [InlineData("VEHICLES", "currentVehicles")]
    [InlineData("DRIVERS", "currentDrivers")]
    [InlineData("ASSISTANTS", "currentAssistants")]
    [InlineData("OPERATOR_USERS", "currentOperatorUsers")]
    [InlineData("ROUTES", "currentRoutes")]
    [InlineData("TRIPS_THIS_MONTH", "currentTripsThisMonth")]
    public async Task IncrementUsage_AllowedResource_IncrementsMatchingCounter(string resource, string counterProperty)
    {
        var factory = new DbBackedInternalOperatorsFactory();
        try
        {
            await factory.InitializeAsync();
            await factory.SeedOperatorSubscriptionAsync(OperatorId);
            using var client = factory.CreateClient();
            AddInternalJwt(client);

            var response = await client.PostAsJsonAsync(
                $"/internal/v1/operators/{OperatorId}/usage/increment",
                new { resource, delta = 1 });

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("usage").GetProperty(counterProperty).GetInt32().Should().Be(1);
        }
        finally
        {
            await factory.DropDatabaseAsync();
            factory.Dispose();
        }
    }

    [Fact]
    public async Task IncrementUsage_Overflow_ReturnsSubscriptionLimitExceededAndDoesNotIncrement()
    {
        var factory = new DbBackedInternalOperatorsFactory();
        try
        {
            await factory.InitializeAsync();
            await factory.SeedOperatorSubscriptionAsync(OperatorId, currentDrivers: 5);
            using var client = factory.CreateClient();
            AddInternalJwt(client);

            var response = await client.PostAsJsonAsync(
                $"/internal/v1/operators/{OperatorId}/usage/increment",
                new { resource = "DRIVERS", delta = 1 });

            await AssertErrorCode(response, HttpStatusCode.UnprocessableEntity, "SUBSCRIPTION_LIMIT_EXCEEDED");
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var subscription = await db.OperatorSubscriptions.SingleAsync(s => s.OperatorId == OperatorId);
            subscription.CurrentDrivers.Should().Be(5);
        }
        finally
        {
            await factory.DropDatabaseAsync();
            factory.Dispose();
        }
    }

    [Fact]
    public async Task IncrementUsage_ExpiredSubscription_Returns402SubscriptionExpired()
    {
        var factory = new DbBackedInternalOperatorsFactory();
        try
        {
            await factory.InitializeAsync();
            await factory.SeedOperatorSubscriptionAsync(OperatorId, subscriptionStatus: SubscriptionStatus.EXPIRED);
            using var client = factory.CreateClient();
            AddInternalJwt(client);

            var response = await client.PostAsJsonAsync(
                $"/internal/v1/operators/{OperatorId}/usage/increment",
                new { resource = "DRIVERS", delta = 1 });

            await AssertErrorCode(response, HttpStatusCode.PaymentRequired, "SUBSCRIPTION_EXPIRED");
        }
        finally
        {
            await factory.DropDatabaseAsync();
            factory.Dispose();
        }
    }

    [Theory]
    [InlineData("UNKNOWN", 1)]
    [InlineData("DRIVERS", 0)]
    public async Task IncrementUsage_InvalidBody_Returns422ValidationError(string resource, int delta)
    {
        var factory = new DbBackedInternalOperatorsFactory();
        try
        {
            await factory.InitializeAsync();
            await factory.SeedOperatorSubscriptionAsync(OperatorId);
            using var client = factory.CreateClient();
            AddInternalJwt(client);

            var response = await client.PostAsJsonAsync(
                $"/internal/v1/operators/{OperatorId}/usage/increment",
                new { resource, delta });

            await AssertErrorCode(response, HttpStatusCode.UnprocessableEntity, "VALIDATION_ERROR");
        }
        finally
        {
            await factory.DropDatabaseAsync();
            factory.Dispose();
        }
    }

    [Fact]
    public async Task IncrementUsage_ConcurrentRemainingCapacityOne_AllowsExactlyOneSuccess()
    {
        var factory = new DbBackedInternalOperatorsFactory();
        try
        {
            await factory.InitializeAsync();
            await factory.SeedOperatorSubscriptionAsync(OperatorId, currentDrivers: 4);
            using var clientOne = factory.CreateClient();
            using var clientTwo = factory.CreateClient();
            AddInternalJwt(clientOne);
            AddInternalJwt(clientTwo);

            var calls = new[]
            {
                clientOne.PostAsJsonAsync($"/internal/v1/operators/{OperatorId}/usage/increment", new { resource = "DRIVERS", delta = 1 }),
                clientTwo.PostAsJsonAsync($"/internal/v1/operators/{OperatorId}/usage/increment", new { resource = "DRIVERS", delta = 1 }),
            };

            var responses = await Task.WhenAll(calls);

            responses.Count(response => response.StatusCode == HttpStatusCode.OK).Should().Be(1);
            responses.Count(response => response.StatusCode == HttpStatusCode.UnprocessableEntity).Should().Be(1);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var subscription = await db.OperatorSubscriptions.SingleAsync(s => s.OperatorId == OperatorId);
            subscription.CurrentDrivers.Should().Be(5);
        }
        finally
        {
            await factory.DropDatabaseAsync();
            factory.Dispose();
        }
    }

    private static void AddInternalJwt(HttpClient client)
    {
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            InternalJwtAuthenticationExtensions.HeaderName,
            $"Bearer {CreateInternalJwt()}");
    }

    private static string CreateInternalJwt()
    {
        var now = DateTime.UtcNow;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthWebApplicationFactory.InternalJwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims: [new Claim(JwtRegisteredClaimNames.Sub, "trip-service")],
            notBefore: now.AddSeconds(-5),
            expires: now.AddSeconds(120),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task AssertErrorCode(HttpResponseMessage response, HttpStatusCode statusCode, string errorCode)
    {
        response.StatusCode.Should().Be(statusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("statusCode").GetInt32().Should().Be((int)statusCode);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be(errorCode);
    }

    private sealed class DbBackedInternalOperatorsFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString = BuildTestDatabaseConnectionString();
        private readonly string _databaseName;
        private bool _databaseCreated;

        public DbBackedInternalOperatorsFactory()
        {
            _databaseName = new NpgsqlConnectionStringBuilder(_connectionString).Database!;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Environment.SetEnvironmentVariable("INTERNAL_JWT_SECRET", AuthWebApplicationFactory.InternalJwtSecret);
            builder.UseEnvironment("Testing");
            builder.UseSetting("INTERNAL_JWT_SECRET", AuthWebApplicationFactory.InternalJwtSecret);
            builder.UseSetting("ConnectionStrings:Default", _connectionString);
            builder.UseSetting("REDIS_URL", "localhost:6379,abortConnect=false");
            builder.UseSetting("IdentityJwt:Kid", "test-kid");
            builder.UseSetting("IdentityJwt:PrivateKey", AuthWebApplicationFactory.DevPrivateKeyPem);
        }

        public async Task InitializeAsync()
        {
            await CreateDatabaseAsync();

            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Database.MigrateAsync();
            await ReloadPostgresTypesAsync();
        }

        public async Task SeedOperatorSubscriptionAsync(
            Guid operatorId,
            int currentVehicles = 0,
            int currentDrivers = 0,
            int currentAssistants = 0,
            int currentOperatorUsers = 0,
            int currentRoutes = 0,
            int currentTripsThisMonth = 0,
            SubscriptionStatus subscriptionStatus = SubscriptionStatus.ACTIVE)
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var operatorEntity = Operator.CreateApproved(
                "VietRide Limousine",
                "0312345678",
                "0312345678",
                "ops@example.com",
                "+84901234567",
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Now);
            SetProperty(operatorEntity, nameof(Operator.Id), operatorId);
            var subscription = OperatorSubscription.CreateActiveTrial(
                operatorId,
                SubscriptionPlan.StarterPlanId,
                Now,
                Now.AddDays(30));
            SetProperty(subscription, nameof(OperatorSubscription.LastResetAt), Now);
            IncrementIfPositive(subscription, SubscriptionUsageResource.VEHICLES, currentVehicles);
            IncrementIfPositive(subscription, SubscriptionUsageResource.DRIVERS, currentDrivers);
            IncrementIfPositive(subscription, SubscriptionUsageResource.ASSISTANTS, currentAssistants);
            IncrementIfPositive(subscription, SubscriptionUsageResource.OPERATOR_USERS, currentOperatorUsers);
            IncrementIfPositive(subscription, SubscriptionUsageResource.ROUTES, currentRoutes);
            IncrementIfPositive(subscription, SubscriptionUsageResource.TRIPS_THIS_MONTH, currentTripsThisMonth);
            if (subscriptionStatus == SubscriptionStatus.EXPIRED)
            {
                subscription.MarkExpired(Now.AddDays(31));
            }

            await db.Operators.AddAsync(operatorEntity);
            await db.OperatorSubscriptions.AddAsync(subscription);
            await db.SaveChangesAsync();
        }

        public async Task DropDatabaseAsync()
        {
            if (!_databaseCreated)
                return;

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

        private static void IncrementIfPositive(OperatorSubscription subscription, SubscriptionUsageResource resource, int amount)
        {
            if (amount > 0)
                subscription.IncrementUsage(resource, amount);
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
                Database = $"vietride_identity_task6_5_{Guid.NewGuid():N}",
            };

            return builder.ConnectionString;
        }

        private static void SetProperty<T>(object entity, string propertyName, T value)
        {
            var property = entity.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            property.SetValue(entity, value);
        }
    }
}
