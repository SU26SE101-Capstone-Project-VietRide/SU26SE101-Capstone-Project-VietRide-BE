using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Infrastructure;
using VietRide.Identity.Infrastructure.DependencyInjection;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Identity.IntegrationTests.Persistence;

public sealed class UserDeviceRepositoryTests : IClassFixture<UserDeviceRepositoryTests.IdentityPersistenceFixture>
{
    private readonly IdentityPersistenceFixture _fixture;

    public UserDeviceRepositoryTests(IdentityPersistenceFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task FindByUserAndFcmTokenAsync_FindsInactiveSameUserRow()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IUserDeviceRepository>();
        var user = User.CreateAdminPendingPassword(UniqueEmail("inactive-device-user"), "Inactive Device User");
        var otherUser = User.CreateAdminPendingPassword(UniqueEmail("inactive-device-other"), "Inactive Device Other");
        var fcmToken = $"fcm-token-reused-by-same-user-{Guid.NewGuid():N}";
        var inactiveDevice = UserDevice.Create(user.Id, fcmToken, DevicePlatform.ANDROID, DateTimeOffset.UtcNow);
        inactiveDevice.Deactivate();
        var otherUserDevice = UserDevice.Create(otherUser.Id, fcmToken, DevicePlatform.IOS, DateTimeOffset.UtcNow);

        await db.Users.AddRangeAsync(user, otherUser);
        await db.UserDevices.AddRangeAsync(inactiveDevice, otherUserDevice);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await repository.FindByUserAndFcmTokenAsync(user.Id, fcmToken);

        result.Should().NotBeNull();
        result!.Id.Should().Be(inactiveDevice.Id);
        result.UserId.Should().Be(user.Id);
        result.FcmToken.Should().Be(fcmToken);
        result.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task FindByFcmTokenAsync_IsGlobalActiveOnly()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IUserDeviceRepository>();
        var inactiveUser = User.CreateAdminPendingPassword(UniqueEmail("global-token-inactive"), "Global Token Inactive");
        var activeUser = User.CreateAdminPendingPassword(UniqueEmail("global-token-active"), "Global Token Active");
        var otherUser = User.CreateAdminPendingPassword(UniqueEmail("global-token-other"), "Global Token Other");
        var fcmToken = $"globally-claimed-token-{Guid.NewGuid():N}";
        var activeDevice = UserDevice.Create(activeUser.Id, fcmToken, DevicePlatform.IOS, DateTimeOffset.UtcNow);
        var inactiveDevice = UserDevice.Create(inactiveUser.Id, fcmToken, DevicePlatform.ANDROID, DateTimeOffset.UtcNow);
        inactiveDevice.Deactivate();
        var unrelatedDevice = UserDevice.Create(otherUser.Id, $"different-token-{Guid.NewGuid():N}", DevicePlatform.WEB, DateTimeOffset.UtcNow);

        await db.Users.AddRangeAsync(inactiveUser, activeUser, otherUser);
        await db.UserDevices.AddRangeAsync(inactiveDevice, activeDevice, unrelatedDevice);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await repository.FindByFcmTokenAsync(fcmToken);

        result.Should().NotBeNull();
        result!.Id.Should().Be(activeDevice.Id);
        result.UserId.Should().Be(activeUser.Id);
        result.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task ListActiveByUserIdAsync_ExcludesInactiveRows()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IUserDeviceRepository>();
        var user = User.CreateAdminPendingPassword(UniqueEmail("list-active-user"), "List Active User");
        var otherUser = User.CreateAdminPendingPassword(UniqueEmail("list-active-other"), "List Active Other");
        var activeDevice = UserDevice.Create(user.Id, $"active-token-{Guid.NewGuid():N}", DevicePlatform.WEB, DateTimeOffset.UtcNow);
        var inactiveDevice = UserDevice.Create(user.Id, $"inactive-token-{Guid.NewGuid():N}", DevicePlatform.IOS, DateTimeOffset.UtcNow);
        inactiveDevice.Deactivate();
        var otherUserDevice = UserDevice.Create(otherUser.Id, $"other-user-token-{Guid.NewGuid():N}", DevicePlatform.ANDROID, DateTimeOffset.UtcNow);

        await db.Users.AddRangeAsync(user, otherUser);
        await db.UserDevices.AddRangeAsync(activeDevice, inactiveDevice, otherUserDevice);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await repository.ListActiveByUserIdAsync(user.Id);

        result.Should().ContainSingle().Which.Id.Should().Be(activeDevice.Id);
        result.Should().NotContain(device => device.Id == inactiveDevice.Id);
        result.Should().NotContain(device => device.Id == otherUserDevice.Id);
    }

    private static string UniqueEmail(string prefix)
        => $"{prefix}-{Guid.NewGuid():N}@example.com";

    public sealed class IdentityPersistenceFixture : IAsyncLifetime
    {
        private readonly string _connectionString = BuildTestDatabaseConnectionString();
        private readonly string _databaseName;
        private bool _databaseCreated;
        private NpgsqlDataSource? _dataSource;
        private ServiceProvider? _provider;

        public IdentityPersistenceFixture()
        {
            _databaseName = new NpgsqlConnectionStringBuilder(_connectionString).Database!;
        }

        public async Task InitializeAsync()
        {
            await CreateDatabaseAsync();

            var dataSourceBuilder = new NpgsqlDataSourceBuilder(_connectionString);
            IdentityDbContext.ConfigurePostgresEnums(dataSourceBuilder);
            _dataSource = dataSourceBuilder.Build();

            var services = new ServiceCollection();
            services.AddSingleton<IClock, SystemClock>();
            services.AddDbContext<IdentityDbContext>(options => options
                .EnableServiceProviderCaching(false)
                .ConfigureWarnings(warnings => warnings.Ignore(
                    Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
                .UseNpgsql(_dataSource));
            services.AddInfrastructure(BuildConfiguration());
            _provider = services.BuildServiceProvider(validateScopes: true);

            await using var scope = CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.Database.MigrateAsync();
            await ReloadPostgresTypesAsync();
        }

        public AsyncServiceScope CreateScope()
            => (_provider ?? throw new InvalidOperationException("Fixture is not initialized.")).CreateAsyncScope();

        public async Task DisposeAsync()
        {
            if (_provider is not null)
            {
                await _provider.DisposeAsync();
            }

            if (_dataSource is not null)
            {
                await _dataSource.DisposeAsync();
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
            dropCommand.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\"";
            await dropCommand.ExecuteNonQueryAsync();
        }

        private string BuildMaintenanceConnectionString()
        {
            var builder = new NpgsqlConnectionStringBuilder(_connectionString)
            {
                Database = "postgres",
            };

            return builder.ConnectionString;
        }

        private static IConfiguration BuildConfiguration()
            => new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["REDIS_URL"] = "localhost:6379,abortConnect=false",
                })
                .Build();

        private static string BuildTestDatabaseConnectionString()
        {
            var configured = Environment.GetEnvironmentVariable("VIETRIDE_IDENTITY_TEST_CONNECTION_STRING")
                ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                ?? "Host=localhost;Port=5432;Database=vietride_identity_tests;Username=vietride;Password=vietride_dev";
            var builder = new NpgsqlConnectionStringBuilder(configured)
            {
                Database = $"vietride_identity_task5_{Guid.NewGuid():N}",
            };

            return builder.ConnectionString;
        }
    }
}
