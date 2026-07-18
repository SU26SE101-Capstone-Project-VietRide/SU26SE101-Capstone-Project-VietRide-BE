using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Identity.Domain.Entities;

namespace VietRide.Identity.Infrastructure.Persistence.Configurations;

public sealed class ActivityLogConfiguration : IEntityTypeConfiguration<ActivityLog>
{
    public void Configure(EntityTypeBuilder<ActivityLog> builder)
    {
        builder.ToTable("activity_logs");

        builder.HasKey(activityLog => activityLog.Id);

        builder.Property(activityLog => activityLog.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(activityLog => activityLog.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(activityLog => activityLog.Action)
            .HasColumnName("action")
            .HasColumnType("activity_log_action")
            .IsRequired();

        builder.Property(activityLog => activityLog.Metadata)
            .HasColumnName("metadata")
            .HasColumnType("jsonb");

        builder.Property(activityLog => activityLog.IpAddress)
            .HasColumnName("ip_address")
            .HasMaxLength(45);

        builder.Property(activityLog => activityLog.UserAgent)
            .HasColumnName("user_agent")
            .HasMaxLength(500);

        builder.Property(activityLog => activityLog.SourceEventId)
            .HasColumnName("source_event_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(activityLog => activityLog.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasOne(activityLog => activityLog.Actor)
            .WithMany()
            .HasForeignKey(activityLog => activityLog.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(activityLog => new { activityLog.UserId, activityLog.CreatedAt })
            .HasDatabaseName("idx_activity_logs_user_id_created_at")
            .IsDescending(false, true);

        builder.HasIndex(activityLog => new { activityLog.Action, activityLog.CreatedAt })
            .HasDatabaseName("idx_activity_logs_action_created_at")
            .IsDescending(false, true);

        builder.HasIndex(activityLog => new { activityLog.CreatedAt, activityLog.Id })
            .HasDatabaseName("idx_activity_logs_created_at_id")
            .IsDescending(true, true);

        builder.HasIndex(activityLog => activityLog.SourceEventId)
            .HasDatabaseName("uq_activity_logs_source_event_id")
            .IsUnique()
            .HasFilter("source_event_id IS NOT NULL");
    }
}
