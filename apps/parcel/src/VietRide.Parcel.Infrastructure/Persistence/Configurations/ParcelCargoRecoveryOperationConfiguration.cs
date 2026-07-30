using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Parcel.Domain.Entities;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.Infrastructure.Persistence.Configurations;

internal sealed class ParcelCargoRecoveryOperationConfiguration
    : IEntityTypeConfiguration<ParcelCargoRecoveryOperation>
{
    public void Configure(EntityTypeBuilder<ParcelCargoRecoveryOperation> builder)
    {
        builder.ToTable("parcel_cargo_recovery_operations", table =>
        {
            table.HasCheckConstraint(
                "chk_parcel_cargo_recovery_operation_type",
                "operation_type IN ('TRANSFER', 'RETURN')");
            table.HasCheckConstraint(
                "chk_parcel_cargo_recovery_status",
                "status IN ('PENDING', 'COMPLETED', 'FAILED')");
            table.HasCheckConstraint(
                "chk_parcel_cargo_recovery_target",
                """
                (operation_type = 'TRANSFER' AND target_trip_id IS NOT NULL AND target_state = 'RESERVED')
                OR (operation_type = 'RETURN' AND target_trip_id IS NULL AND target_state IS NULL)
                """);
            table.HasCheckConstraint(
                "chk_parcel_cargo_recovery_amounts",
                "refund_amount_vnd >= 0 AND refund_due_vnd >= 0");
            table.HasCheckConstraint(
                "chk_parcel_cargo_recovery_completion",
                """
                (status = 'PENDING' AND completed_at IS NULL AND failure_code IS NULL)
                OR (status = 'COMPLETED' AND completed_at IS NOT NULL AND failure_code IS NULL)
                OR (status = 'FAILED' AND completed_at IS NOT NULL AND failure_code IS NOT NULL)
                """);
        });

        builder.HasKey(operation => operation.Id);
        builder.Property(operation => operation.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();
        builder.Property(operation => operation.ParcelId)
            .HasColumnName("parcel_id")
            .HasColumnType("uuid")
            .IsRequired();
        builder.Property(operation => operation.OperatorId)
            .HasColumnName("operator_id")
            .HasColumnType("uuid")
            .IsRequired();
        builder.Property(operation => operation.OperationType)
            .HasColumnName("operation_type")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(operation => operation.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(operation => operation.SourceTripId)
            .HasColumnName("source_trip_id")
            .HasColumnType("uuid")
            .IsRequired();
        builder.Property(operation => operation.TargetTripId)
            .HasColumnName("target_trip_id")
            .HasColumnType("uuid")
            .IsRequired(false);
        builder.Property(operation => operation.TargetState)
            .HasColumnName("target_state")
            .HasMaxLength(16)
            .IsRequired(false);
        builder.Property(operation => operation.ActorUserId)
            .HasColumnName("actor_user_id")
            .HasColumnType("uuid")
            .IsRequired();
        builder.Property(operation => operation.Reason)
            .HasColumnName("reason")
            .HasMaxLength(500)
            .IsRequired();
        builder.Property(operation => operation.RefundAmountVnd)
            .HasColumnName("refund_amount_vnd")
            .HasDefaultValue(0L)
            .IsRequired();
        builder.Property(operation => operation.RefundDueVnd)
            .HasColumnName("refund_due_vnd")
            .HasDefaultValue(0L)
            .IsRequired();
        builder.Property(operation => operation.SourceStatus)
            .HasColumnName("source_status")
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();
        builder.Property(operation => operation.IsStatusOverride)
            .HasColumnName("is_status_override")
            .HasDefaultValue(false)
            .IsRequired();
        builder.Property(operation => operation.ClaimedAt)
            .HasColumnName("claimed_at")
            .IsRequired();
        builder.Property(operation => operation.CompletedAt)
            .HasColumnName("completed_at")
            .IsRequired(false);
        builder.Property(operation => operation.FailureCode)
            .HasColumnName("failure_code")
            .HasMaxLength(64)
            .IsRequired(false);
        builder.Property(operation => operation.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();
        builder.Property(operation => operation.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();
        builder.Ignore(operation => operation.RowVersion);

        builder.HasOne<ParcelEntity>()
            .WithMany()
            .HasForeignKey(operation => operation.ParcelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(operation => operation.ParcelId)
            .HasDatabaseName("uq_parcel_cargo_recovery_operations_active_parcel")
            .HasFilter("status = 'PENDING'")
            .IsUnique();
        builder.HasIndex(operation => new { operation.ClaimedAt, operation.Id })
            .HasDatabaseName("idx_parcel_cargo_recovery_operations_stale")
            .HasFilter("status = 'PENDING'");
    }
}
