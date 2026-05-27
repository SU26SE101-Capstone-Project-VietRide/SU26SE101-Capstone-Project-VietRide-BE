using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.Infrastructure.Design;

/// EF Core design-time factory. Lets `dotnet ef migrations add ...` create the DbContext
/// WITHOUT booting `Program.cs` (which requires INTERNAL_JWT_SECRET ≥32 chars + full DI).
internal sealed class BookingDbContextDesignFactory : IDesignTimeDbContextFactory<BookingDbContext>
{
    public BookingDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("BOOKING_DESIGN_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=vietride_booking;Username=vietride;Password=vietride_dev";

        var options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", BookingDbContext.SchemaName))
            .Options;

        return new BookingDbContext(options, new SystemClock());
    }
}
