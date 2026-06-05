using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Infrastructure.Seed;

namespace VietRide.Identity.UnitTests.Infrastructure;

public sealed class BootstrapAdminSeederTests
{
    [Fact]
    public async Task SeedAsync_WhenSystemAdminExists_SkipsWithoutRequiringEnvironment()
    {
        var store = new InMemorySystemAdminBootstrapStore(existingSystemAdmin: true);
        var seeder = BuildSeeder(store, new Dictionary<string, string?>());

        await seeder.SeedAsync(CancellationToken.None);

        store.InsertCallCount.Should().Be(0);
        store.Users.Should().HaveCount(1);
    }

    [Fact]
    public async Task SeedAsync_WhenNoSystemAdminAndEmailMissing_ThrowsBeforeInsert()
    {
        var store = new InMemorySystemAdminBootstrapStore();
        var seeder = BuildSeeder(store, new Dictionary<string, string?>
        {
            ["SYSTEM_ADMIN_BOOTSTRAP_PASSWORD"] = "StrongPassword123!",
        });

        var act = () => seeder.SeedAsync(CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<InvalidOperationException>();
        assertion.Which.Message.Should().Contain("SYSTEM_ADMIN_BOOTSTRAP_EMAIL");
        store.InsertCallCount.Should().Be(0);
        store.Users.Should().BeEmpty();
    }

    [Fact]
    public async Task SeedAsync_WhenNoSystemAdmin_InsertsActiveSystemAdminWithCost12BCryptHash()
    {
        var store = new InMemorySystemAdminBootstrapStore();
        var password = "StrongPassword123!";
        var seeder = BuildSeeder(store, new Dictionary<string, string?>
        {
            ["SYSTEM_ADMIN_BOOTSTRAP_EMAIL"] = " Admin@Example.com ",
            ["SYSTEM_ADMIN_BOOTSTRAP_PASSWORD"] = $" {password} ",
            ["SYSTEM_ADMIN_BOOTSTRAP_DISPLAY_NAME"] = " Root Admin ",
        });

        await seeder.SeedAsync(CancellationToken.None);

        store.Users.Should().ContainSingle();
        var inserted = store.Users.Single();
        inserted.Email.Should().Be("Admin@Example.com");
        inserted.DisplayName.Should().Be("Root Admin");
        inserted.Role.Should().Be(UserRole.SYSTEM_ADMIN);
        inserted.Status.Should().Be(UserStatus.ACTIVE);
        inserted.PasswordHash.Should().NotBeNullOrWhiteSpace();
        inserted.PasswordHash.Split('$')[2].Should().Be("12");
        BCrypt.Net.BCrypt.Verify(password, inserted.PasswordHash).Should().BeTrue();
    }

    [Fact]
    public async Task SeedAsync_WhenRunTwice_DoesNotCreateDuplicateSystemAdmin()
    {
        var store = new InMemorySystemAdminBootstrapStore();
        var seeder = BuildSeeder(store, new Dictionary<string, string?>
        {
            ["SYSTEM_ADMIN_BOOTSTRAP_EMAIL"] = "admin@example.com",
            ["SYSTEM_ADMIN_BOOTSTRAP_PASSWORD"] = "StrongPassword123!",
        });

        await seeder.SeedAsync(CancellationToken.None);
        await seeder.SeedAsync(CancellationToken.None);

        store.Users.Should().ContainSingle(user => user.Role == UserRole.SYSTEM_ADMIN);
        store.InsertCallCount.Should().Be(1);
    }

    private static BootstrapAdminSeeder BuildSeeder(
        InMemorySystemAdminBootstrapStore store,
        IReadOnlyDictionary<string, string?> configurationValues)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build();
        var logger = Substitute.For<ILogger<BootstrapAdminSeeder>>();

        return new BootstrapAdminSeeder(store, configuration, logger);
    }

    private sealed class InMemorySystemAdminBootstrapStore : ISystemAdminBootstrapStore
    {
        private readonly List<SystemAdminBootstrapUser> _users = new();

        public InMemorySystemAdminBootstrapStore(bool existingSystemAdmin = false)
        {
            if (existingSystemAdmin)
            {
                _users.Add(new SystemAdminBootstrapUser(
                    "existing@example.com",
                    "existing-hash",
                    "Existing Admin",
                    UserRole.SYSTEM_ADMIN,
                    UserStatus.ACTIVE));
            }
        }

        public int InsertCallCount { get; private set; }

        public IReadOnlyCollection<SystemAdminBootstrapUser> Users => _users;

        public Task<bool> HasSystemAdminAsync(CancellationToken ct)
            => Task.FromResult(_users.Any(user => user.Role == UserRole.SYSTEM_ADMIN));

        public Task<bool> InsertIfMissingAsync(SystemAdminBootstrapUser user, CancellationToken ct)
        {
            InsertCallCount++;

            if (_users.Any(existing => existing.Role == UserRole.SYSTEM_ADMIN))
                return Task.FromResult(false);

            _users.Add(user);
            return Task.FromResult(true);
        }
    }
}
