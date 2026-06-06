using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Identity.Infrastructure;
using VietRide.Identity.Infrastructure.Migrations;

namespace VietRide.Identity.IntegrationTests.Persistence;

public sealed class ActivityLogPersistenceTests
{
    [Fact]
    public async Task ActivityLog_InitialPasswordActionsPersist()
    {
        var fixture = new UserDeviceRepositoryTests.IdentityPersistenceFixture();

        try
        {
            await fixture.InitializeAsync();
            await using var scope = fixture.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var user = User.CreateAdminPendingPassword(UniqueEmail("activity-log-user"), "Activity Log User");
            var setInitialPasswordLog = ActivityLog.Create(
                user.Id,
                ActivityLogAction.SET_INITIAL_PASSWORD,
                metadata: null,
                ipAddress: null,
                userAgent: null);
            var resendInitialPasswordLog = ActivityLog.Create(
                user.Id,
                ActivityLogAction.RESEND_INITIAL_PASSWORD,
                metadata: null,
                ipAddress: null,
                userAgent: null);

            await db.Users.AddAsync(user);
            await db.ActivityLogs.AddRangeAsync(setInitialPasswordLog, resendInitialPasswordLog);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var persistedActions = await db.ActivityLogs
                .Where(log => log.UserId == user.Id)
                .OrderBy(log => log.CreatedAt)
                .Select(log => log.Action)
                .ToListAsync();

            persistedActions.Should().BeEquivalentTo(new[]
            {
                ActivityLogAction.SET_INITIAL_PASSWORD,
                ActivityLogAction.RESEND_INITIAL_PASSWORD,
            });
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public void AddActivityLogActions_MigrationContainsPostgresAddValueSql()
    {
        var migration = new AddActivityLogActions();
        var operations = ReadUpSqlOperations(migration);
        var upSql = string.Join(Environment.NewLine, operations.Select(operation => operation.Sql));

        upSql.Should().Contain("ALTER TYPE activity_log_action ADD VALUE IF NOT EXISTS 'SET_INITIAL_PASSWORD';");
        upSql.Should().Contain("ALTER TYPE activity_log_action ADD VALUE IF NOT EXISTS 'RESEND_INITIAL_PASSWORD';");
        operations.Should().OnlyContain(operation => operation.SuppressTransaction);
    }

    [Fact]
    public void AddActivityLogActions_DownIsNoOp()
    {
        var migration = new AddActivityLogActions();
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        var down = typeof(Migration).GetMethod(
            "Down",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        down.Invoke(migration, new object[] { builder });

        builder.Operations.Should().BeEmpty();
    }

    private static IReadOnlyList<SqlOperation> ReadUpSqlOperations(Migration migration)
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        var up = typeof(Migration).GetMethod(
            "Up",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        up.Invoke(migration, new object[] { builder });

        return builder.Operations.OfType<SqlOperation>().ToList();
    }

    private static string UniqueEmail(string prefix)
        => $"{prefix}-{Guid.NewGuid():N}@example.com";
}
