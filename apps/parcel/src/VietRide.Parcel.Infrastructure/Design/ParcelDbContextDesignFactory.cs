using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Infrastructure.Design;

/// EF Core design-time factory. Lets `dotnet ef migrations add ...` create the DbContext
/// WITHOUT booting `Program.cs` (which requires INTERNAL_JWT_SECRET ≥32 chars + full DI).
internal sealed class ParcelDbContextDesignFactory : IDesignTimeDbContextFactory<ParcelDbContext>
{
    public ParcelDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("PARCEL_DESIGN_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=vietride_parcel;Username=vietride;Password=vietride_dev";

        var options = new DbContextOptionsBuilder<ParcelDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", ParcelDbContext.SchemaName))
            .Options;

        return new ParcelDbContext(options, new SystemClock());
    }
}
