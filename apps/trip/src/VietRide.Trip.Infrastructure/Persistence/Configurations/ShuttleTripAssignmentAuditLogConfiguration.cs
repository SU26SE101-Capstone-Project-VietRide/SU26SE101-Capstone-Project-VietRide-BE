using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Infrastructure.Persistence.Configurations;

internal sealed class ShuttleTripAssignmentAuditLogConfiguration
    : IEntityTypeConfiguration<ShuttleTripAssignmentAuditLog>
{
    public void Configure(EntityTypeBuilder<ShuttleTripAssignmentAuditLog> builder)
    {
        builder.ToTable("shuttle_trip_assignment_audit_logs", table =>
            table.HasCheckConstraint(
                "chk_shuttle_trip_assignment_audit_logs_action",
                "action IN ('INITIAL_ASSIGNED', 'REASSIGNED')"));
        builder.HasKey(x => x.Id).HasName("pk_shuttle_trip_assignment_audit_logs");
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.ShuttleTripId).HasColumnName("shuttle_trip_id");
        builder.Property(x => x.OperatorId).HasColumnName("operator_id");
        builder.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(x => x.Action).HasColumnName("action").HasMaxLength(32);
        builder.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at");
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAdd();
        builder.HasOne<ShuttleTrip>()
            .WithMany()
            .HasForeignKey(x => x.ShuttleTripId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.OperatorId, x.ShuttleTripId, x.OccurredAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("idx_shuttle_assignment_audit_operator_trip_occurred");
    }
}
