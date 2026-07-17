using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Booking.Domain.Entities;

namespace VietRide.Booking.Infrastructure.Persistence.Configurations;

internal sealed class BookingStationRedirectConfiguration : IEntityTypeConfiguration<BookingStationRedirect>
{
    public void Configure(EntityTypeBuilder<BookingStationRedirect> builder)
    {
        builder.ToTable("booking_station_redirects", table =>
            table.HasCheckConstraint(
                "chk_booking_station_redirects_not_self",
                "duplicate_station_id <> canonical_station_id"));
        builder.HasKey(redirect => redirect.DuplicateStationId)
            .HasName("pk_booking_station_redirects");
        builder.Property(redirect => redirect.DuplicateStationId)
            .HasColumnName("duplicate_station_id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();
        builder.Property(redirect => redirect.CanonicalStationId)
            .HasColumnName("canonical_station_id")
            .HasColumnType("uuid")
            .IsRequired();
        builder.Property(redirect => redirect.SourceEventId)
            .HasColumnName("source_event_id")
            .HasColumnType("uuid")
            .IsRequired();
        builder.Property(redirect => redirect.OccurredAt)
            .HasColumnName("occurred_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(redirect => redirect.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();
        builder.Property(redirect => redirect.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();
        builder.HasIndex(redirect => redirect.SourceEventId)
            .IsUnique()
            .HasDatabaseName("uq_booking_station_redirects_source_event");
        builder.HasIndex(redirect => redirect.CanonicalStationId)
            .HasDatabaseName("idx_booking_station_redirects_canonical");
    }
}
