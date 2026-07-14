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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Infrastructure;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Persistence;
using VietRide.Shared.Persistence.UnitOfWork;

namespace VietRide.Identity.IntegrationTests.Api;

public sealed class DevicesEndpointsTests : IClassFixture<AuthWebApplicationFactory>, IClassFixture<DevicesEndpointsTests.IdentityDeviceEndpointFixture>
{
    private static readonly Guid CallerUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly AuthWebApplicationFactory _factory;
    private readonly IdentityDeviceEndpointFixture _fixture;

    public DevicesEndpointsTests(AuthWebApplicationFactory factory, IdentityDeviceEndpointFixture fixture)
    {
        _factory = factory;
        _fixture = fixture;
    }

    [Fact]
    public async Task RegisterDeviceToken_NewToken_InsertsActiveRow()
    {
        await SeedUsersAsync(CallerUserId);
        using var client = CreateDbBackedClient(CallerUserId);
        var fcmToken = UniqueToken("new");

        var response = await client.PostAsJsonAsync("/v1/auth/device-token", new { fcmToken, platform = "ANDROID" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertRegisterResponse(doc, 200, fcmToken, "ANDROID", expectedIsActive: true);
        var deviceId = doc.RootElement.GetProperty("data").GetProperty("userDeviceId").GetGuid();
        await using var db = _fixture.CreateDbContext();
        var devices = await db.UserDevices.Where(d => d.FcmToken == fcmToken).ToListAsync();
        devices.Should().ContainSingle();
        devices[0].Id.Should().Be(deviceId);
        devices[0].UserId.Should().Be(CallerUserId);
        devices[0].Platform.Should().Be(DevicePlatform.ANDROID);
        devices[0].IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task RegisterDeviceToken_SameUserActiveToken_RefreshesWithoutDuplicate()
    {
        var fcmToken = UniqueToken("same-active");
        var originalLastActiveAt = DateTimeOffset.UtcNow.AddDays(-1);
        var existing = UserDevice.Create(CallerUserId, fcmToken, DevicePlatform.IOS, originalLastActiveAt);
        await SeedUsersAndDevicesAsync([CallerUserId], [existing]);
        using var client = CreateDbBackedClient(CallerUserId);

        var response = await client.PostAsJsonAsync("/v1/auth/device-token", new { fcmToken, platform = "IOS" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            AssertRegisterResponse(doc, 200, fcmToken, "IOS", expectedIsActive: true);
            doc.RootElement.GetProperty("data").GetProperty("userDeviceId").GetGuid().Should().Be(existing.Id);
        }

        await using var db = _fixture.CreateDbContext();
        var devices = await db.UserDevices.Where(d => d.FcmToken == fcmToken).ToListAsync();
        devices.Should().ContainSingle();
        devices[0].Id.Should().Be(existing.Id);
        devices[0].UserId.Should().Be(CallerUserId);
        devices[0].IsActive.Should().BeTrue();
        devices[0].LastActiveAt.Should().BeAfter(originalLastActiveAt);
    }

    [Fact]
    public async Task RemoveDeviceToken_ExistingToken_DeactivatesSameRowAndReturns204EmptyBody()
    {
        var fcmToken = UniqueToken("delete");
        var existing = UserDevice.Create(CallerUserId, fcmToken, DevicePlatform.ANDROID, DateTimeOffset.UtcNow.AddHours(-1));
        await SeedUsersAndDevicesAsync([CallerUserId], [existing]);
        using var client = CreateDbBackedClient(CallerUserId);
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/v1/auth/device-token")
        {
            Content = JsonContent.Create(new { fcmToken }),
        };

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
        await using var db = _fixture.CreateDbContext();
        var devices = await db.UserDevices.Where(d => d.FcmToken == fcmToken).ToListAsync();
        devices.Should().ContainSingle();
        devices[0].Id.Should().Be(existing.Id);
        devices[0].IsActive.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RemoveDeviceToken_NullOrBlankFcmToken_Returns422ValidationError(string? fcmToken)
    {
        await SeedUsersAsync(CallerUserId);
        using var client = CreateDbBackedClient(CallerUserId);
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/v1/auth/device-token")
        {
            Content = JsonContent.Create(new { fcmToken }),
        };

        var response = await client.SendAsync(request);

        await AssertValidationError(response, "fcmToken");
    }

    [Fact]
    public async Task RemoveDeviceToken_MissingFcmToken_Returns422ValidationError()
    {
        await SeedUsersAsync(CallerUserId);
        using var client = CreateDbBackedClient(CallerUserId);
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/v1/auth/device-token")
        {
            Content = JsonContent.Create(new { }),
        };

        var response = await client.SendAsync(request);

        await AssertValidationError(response, "fcmToken");
    }

    [Fact]
    public async Task RemoveDeviceToken_AbsentRow_Returns204EmptyBody()
    {
        await SeedUsersAsync(CallerUserId);
        using var client = CreateDbBackedClient(CallerUserId);
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/v1/auth/device-token")
        {
            Content = JsonContent.Create(new { fcmToken = UniqueToken("absent-delete") }),
        };

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task RegisterDeviceToken_AfterDelete_ReactivatesSameRowAndKeepsCountOne()
    {
        var fcmToken = UniqueToken("delete-register");
        var existing = UserDevice.Create(CallerUserId, fcmToken, DevicePlatform.ANDROID, DateTimeOffset.UtcNow.AddHours(-1));
        await SeedUsersAndDevicesAsync([CallerUserId], [existing]);
        using var client = CreateDbBackedClient(CallerUserId);
        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, "/v1/auth/device-token")
        {
            Content = JsonContent.Create(new { fcmToken }),
        };

        var deleteResponse = await client.SendAsync(deleteRequest);
        var registerResponse = await client.PostAsJsonAsync("/v1/auth/device-token", new { fcmToken, platform = "ANDROID" });

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var db = _fixture.CreateDbContext();
        var devices = await db.UserDevices.Where(d => d.FcmToken == fcmToken).ToListAsync();
        devices.Should().ContainSingle();
        devices[0].Id.Should().Be(existing.Id);
        devices[0].UserId.Should().Be(CallerUserId);
        devices[0].IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task RegisterDeviceToken_ActiveTokenOwnedByUserB_IsTransferredToCallerA()
    {
        var fcmToken = UniqueToken("claim");
        var existing = UserDevice.Create(OtherUserId, fcmToken, DevicePlatform.WEB, DateTimeOffset.UtcNow.AddHours(-1));
        await SeedUsersAndDevicesAsync([CallerUserId, OtherUserId], [existing]);
        using var client = CreateDbBackedClient(CallerUserId);

        var response = await client.PostAsJsonAsync("/v1/auth/device-token", new { fcmToken, platform = "WEB" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var db = _fixture.CreateDbContext();
        var devices = await db.UserDevices.Where(d => d.FcmToken == fcmToken).ToListAsync();
        devices.Should().ContainSingle();
        devices[0].Id.Should().Be(existing.Id);
        devices[0].UserId.Should().Be(CallerUserId);
        devices[0].IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task RegisterDeviceToken_InactiveCallerRowAndActiveOtherOwner_ReactivatesCallerAndLeavesSingleActiveToken()
    {
        var fcmToken = UniqueToken("reactivate-claim-edge");
        var callerDevice = UserDevice.Create(CallerUserId, fcmToken, DevicePlatform.ANDROID, DateTimeOffset.UtcNow.AddDays(-2));
        callerDevice.Deactivate();
        var otherDevice = UserDevice.Create(OtherUserId, fcmToken, DevicePlatform.IOS, DateTimeOffset.UtcNow.AddHours(-1));
        await SeedUsersAndDevicesAsync([CallerUserId, OtherUserId], [callerDevice, otherDevice]);
        using var client = CreateDbBackedClient(CallerUserId);

        var response = await client.PostAsJsonAsync("/v1/auth/device-token", new { fcmToken, platform = "ANDROID" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var db = _fixture.CreateDbContext();
        var devices = await db.UserDevices.Where(d => d.FcmToken == fcmToken).OrderBy(d => d.UserId).ToListAsync();
        devices.Should().HaveCount(2);
        devices.Single(d => d.Id == callerDevice.Id).IsActive.Should().BeTrue();
        devices.Single(d => d.Id == callerDevice.Id).UserId.Should().Be(CallerUserId);
        devices.Single(d => d.Id == otherDevice.Id).IsActive.Should().BeFalse();
        devices.Count(d => d.IsActive).Should().Be(1);
    }

    [Fact]
    public async Task RegisterDeviceToken_WithoutAuth_Returns401()
    {
        using var client = CreateDbBackedClient(CallerUserId, addAuthHeader: false);

        var response = await client.PostAsJsonAsync("/v1/auth/device-token", new { fcmToken = UniqueToken("unauth"), platform = "ANDROID" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private HttpClient CreateDbBackedClient(Guid userId, bool addAuthHeader = true)
    {
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", _fixture.ConnectionString);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<NpgsqlDataSource>();
                services.RemoveAll<DbContextOptions<IdentityDbContext>>();
                services.RemoveAll<IdentityDbContext>();
                services.RemoveAll<VietRideDbContextBase>();
                services.RemoveAll<IUnitOfWork>();

                services.AddSingleton(_ =>
                {
                    var dataSourceBuilder = new NpgsqlDataSourceBuilder(_fixture.ConnectionString);
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
                services.AddScoped<IUnitOfWork>(sp => new EfUnitOfWork(sp.GetRequiredService<VietRideDbContextBase>()));
            });
        }).CreateClient();

        if (addAuthHeader)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateInternalJwt(userId)}");
        }

        return client;
    }

    private async Task SeedUsersAsync(params Guid[] userIds)
        => await SeedUsersAndDevicesAsync(userIds, []);

    private async Task SeedUsersAndDevicesAsync(IReadOnlyCollection<Guid> userIds, IReadOnlyCollection<UserDevice> devices)
    {
        await using var db = _fixture.CreateDbContext();
        foreach (var userId in userIds)
        {
            if (await db.Users.AnyAsync(u => u.Id == userId))
                continue;

            db.Users.Add(CreateUser(userId));
        }

        await db.UserDevices.AddRangeAsync(devices);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static User CreateUser(Guid userId)
    {
        var user = User.CreateAdminPendingPassword($"device-user-{userId:N}@example.com", $"Device User {userId:N}");
        typeof(User).GetProperty(nameof(User.Id))!.GetSetMethod(nonPublic: true)!.Invoke(user, [userId]);
        return user;
    }

    private static string UniqueToken(string prefix)
        => $"fcm-{prefix}-{Guid.NewGuid():N}";

    private static async Task AssertValidationError(HttpResponseMessage response, string expectedField)
    {
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("statusCode").GetInt32().Should().Be(422);
        var error = doc.RootElement.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
        error.GetProperty("fields")
            .EnumerateArray()
            .Should()
            .Contain(field => field.GetProperty("field").GetString() == expectedField);
        doc.RootElement.TryGetProperty("meta", out _).Should().BeTrue();
    }

    private static void AssertRegisterResponse(
        JsonDocument doc,
        int expectedStatusCode,
        string expectedFcmToken,
        string expectedPlatform,
        bool expectedIsActive)
    {
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("statusCode").GetInt32().Should().Be(expectedStatusCode);
        doc.RootElement.TryGetProperty("meta", out _).Should().BeTrue();
        var data = doc.RootElement.GetProperty("data");
        data.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            ["userDeviceId", "fcmToken", "platform", "isActive"]);
        data.GetProperty("userDeviceId").GetGuid().Should().NotBeEmpty();
        data.GetProperty("fcmToken").GetString().Should().Be(expectedFcmToken);
        data.GetProperty("platform").GetString().Should().Be(expectedPlatform);
        data.GetProperty("isActive").GetBoolean().Should().Be(expectedIsActive);
    }

    private static string CreateInternalJwt(Guid userId)
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
                new Claim("role", "PASSENGER"),
            ],
            notBefore: now.AddSeconds(-5),
            expires: now.AddSeconds(120),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public sealed class IdentityDeviceEndpointFixture : IAsyncLifetime
    {
        public string ConnectionString { get; } = BuildTestDatabaseConnectionString();
        private readonly string _databaseName;
        private bool _databaseCreated;
        private NpgsqlDataSource? _dataSource;

        public IdentityDeviceEndpointFixture()
        {
            _databaseName = new NpgsqlConnectionStringBuilder(ConnectionString).Database!;
        }

        public async Task InitializeAsync()
        {
            await CreateDatabaseAsync();

            var dataSourceBuilder = new NpgsqlDataSourceBuilder(ConnectionString);
            IdentityDbContext.ConfigurePostgresEnums(dataSourceBuilder);
            _dataSource = dataSourceBuilder.Build();

            await using var db = CreateDbContext();
            await db.Database.MigrateAsync();
            await ReloadPostgresTypesAsync();
        }

        public IdentityDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql(_dataSource ?? throw new InvalidOperationException("Fixture is not initialized."))
                .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
                .Options;

            return new IdentityDbContext(options, new SystemClock());
        }

        public async Task DisposeAsync()
        {
            if (_dataSource is not null)
            {
                await _dataSource.DisposeAsync();
                _dataSource = null;
            }

            await DropDatabaseAsync();
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
            await using var connection = await (_dataSource
                ?? throw new InvalidOperationException("Fixture is not initialized."))
                .OpenConnectionAsync();
            await connection.ReloadTypesAsync();
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
            dropCommand.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)";
            await dropCommand.ExecuteNonQueryAsync();
        }

        private string BuildMaintenanceConnectionString()
        {
            var builder = new NpgsqlConnectionStringBuilder(ConnectionString)
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
                Database = $"vietride_identity_devices_{Guid.NewGuid():N}",
            };

            return builder.ConnectionString;
        }
    }
}
