using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Booking.Domain.Entities;

namespace VietRide.Booking.Infrastructure.Persistence.Configurations;

internal sealed class BookingStatsProcessedEventConfiguration
    : IEntityTypeConfiguration<BookingStatsProcessedEvent>
{
    public void Configure(EntityTypeBuilder<BookingStatsProcessedEvent> builder)
    {
        builder.ToTable("booking_stats_processed_events");

        builder.HasKey(x => new { x.EventType, x.BookingId });

        builder.Property(x => x.EventType)
            .HasColumnName("event_type")
            .HasColumnType("character varying(100)")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.BookingId)
            .HasColumnName("booking_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.ProcessedAt)
            .HasColumnName("processed_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();
    }
}
