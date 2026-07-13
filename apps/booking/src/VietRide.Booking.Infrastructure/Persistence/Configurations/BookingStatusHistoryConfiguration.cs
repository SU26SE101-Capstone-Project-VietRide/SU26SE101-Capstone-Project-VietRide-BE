using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Booking.Domain.Entities;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.Infrastructure.Persistence.Configurations;

internal sealed class BookingStatusHistoryConfiguration : IEntityTypeConfiguration<BookingStatusHistory>
{
    public void Configure(EntityTypeBuilder<BookingStatusHistory> builder)
    {
        builder.ToTable("booking_status_history");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasColumnType("booking_status").IsRequired();
        builder.Property(x => x.OccurredAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.ReasonCode).HasMaxLength(100);
        builder.Property(x => x.ActorUserId).HasColumnType("uuid");
        builder.Property(x => x.Source).HasMaxLength(100).IsRequired();
        builder.HasOne<BookingEntity>().WithMany().HasForeignKey(x => x.BookingId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.BookingId, x.OccurredAt, x.Id })
            .HasDatabaseName("idx_booking_status_history_booking_occurred_id");
    }
}
