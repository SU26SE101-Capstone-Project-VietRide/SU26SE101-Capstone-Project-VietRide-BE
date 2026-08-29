using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Parcel.Domain.Entities;

namespace VietRide.Parcel.Infrastructure.Persistence.Configurations;

internal sealed class ParcelClaimAppealConfiguration : IEntityTypeConfiguration<ParcelClaimAppeal>
{
    public void Configure(EntityTypeBuilder<ParcelClaimAppeal> builder)
    {
        builder.ToTable("parcel_claim_appeals", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "chk_parcel_claim_appeal_status",
                "status IN ('SUBMITTED', 'UNDER_REVIEW', 'UPHELD', 'ADJUSTMENT_APPROVED', 'FUNDING_PENDING', 'PAID')");
            tableBuilder.HasCheckConstraint(
                "chk_parcel_claim_appeal_awards",
                "original_total_award_vnd >= 0 AND revised_cargo_award_vnd >= 0 AND revised_freight_refund_vnd >= 0 AND revised_total_award_vnd >= 0 AND supplementary_award_vnd >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.ClaimId).HasColumnName("claim_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.ParcelId).HasColumnName("parcel_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.IncidentId).HasColumnName("incident_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.BeneficiaryUserId).HasColumnName("beneficiary_user_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.OriginalClaimStatus).HasColumnName("original_claim_status").HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.OriginalTotalAwardVnd).HasColumnName("original_total_award_vnd").HasColumnType("bigint").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasColumnType("text").IsRequired();
        builder.Property(x => x.SubmittedByUserId).HasColumnName("submitted_by_user_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.SubmittedAt).HasColumnName("submitted_at").IsRequired();
        builder.Property(x => x.RevisedProvenDirectLossVnd).HasColumnName("revised_proven_direct_loss_vnd").HasColumnType("bigint");
        builder.Property(x => x.RevisedCargoAwardVnd).HasColumnName("revised_cargo_award_vnd").HasColumnType("bigint").IsRequired();
        builder.Property(x => x.RevisedFreightRefundVnd).HasColumnName("revised_freight_refund_vnd").HasColumnType("bigint").IsRequired();
        builder.Property(x => x.RevisedTotalAwardVnd).HasColumnName("revised_total_award_vnd").HasColumnType("bigint").IsRequired();
        builder.Property(x => x.SupplementaryAwardVnd).HasColumnName("supplementary_award_vnd").HasColumnType("bigint").IsRequired();
        builder.Property(x => x.DecisionReason).HasColumnName("decision_reason").HasColumnType("text");
        builder.Property(x => x.DecidedByUserId).HasColumnName("decided_by_user_id").HasColumnType("uuid");
        builder.Property(x => x.DecidedAt).HasColumnName("decided_at");
        builder.Property(x => x.PayoutReferenceId).HasColumnName("payout_reference_id").HasColumnType("uuid");
        builder.Property(x => x.PaidAt).HasColumnName("paid_at");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(0).IsConcurrencyToken();

        builder.HasOne<ParcelClaim>().WithMany().HasForeignKey(x => x.ClaimId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ParcelIncident>().WithMany().HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.ClaimId).HasDatabaseName("uq_parcel_claim_appeals_claim").IsUnique();
        builder.HasIndex(x => x.IdempotencyKey).HasDatabaseName("uq_parcel_claim_appeals_idempotency").IsUnique();
        builder.HasIndex(x => new { x.OperatorId, x.Status, x.CreatedAt })
            .HasDatabaseName("idx_parcel_claim_appeals_operator_status");
    }
}
