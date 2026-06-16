using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql.NameTranslation;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Design;

/// EF Core design-time factory. Lets `dotnet ef migrations add ...` create the DbContext
/// WITHOUT booting `Program.cs` (which requires INTERNAL_JWT_SECRET ≥32 chars + full DI).
internal sealed class TripDbContextDesignFactory : IDesignTimeDbContextFactory<TripDbContext>
{
    public TripDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("TRIP_DESIGN_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=vietride_trip;Username=vietride;Password=vietride_dev";

        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.MapEnum<OutboxEventStatus>("outbox_event_status", new NpgsqlNullNameTranslator());
        TripDbContext.ConfigurePostgresEnums(dataSourceBuilder);
        var dataSource = dataSourceBuilder.Build();

        var options = new DbContextOptionsBuilder<TripDbContext>()
            .UseNpgsql(dataSource, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", TripDbContext.SchemaName))
            .Options;

        return new TripDbContext(options, new SystemClock());
    }
}
