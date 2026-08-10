using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Infrastructure;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.IntegrationTests.Persistence;

public sealed class AdminDashboardIdentityMetricsRepositoryTests
{
    [Fact]
    public async Task Repository_UsesCurrentStateVietnamBoundariesAndOneSql()
    {
        var databaseName = $"vietride_identity_ui17_metrics_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            var fromUtc = DateTimeOffset.Parse("2026-01-31T17:00:00Z");
            var toUtcExclusive = DateTimeOffset.Parse("2026-02-01T17:00:00Z");
            var approvedA = CreateApprovedOperator("A");
            var approvedB = CreateApprovedOperator("B");
            var approvedInactive = CreateApprovedOperator("Inactive");
            approvedInactive.Deactivate();
            var pending = CreatePendingOperator("Pending");
            var suspended = CreateApprovedOperator("Suspended");
            suspended.Suspend("test", DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
            var deletedApproved = CreateApprovedOperator("Deleted");
            deletedApproved.SoftDelete(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

            var atStart = CreatePassenger("start", "+84900000001", fromUtc);
            var inside = CreatePassenger("inside", "+84900000002", toUtcExclusive.AddTicks(-1));
            var before = CreatePassenger("before", "+84900000003", fromUtc.AddTicks(-1));
            var atEnd = CreatePassenger("end", "+84900000004", toUtcExclusive);
            var locked = CreatePassenger("locked", "+84900000005", fromUtc.AddHours(1));
            locked.Lock();
            var deleted = CreatePassenger("deleted", "+84900000006", fromUtc.AddHours(2));
            deleted.SoftDelete(DateTimeOffset.Parse("2026-02-01T00:00:00Z"));
            var driver = User.CreateOperatorScopedPendingPassword(
                $"driver-{Guid.NewGuid():N}@example.com",
                PhoneNumber.Parse("+84909999999"),
                "Driver",
                UserRole.DRIVER,
                approvedA.Id);

            await using (var seed = CreateDbContext(dataSource))
            {
                await seed.Database.MigrateAsync();
                await using (var reloadConnection = await dataSource.OpenConnectionAsync())
                {
                    await reloadConnection.ReloadTypesAsync();
                }
                seed.Users.AddRange(atStart, inside, before, atEnd, locked, deleted, driver);
                seed.Operators.AddRange(
                    approvedA,
                    approvedB,
                    approvedInactive,
                    pending,
                    suspended,
                    deletedApproved);
                await seed.SaveChangesAsync();
            }

            var counter = new SelectCommandCounter();
            await using var context = CreateDbContext(dataSource, counter);
            var repository = CreateRepository(context);

            var result = await repository.GetAsync(fromUtc, toUtcExclusive);

            counter.Count.Should().Be(1);
            result.ActiveUserCount.Should().Be(2);
            result.ApprovedActiveOperatorIds.Should().Equal(
                new[] { approvedA.Id, approvedB.Id }.OrderBy(id => id));
            result.UserRoleCounts.Should().ContainSingle(item => item.Key == "DRIVER" && item.Count == 1);
            result.UserRoleCounts.Should().ContainSingle(item => item.Key == "PASSENGER" && item.Count == 5);
            result.OperatorStatusCounts.Should().ContainSingle(item => item.Key == "APPROVED" && item.Count == 3);
            result.OperatorStatusCounts.Should().ContainSingle(item => item.Key == "PENDING" && item.Count == 1);
            result.OperatorStatusCounts.Should().ContainSingle(item => item.Key == "SUSPENDED" && item.Count == 1);
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    private static IAdminDashboardIdentityMetricsRepository CreateRepository(IdentityDbContext dbContext)
    {
        var repositoryType = typeof(IdentityDbContext).Assembly.GetType(
            "VietRide.Identity.Infrastructure.Persistence.Repositories.AdminDashboardIdentityMetricsRepository",
            throwOnError: true)!;
        return (IAdminDashboardIdentityMetricsRepository)Activator.CreateInstance(repositoryType, dbContext)!;
    }

    private static User CreatePassenger(
        string suffix,
        string phone,
        DateTimeOffset lastLoginAt)
    {
        var user = User.CreatePassenger(
            $"{suffix}-{Guid.NewGuid():N}@example.com",
            PhoneNumber.Parse(phone),
            "hash",
            suffix);
        user.VerifyEmail();
        user.RecordSuccessfulLogin(new FixedClock(lastLoginAt));
        return user;
    }

    private static Operator CreateApprovedOperator(string suffix)
        => Operator.CreateApproved(
            $"Operator {suffix}",
            $"BR-{suffix}-{Guid.NewGuid():N}",
            $"TAX-{suffix}-{Guid.NewGuid():N}",
            $"{suffix.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com",
            $"contact-{suffix}",
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

    private static Operator CreatePendingOperator(string suffix)
        => Operator.CreatePending(
            $"Operator {suffix}",
            $"BR-{suffix}-{Guid.NewGuid():N}",
            $"TAX-{suffix}-{Guid.NewGuid():N}",
            $"{suffix.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com",
            $"contact-{suffix}");

    private static NpgsqlDataSource CreateDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        IdentityDbContext.ConfigurePostgresEnums(builder);
        return builder.Build();
    }

    private static IdentityDbContext CreateDbContext(
        NpgsqlDataSource dataSource,
        DbCommandInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<IdentityDbContext>()
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .UseNpgsql(dataSource, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", IdentityDbContext.SchemaName));
        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        return new IdentityDbContext(builder.Options, new SystemClock());
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string fallback =
            "Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var configured = Environment.GetEnvironmentVariable("VIETRIDE_IDENTITY_TEST_CONNECTION_STRING");
        var connectionString = string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        return new NpgsqlConnectionStringBuilder(
            connectionString.Replace("{databaseName}", databaseName, StringComparison.OrdinalIgnoreCase))
        {
            Database = databaseName,
        }.ConnectionString;
    }

    private static async Task CreateDatabaseAsync(string connectionString, string databaseName)
    {
        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" }.ConnectionString;
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\";", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string connectionString, string databaseName)
    {
        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" }.ConnectionString;
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class SelectCommandCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
            {
                Count++;
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
