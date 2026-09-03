using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Parcel.Domain.Entities;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.Infrastructure.Persistence.Configurations;

internal sealed class ParcelClaimConfiguration : IEntityTypeConfiguration<ParcelClaim>
{
    public void Configure(EntityTypeBuilder<ParcelClaim> builder)
    {
        builder.ToTable("parcel_claims", table =>
        {
            table.HasCheckConstraint("chk_parcel_claims_rate", "compensation_rate_percent BETWEEN 1 AND 100");
            table.HasCheckConstraint("chk_parcel_claims_amounts", "policy_cap_vnd > 0 AND cargo_award_vnd >= 0 AND freight_refund_vnd >= 0 AND total_award_vnd >= 0");
            table.HasCheckConstraint(
                "chk_parcel_claims_proof_status",
                "proof_status IS NULL OR proof_status IN ('VERIFIED', 'UNVERIFIED', 'NO_PROOF')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.ParcelId).HasColumnName("parcel_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.IncidentId).HasColumnName("incident_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.BeneficiaryUserId).HasColumnName("beneficiary_user_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.DeclaredValueVnd).HasColumnName("declared_value_vnd").HasColumnType("bigint");
        builder.Property(x => x.ProofStatus).HasColumnName("proof_status").HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.ProvenDirectLossVnd).HasColumnName("proven_direct_loss_vnd").HasColumnType("bigint");
        builder.Property(x => x.CompensationRatePercent).HasColumnName("compensation_rate_percent").IsRequired();
        builder.Property(x => x.PolicyCapVnd).HasColumnName("policy_cap_vnd").HasColumnType("bigint").IsRequired();
        builder.Property(x => x.CargoAwardVnd).HasColumnName("cargo_award_vnd").HasColumnType("bigint").IsRequired();
        builder.Property(x => x.FreightRefundVnd).HasColumnName("freight_refund_vnd").HasColumnType("bigint").IsRequired();
        builder.Property(x => x.TotalAwardVnd).HasColumnName("total_award_vnd").HasColumnType("bigint").IsRequired();
        builder.Property(x => x.PolicyVersion).HasColumnName("policy_version").IsRequired();
        builder.Property(x => x.NoProofFallbackMultiplier).HasColumnName("no_proof_fallback_multiplier").IsRequired();
        builder.Property(x => x.DecisionReason).HasColumnName("decision_reason").HasColumnType("text");
        builder.Property(x => x.DecidedBy).HasColumnName("decided_by").HasColumnType("uuid");
        builder.Property(x => x.DecidedAt).HasColumnName("decided_at");
        builder.Property(x => x.PayoutReferenceId).HasColumnName("payout_reference_id").HasColumnType("uuid");
        builder.Property(x => x.PaidAt).HasColumnName("paid_at");
        builder.Property(x => x.AppealReason).HasColumnName("appeal_reason").HasColumnType("text");
        builder.Property(x => x.AppealedByUserId).HasColumnName("appealed_by_user_id").HasColumnType("uuid");
        builder.Property(x => x.AppealedAt).HasColumnName("appealed_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.Ignore(x => x.RowVersion);

        builder.HasOne<ParcelEntity>().WithMany().HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ParcelIncident>().WithMany().HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.IncidentId).IsUnique();
        builder.HasIndex(x => new { x.OperatorId, x.Status, x.CreatedAt });
        builder.HasIndex(x => new { x.BeneficiaryUserId, x.CreatedAt });
    }
}
