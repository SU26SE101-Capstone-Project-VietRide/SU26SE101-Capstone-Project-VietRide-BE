using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Configurations;

public sealed class TripAuditLogConfiguration : IEntityTypeConfiguration<TripAuditLog>
{
    public void Configure(EntityTypeBuilder<TripAuditLog> builder)
    {
        builder.ToTable("trip_audit_logs");
        builder.HasKey(auditLog => auditLog.Id).HasName("pk_trip_audit_logs");

        builder.Property(auditLog => auditLog.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();
        builder.Property(auditLog => auditLog.TripId)
            .HasColumnName("trip_id")
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

        builder.HasOne<Domain.Entities.Trip>()
            .WithMany()
            .HasForeignKey(auditLog => auditLog.TripId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasIndex(auditLog => new { auditLog.TripId, auditLog.OccurredAt })
            .IsDescending(false, true)
            .HasDatabaseName("idx_trip_audit_logs_trip_occurred");
        builder.HasIndex(auditLog => new { auditLog.ActorUserId, auditLog.OccurredAt })
            .IsDescending(false, true)
            .HasFilter("actor_user_id IS NOT NULL")
            .HasDatabaseName("idx_trip_audit_logs_actor_occurred");
        builder.HasIndex(auditLog => new { auditLog.Action, auditLog.OccurredAt })
            .IsDescending(false, true)
            .HasDatabaseName("idx_trip_audit_logs_action_occurred");
    }
}
