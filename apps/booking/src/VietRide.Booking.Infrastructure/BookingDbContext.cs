using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
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
        modelBuilder.HasPostgresEnum("booking_status", Enum.GetNames<BookingStatus>());
        modelBuilder.HasPostgresEnum(
            "booking_cancellation_reason",
            Enum.GetNames<BookingCancellationReason>());
        modelBuilder.HasPostgresEnum(
            "passenger_boarding_status",
            Enum.GetNames<PassengerBoardingStatus>());
        modelBuilder.HasPostgresEnum("trip_direction", Enum.GetNames<TripDirection>());

        // Apply all IEntityTypeConfiguration<T> defined in this assembly BEFORE base
        // (base applies snake_case naming + OutboxEvent mapping).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BookingDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }

    public static void ConfigurePostgresTypes(NpgsqlDataSourceBuilder dataSourceBuilder)
    {
        dataSourceBuilder.MapEnum<BookingStatus>("booking_status", new NpgsqlNullNameTranslator());
        dataSourceBuilder.MapEnum<BookingCancellationReason>(
            "booking_cancellation_reason",
            new NpgsqlNullNameTranslator());
        dataSourceBuilder.MapEnum<PassengerBoardingStatus>(
            "passenger_boarding_status",
            new NpgsqlNullNameTranslator());
        dataSourceBuilder.MapEnum<TripDirection>("trip_direction", new NpgsqlNullNameTranslator());
    }
}
