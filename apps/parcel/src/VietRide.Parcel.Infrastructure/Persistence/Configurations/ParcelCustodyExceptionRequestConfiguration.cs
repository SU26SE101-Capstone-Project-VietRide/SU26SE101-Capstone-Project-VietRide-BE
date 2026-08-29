using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.Infrastructure.Persistence.Configurations;

internal sealed class ParcelCustodyExceptionRequestConfiguration
    : IEntityTypeConfiguration<ParcelCustodyExceptionRequest>
{
    public void Configure(EntityTypeBuilder<ParcelCustodyExceptionRequest> builder)
    {
        builder.ToTable("parcel_custody_exception_requests", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "chk_parcel_custody_exception_request_status",
                "status IN ('PENDING_APPROVAL', 'APPROVED', 'REJECTED', 'CANCELLED')");
            tableBuilder.HasCheckConstraint(
                "chk_parcel_custody_exception_review_audit",
                "(status = 'PENDING_APPROVAL' AND reviewed_by_user_id IS NULL AND reviewed_by_role IS NULL AND reviewed_at IS NULL) OR (status <> 'PENDING_APPROVAL' AND reviewed_by_user_id IS NOT NULL AND reviewed_by_role IS NOT NULL AND reviewed_at IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.ParcelId).HasColumnName("parcel_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.IncidentId).HasColumnName("incident_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.TripId).HasColumnName("trip_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.IncidentType).HasColumnName("incident_type").HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(x => x.ActualLocationType).HasColumnName("actual_location_type").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.ActualLocationId).HasColumnName("actual_location_id").HasColumnType("uuid");
        builder.Property(x => x.LocationSnapshot).HasColumnName("location_snapshot").HasMaxLength(500);
        builder.Property(x => x.TemporaryExceptionTag).HasColumnName("temporary_exception_tag").HasMaxLength(100);
        builder.Property(x => x.Description).HasColumnName("description").HasColumnType("text");
        builder.Property(x => x.ObservedWeightKg).HasColumnName("observed_weight_kg").HasColumnType("numeric(10,3)");
        builder.Property(x => x.EvidenceReferencesJson).HasColumnName("evidence_references_json").HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb").IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasColumnType("text").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.ReportedByUserId).HasColumnName("reported_by_user_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.ReportedByRole).HasColumnName("reported_by_role").HasMaxLength(32).IsRequired();
        builder.Property(x => x.ReportedAt).HasColumnName("reported_at").IsRequired();
        builder.Property(x => x.ReviewedByUserId).HasColumnName("reviewed_by_user_id").HasColumnType("uuid");
        builder.Property(x => x.ReviewedByRole).HasColumnName("reviewed_by_role").HasMaxLength(32);
        builder.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
        builder.Property(x => x.ReviewNote).HasColumnName("review_note").HasColumnType("text");
        builder.Property(x => x.ApprovedCustodyEventId).HasColumnName("approved_custody_event_id").HasColumnType("uuid");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.Property(x => x.RowVersion)
            .HasColumnName("row_version")
            .HasDefaultValue(0)
            .IsConcurrencyToken();

        builder.HasOne<ParcelEntity>().WithMany().HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ParcelIncident>().WithMany().HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ParcelCustodyEvent>().WithMany().HasForeignKey(x => x.ApprovedCustodyEventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.IncidentId)
            .HasDatabaseName("uq_parcel_custody_exception_requests_incident")
            .IsUnique();
        builder.HasIndex(x => x.IdempotencyKey)
            .HasDatabaseName("uq_parcel_custody_exception_requests_idempotency")
            .IsUnique();
        builder.HasIndex(x => x.ApprovedCustodyEventId)
            .HasDatabaseName("idx_parcel_custody_exception_requests_approved_event");
        builder.HasIndex(x => new { x.ParcelId, x.IncidentType }).IsUnique()
            .HasDatabaseName("uq_parcel_custody_exception_requests_pending_parcel_type")
            .HasFilter("status = 'PENDING_APPROVAL'");
        builder.HasIndex(x => new { x.OperatorId, x.Status, x.CreatedAt })
            .HasDatabaseName("idx_parcel_custody_exception_requests_operator_status");
        builder.HasIndex(x => new { x.TripId, x.Status, x.CreatedAt })
            .HasDatabaseName("idx_parcel_custody_exception_requests_trip_status");
    }
}
