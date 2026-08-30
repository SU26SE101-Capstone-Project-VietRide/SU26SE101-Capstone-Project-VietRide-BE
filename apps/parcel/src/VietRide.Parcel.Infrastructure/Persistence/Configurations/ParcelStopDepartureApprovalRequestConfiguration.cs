using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Parcel.Domain.Entities;

namespace VietRide.Parcel.Infrastructure.Persistence.Configurations;

internal sealed class ParcelStopDepartureApprovalRequestConfiguration
    : IEntityTypeConfiguration<ParcelStopDepartureApprovalRequest>
{
    public void Configure(EntityTypeBuilder<ParcelStopDepartureApprovalRequest> builder)
    {
        builder.ToTable("parcel_stop_departure_approval_requests", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "chk_parcel_stop_departure_approval_status",
                "status IN ('PENDING_APPROVAL', 'APPROVED', 'REJECTED', 'CANCELLED')");
            tableBuilder.HasCheckConstraint(
                "chk_parcel_stop_departure_review_audit",
                "(status = 'PENDING_APPROVAL' AND reviewed_by_user_id IS NULL AND reviewed_by_role IS NULL AND reviewed_at IS NULL) OR (status IN ('APPROVED', 'REJECTED') AND reviewed_by_user_id IS NOT NULL AND reviewed_by_role IS NOT NULL AND reviewed_at IS NOT NULL) OR (status = 'CANCELLED' AND reviewed_by_user_id IS NULL AND reviewed_by_role = 'SYSTEM' AND reviewed_at IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.TripId).HasColumnName("trip_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.StopId).HasColumnName("stop_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.UnresolvedParcelIdsJson).HasColumnName("unresolved_parcel_ids_json").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.DepartureOverrideReason).HasColumnName("departure_override_reason").HasColumnType("text").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.RequestedByRole).HasColumnName("requested_by_role").HasMaxLength(32).IsRequired();
        builder.Property(x => x.RequestedAt).HasColumnName("requested_at").IsRequired();
        builder.Property(x => x.ReviewedByUserId).HasColumnName("reviewed_by_user_id").HasColumnType("uuid");
        builder.Property(x => x.ReviewedByRole).HasColumnName("reviewed_by_role").HasMaxLength(32);
        builder.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
        builder.Property(x => x.ReviewNote).HasColumnName("review_note").HasColumnType("text");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(0).IsConcurrencyToken();

        builder.HasIndex(x => x.IdempotencyKey)
            .HasDatabaseName("uq_parcel_stop_departure_approval_idempotency")
            .IsUnique();
        builder.HasIndex(x => new { x.TripId, x.StopId, x.Status })
            .HasDatabaseName("uq_parcel_stop_departure_approval_pending")
            .IsUnique()
            .HasFilter("status = 'PENDING_APPROVAL'");
        builder.HasIndex(x => new { x.OperatorId, x.Status, x.CreatedAt })
            .HasDatabaseName("idx_parcel_stop_departure_approval_operator_status");
        builder.HasIndex(x => new { x.TripId, x.StopId, x.CreatedAt })
            .HasDatabaseName("idx_parcel_stop_departure_approval_trip_stop");
    }
}
