using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Configurations;

public sealed class DriverScheduleAuditLogConfiguration : IEntityTypeConfiguration<DriverScheduleAuditLog>
{
    public void Configure(EntityTypeBuilder<DriverScheduleAuditLog> builder)
    {
        builder.ToTable("driver_schedule_audit_logs");
        builder.HasKey(auditLog => auditLog.Id).HasName("pk_driver_schedule_audit_logs");

        builder.Property(auditLog => auditLog.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();
        builder.Property(auditLog => auditLog.DriverScheduleId)
            .HasColumnName("driver_schedule_id")
            .HasColumnType("uuid")
            .IsRequired();
        builder.Property(auditLog => auditLog.ActorUserId)
            .HasColumnName("actor_user_id")
            .HasColumnType("uuid");
        builder.Property(auditLog => auditLog.Action)
            .HasColumnName("action")
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(auditLog => auditLog.Metadata)
            .HasColumnName("metadata")
            .HasColumnType("jsonb");
        builder.Property(auditLog => auditLog.OccurredAt)
            .HasColumnName("occurred_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(auditLog => auditLog.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAdd();

        builder.HasOne<DriverSchedule>()
            .WithMany()
            .HasForeignKey(auditLog => auditLog.DriverScheduleId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasIndex(auditLog => new { auditLog.DriverScheduleId, auditLog.OccurredAt })
            .IsDescending(false, true)
            .HasDatabaseName("idx_driver_schedule_audit_logs_schedule_occurred");
        builder.HasIndex(auditLog => new { auditLog.ActorUserId, auditLog.OccurredAt })
            .IsDescending(false, true)
            .HasFilter("actor_user_id IS NOT NULL")
            .HasDatabaseName("idx_driver_schedule_audit_logs_actor_occurred");
        builder.HasIndex(auditLog => new { auditLog.Action, auditLog.OccurredAt })
            .IsDescending(false, true)
            .HasDatabaseName("idx_driver_schedule_audit_logs_action_occurred");
    }
}
