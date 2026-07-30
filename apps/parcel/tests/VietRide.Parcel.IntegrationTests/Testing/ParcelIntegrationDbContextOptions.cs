using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using VietRide.Parcel.Infrastructure;

namespace VietRide.Parcel.IntegrationTests.Testing;

internal static class ParcelIntegrationDbContextOptions
{
    public static DbContextOptions<ParcelDbContext> Create(
        NpgsqlDataSource dataSource,
        params IInterceptor?[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<ParcelDbContext>()
            .UseNpgsql(dataSource, npgsql =>
                npgsql.MigrationsHistoryTable(
                    "__ef_migrations_history",
                    ParcelDbContext.SchemaName))
            // Every scenario owns a unique data source; keep test providers out of EF's global cache.
            .EnableServiceProviderCaching(false);

        foreach (var interceptor in interceptors)
        {
            if (interceptor is not null)
                builder.AddInterceptors(interceptor);
        }

        return builder.Options;
    }
}
