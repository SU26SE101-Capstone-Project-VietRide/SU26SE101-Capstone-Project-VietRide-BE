using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Payment.Domain.Entities;

namespace VietRide.Payment.Infrastructure.Persistence.Configurations;

internal sealed class RefundFailureLogConfiguration : IEntityTypeConfiguration<RefundFailureLog>
{
    public void Configure(EntityTypeBuilder<RefundFailureLog> builder)
    {
        builder.ToTable("refund_failure_logs", table =>
        {
            table.HasCheckConstraint(
                "chk_refund_failure_logs_target_exists",
                "booking_id IS NOT NULL OR parcel_id IS NOT NULL");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.BookingId)
            .HasColumnName("booking_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(x => x.ParcelId)
            .HasColumnName("parcel_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(x => x.TriggerEventType)
            .HasColumnName("trigger_event_type")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.FailureReason)
            .HasColumnName("failure_reason")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.RetryCount)
            .HasColumnName("retry_count")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.LastAttemptAt)
            .HasColumnName("last_attempt_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(x => x.ResolvedAt)
            .HasColumnName("resolved_at")
            .IsRequired(false);

        builder.Property(x => x.ResolvedByUserId)
            .HasColumnName("resolved_by_user_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Ignore(x => x.UpdatedAt);
        builder.Ignore(x => x.RowVersion);
        builder.Ignore(x => x.IsResolved);
        builder.Ignore(x => x.CanRetry);
        builder.Ignore(x => x.IsRetryExhausted);

        builder.HasIndex(x => x.LastAttemptAt)
            .HasDatabaseName("idx_refund_failure_logs_unresolved")
            .HasFilter("resolved_at IS NULL");

        builder.HasIndex(x => x.BookingId)
            .HasDatabaseName("idx_refund_failure_logs_booking_id")
            .HasFilter("booking_id IS NOT NULL");

        builder.HasIndex(x => x.ParcelId)
            .HasDatabaseName("idx_refund_failure_logs_parcel_id")
            .HasFilter("parcel_id IS NOT NULL");

        builder.HasIndex(x => x.ResolvedByUserId)
            .HasDatabaseName("idx_refund_failure_logs_resolved_by_user_id")
            .HasFilter("resolved_by_user_id IS NOT NULL");
    }
}
