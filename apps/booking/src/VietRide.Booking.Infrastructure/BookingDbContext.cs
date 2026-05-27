using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Persistence;

namespace VietRide.Booking.Infrastructure;

/// Booking service EF Core context — owns schema `vietride_booking`.
public sealed class BookingDbContext : VietRideDbContextBase
{
    public const string SchemaName = "vietride_booking";

    public BookingDbContext(DbContextOptions<BookingDbContext> options, IClock clock)
        : base(options, clock)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        base.OnModelCreating(modelBuilder);
    }
}
