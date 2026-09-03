using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Parcel.Domain.Entities;

namespace VietRide.Parcel.Infrastructure.Persistence.Configurations;

internal sealed class ParcelClaimDecisionEvidenceConfiguration
    : IEntityTypeConfiguration<ParcelClaimDecisionEvidence>
{
    public void Configure(EntityTypeBuilder<ParcelClaimDecisionEvidence> builder)
    {
        builder.ToTable("parcel_claim_decision_evidence");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.ClaimId).HasColumnName("claim_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.EvidenceId).HasColumnName("evidence_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.AcceptedByUserId).HasColumnName("accepted_by_user_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.AcceptedAt).HasColumnName("accepted_at").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.Ignore(x => x.RowVersion);

        builder.HasOne<ParcelClaim>()
            .WithMany()
            .HasForeignKey(x => x.ClaimId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_parcel_claim_decision_evidence_claim");
        builder.HasOne<ParcelClaimEvidence>()
            .WithMany()
            .HasForeignKey(x => new { x.ClaimId, x.EvidenceId })
            .HasPrincipalKey(x => new { x.ClaimId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_parcel_claim_decision_evidence_claim_evidence");
        builder.HasIndex(x => new { x.ClaimId, x.EvidenceId })
            .IsUnique()
            .HasDatabaseName("uq_parcel_claim_decision_evidence");
    }
}
