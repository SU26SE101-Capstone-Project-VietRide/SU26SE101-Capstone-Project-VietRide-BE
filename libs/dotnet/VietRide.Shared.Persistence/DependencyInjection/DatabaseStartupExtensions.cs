using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace VietRide.Shared.Persistence.DependencyInjection;

/// Startup helpers that run once per service while the host is booting.
public static class DatabaseStartupExtensions
{
    /// <summary>
    /// Applies pending EF Core migrations, then reloads the Npgsql type catalog.
    ///
    /// On a fresh database the shared singleton <c>NpgsqlDataSource</c> caches the PG type
    /// catalog on its FIRST connection — which is the <c>MigrateAsync</c> below, opened BEFORE
    /// the migration creates the native enum types (user_role, payment_status, booking_status, …).
    /// Without a reload, every subsequent query touching an enum column/parameter fails at runtime
    /// with <c>InvalidCastException: Reading as 'System.Object' is not supported for fields having
    /// DataTypeName '-'</c> (or "Cannot resolve '&lt;enum&gt;' to a fully qualified datatype name")
    /// until the process is restarted. Reloading the catalog here makes the mapped enums resolve on
    /// first boot against an empty DB.
    /// </summary>
    public static async Task MigrateAndReloadTypesAsync(
        this DbContext dbContext,
        string? targetMigration = null)
    {
        if (string.IsNullOrWhiteSpace(targetMigration))
        {
            await dbContext.Database.MigrateAsync();
        }
        else
        {
            await dbContext.GetService<IMigrator>().MigrateAsync(targetMigration.Trim());
        }

        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed)
        {
            await connection.OpenAsync();
        }

        await connection.ReloadTypesAsync();

        if (wasClosed)
        {
            await connection.CloseAsync();
        }
    }
}
