using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql.NameTranslation;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Persistence.Outbox;

namespace VietRide.Identity.Infrastructure.Design;

/// EF Core design-time factory. Lets `dotnet ef migrations add ...` create the DbContext
/// WITHOUT booting `Program.cs` (which requires INTERNAL_JWT_SECRET ≥32 chars + full DI).
///
/// Connection string priority:
/// 1. env IDENTITY_DESIGN_CONNECTION (CI / explicit override)
/// 2. default local dev string (matches infra/docker/docker-compose.yml + .env.example)
internal sealed class IdentityDbContextDesignFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("IDENTITY_DESIGN_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=vietride_identity;Username=vietride;Password=vietride_dev";

        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.MapEnum<OutboxEventStatus>("outbox_event_status", new NpgsqlNullNameTranslator());
        IdentityDbContext.ConfigurePostgresEnums(dataSourceBuilder);
        var dataSource = dataSourceBuilder.Build();

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(dataSource, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", IdentityDbContext.SchemaName))
            .Options;

        return new IdentityDbContext(options, new SystemClock());
    }
}
