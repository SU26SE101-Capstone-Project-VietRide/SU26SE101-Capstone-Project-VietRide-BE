using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Booking.Domain.Entities;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.Infrastructure.Persistence.Configurations;

internal sealed class BookingStatsConfiguration : IEntityTypeConfiguration<BookingStats>
{
    public void Configure(EntityTypeBuilder<BookingStats> builder)
    {
        builder.ToTable("booking_stats");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.OperatorId)
            .HasColumnName("operator_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.OperatorName)
            .HasColumnName("operator_name")
            .HasColumnType("text")
            .IsRequired(false);

        builder.Property(x => x.StatDate)
            .HasColumnName("stat_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(x => x.TripId)
            .HasColumnName("trip_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(x => x.TotalBookings)
            .HasColumnName("total_bookings")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.TotalConfirmed)
            .HasColumnName("total_confirmed")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.TotalCancelled)
            .HasColumnName("total_cancelled")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.TotalNoShow)
            .HasColumnName("total_no_show")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.TotalNoShowPassengers)
            .HasColumnName("total_no_show_passengers")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.TotalCompleted)
            .HasColumnName("total_completed")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.TotalRevenue)
            .HasColumnName("total_revenue")
            .HasColumnType("bigint")
            .HasConversion(m => m.Amount, amount => Money.FromRaw(amount))
            .HasDefaultValueSql("0")
            .IsRequired();

        builder.Property(x => x.TotalRefunded)
            .HasColumnName("total_refunded")
            .HasColumnType("bigint")
            .HasConversion(m => m.Amount, amount => Money.FromRaw(amount))
            .HasDefaultValueSql("0")
            .IsRequired();

        builder.Property(x => x.TotalSeatsBooked)
            .HasColumnName("total_seats_booked")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Ignore(x => x.CreatedAt);
        builder.Ignore(x => x.RowVersion);

        builder.HasIndex(x => new { x.OperatorId, x.StatDate })
            .HasDatabaseName("idx_booking_stats_operator_date")
            .IsDescending(false, true);
    }
}
