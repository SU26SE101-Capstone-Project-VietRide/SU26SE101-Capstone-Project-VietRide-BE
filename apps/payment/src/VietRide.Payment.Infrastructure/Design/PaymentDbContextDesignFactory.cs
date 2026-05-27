using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.Infrastructure.Design;

/// EF Core design-time factory. Lets `dotnet ef migrations add ...` create the DbContext
/// WITHOUT booting `Program.cs` (which requires INTERNAL_JWT_SECRET ≥32 chars + full DI).
internal sealed class PaymentDbContextDesignFactory : IDesignTimeDbContextFactory<PaymentDbContext>
{
    public PaymentDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("PAYMENT_DESIGN_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=vietride_payment;Username=vietride;Password=vietride_dev";

        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", PaymentDbContext.SchemaName))
            .Options;

        return new PaymentDbContext(options, new SystemClock());
    }
}
