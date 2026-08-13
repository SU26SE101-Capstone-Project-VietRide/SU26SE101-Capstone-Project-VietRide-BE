using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VietRide.Identity.Infrastructure.Migrations;

namespace VietRide.Identity.IntegrationTests.Persistence;

public sealed class OperatorUserLockSourceMigrationTests
{
    [Fact]
    public void Up_CreatesLockSourceBackfillsLegacyAndAddsPasswordChange()
    {
        var operations = ReadOperations(up: true);
        var sqlOperations = operations.OfType<SqlOperation>().ToArray();
        var sql = string.Join(Environment.NewLine, sqlOperations.Select(operation => operation.Sql));

        sql.Should().Contain("CREATE TYPE public.user_lock_source");
        sql.Should().Contain("ADD VALUE IF NOT EXISTS 'PASSWORD_CHANGE'");
        sql.Should().Contain("SET lock_source = 'LEGACY_UNKNOWN'");
        sqlOperations.Single(operation => operation.Sql.Contains("PASSWORD_CHANGE", StringComparison.Ordinal))
            .SuppressTransaction.Should().BeTrue();
        operations.OfType<AddColumnOperation>()
            .Should().ContainSingle(operation => operation.Name == "lock_source"
                && operation.Table == "users"
                && operation.Schema == "vietride_identity"
                && operation.ColumnType == "public.user_lock_source");
        operations.OfType<DropColumnOperation>()
            .Should().NotContain(operation => operation.Name == "address_district");
    }

    [Fact]
    public void Down_MapsPasswordChangeBeforeRecreatingEnumAndDropsLockSource()
    {
        var operations = ReadOperations(up: false);
        var sql = string.Join(Environment.NewLine, operations.OfType<SqlOperation>().Select(operation => operation.Sql));

        sql.Should().Contain("SET revoked_reason = 'PASSWORD_RESET' WHERE revoked_reason = 'PASSWORD_CHANGE'");
        sql.Should().Contain("DROP TYPE public.user_lock_source");
        sql.Should().Contain("CREATE TYPE public.refresh_token_revoke_reason AS ENUM");
        operations.OfType<DropColumnOperation>()
            .Should().ContainSingle(operation => operation.Name == "lock_source"
                && operation.Table == "users"
                && operation.Schema == "vietride_identity");
        operations.OfType<AddColumnOperation>()
            .Should().NotContain(operation => operation.Name == "address_district");
    }

    private static IReadOnlyList<MigrationOperation> ReadOperations(bool up)
    {
        var migration = new AddOperatorUserLockSourceAndPasswordChange();
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        var method = typeof(Migration).GetMethod(
            up ? "Up" : "Down",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        method.Invoke(migration, [builder]);
        return builder.Operations;
    }
}
