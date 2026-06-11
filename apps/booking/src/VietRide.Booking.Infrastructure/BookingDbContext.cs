using Microsoft.EntityFrameworkCore;
using VietRide.Booking.Domain.Entities;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Persistence;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.Infrastructure;

/// Booking service EF Core context — owns schema `vietride_booking`.
public sealed class BookingDbContext : VietRideDbContextBase
{
    public const string SchemaName = "vietride_booking";

    public DbSet<BookingEntity> Bookings => Set<BookingEntity>();
    public DbSet<Passenger> Passengers => Set<Passenger>();
    public DbSet<BookingPendingAction> BookingPendingActions => Set<BookingPendingAction>();

    public BookingDbContext(DbContextOptions<BookingDbContext> options, IClock clock)
        : base(options, clock)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        // Apply all IEntityTypeConfiguration<T> defined in this assembly BEFORE base
        // (base applies snake_case naming + OutboxEvent mapping).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BookingDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
