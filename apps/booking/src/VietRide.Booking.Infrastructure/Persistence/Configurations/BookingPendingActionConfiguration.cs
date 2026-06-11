using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.Infrastructure.Persistence.Configurations;

internal sealed class BookingPendingActionConfiguration : IEntityTypeConfiguration<BookingPendingAction>
{
    public void Configure(EntityTypeBuilder<BookingPendingAction> builder)
    {
        builder.ToTable("booking_pending_actions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.BookingId)
            .HasColumnName("booking_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.Reason)
            .HasColumnName("reason")
            .HasColumnType("booking_pending_action_reason")
            .HasConversion(r => r.ToString(), s => Enum.Parse<BookingPendingActionReason>(s))
            .IsRequired();

        builder.Property(x => x.Severity)
            .HasColumnName("severity")
            .HasColumnType("booking_pending_action_severity")
            .HasConversion(
                s => s.HasValue ? s.Value.ToString() : null,
                s => s != null ? Enum.Parse<BookingPendingActionSeverity>(s) : (BookingPendingActionSeverity?)null)
            .IsRequired(false);

        builder.Property(x => x.Deadline)
            .HasColumnName("deadline")
            .IsRequired();

        builder.Property(x => x.ResolvedAt)
            .HasColumnName("resolved_at")
            .IsRequired(false);

        builder.Property(x => x.ResolvedAction)
            .HasColumnName("resolved_action")
            .HasColumnType("booking_pending_action_resolved")
            .HasConversion(
                r => r.HasValue ? r.Value.ToString() : null,
                s => s != null ? Enum.Parse<BookingPendingActionResolved>(s) : (BookingPendingActionResolved?)null)
            .IsRequired(false);

        builder.Property(x => x.Metadata)
            .HasColumnName("metadata")
            .HasColumnType("jsonb")
            .IsRequired(false);

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Ignore(x => x.RowVersion);

        // Partial unique: only 1 active (unresolved) pending action per booking
        builder.HasIndex(x => x.BookingId)
            .HasDatabaseName("uq_booking_pending_actions_active_per_booking")
            .IsUnique()
            .HasFilter("resolved_at IS NULL");

        builder.HasIndex(x => x.Deadline)
            .HasDatabaseName("idx_booking_pending_actions_deadline_unresolved")
            .HasFilter("resolved_at IS NULL");

        builder.HasIndex(x => x.Reason)
            .HasDatabaseName("idx_booking_pending_actions_reason");

        // Relationship is defined in BookingConfiguration — redeclare FK here for completeness.
        builder.HasOne(x => x.Booking)
            .WithMany(b => b.PendingActions)
            .HasForeignKey(x => x.BookingId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
