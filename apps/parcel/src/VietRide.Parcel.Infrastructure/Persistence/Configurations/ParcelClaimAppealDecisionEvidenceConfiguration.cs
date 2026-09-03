using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Parcel.Domain.Entities;

namespace VietRide.Parcel.Infrastructure.Persistence.Configurations;

internal sealed class ParcelClaimAppealDecisionEvidenceConfiguration
    : IEntityTypeConfiguration<ParcelClaimAppealDecisionEvidence>
{
    public void Configure(EntityTypeBuilder<ParcelClaimAppealDecisionEvidence> builder)
    {
        builder.ToTable("parcel_claim_appeal_decision_evidence");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.AppealId).HasColumnName("appeal_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.ClaimId).HasColumnName("claim_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.EvidenceId).HasColumnName("evidence_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.AcceptedByUserId).HasColumnName("accepted_by_user_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.AcceptedAt).HasColumnName("accepted_at").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.Ignore(x => x.RowVersion);

        builder.HasOne<ParcelClaimAppeal>()
            .WithMany()
            .HasForeignKey(x => new { x.AppealId, x.ClaimId })
            .HasPrincipalKey(x => new { x.Id, x.ClaimId })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_parcel_claim_appeal_decision_evidence_appeal");
        builder.HasOne<ParcelClaimEvidence>()
            .WithMany()
            .HasForeignKey(x => new { x.ClaimId, x.EvidenceId })
            .HasPrincipalKey(x => new { x.ClaimId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_parcel_claim_appeal_decision_evidence_claim_evidence");
        builder.HasIndex(x => new { x.AppealId, x.EvidenceId })
            .IsUnique()
            .HasDatabaseName("uq_parcel_claim_appeal_decision_evidence");
        builder.HasIndex(x => new { x.AppealId, x.ClaimId })
            .HasDatabaseName("idx_parcel_claim_appeal_decision_evidence_appeal_claim");
        builder.HasIndex(x => new { x.ClaimId, x.EvidenceId })
            .HasDatabaseName("idx_parcel_claim_appeal_decision_evidence_claim_evidence");
    }
}
