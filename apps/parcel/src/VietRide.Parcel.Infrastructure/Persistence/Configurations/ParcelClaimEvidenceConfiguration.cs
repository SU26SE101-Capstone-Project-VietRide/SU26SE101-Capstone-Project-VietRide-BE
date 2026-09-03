using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Parcel.Domain.Entities;

namespace VietRide.Parcel.Infrastructure.Persistence.Configurations;

internal sealed class ParcelClaimEvidenceConfiguration : IEntityTypeConfiguration<ParcelClaimEvidence>
{
    public void Configure(EntityTypeBuilder<ParcelClaimEvidence> builder)
    {
        builder.ToTable("parcel_claim_evidence");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.ClaimId).HasColumnName("claim_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.EvidenceType).HasColumnName("evidence_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Reference).HasColumnName("reference").HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Note).HasColumnName("note").HasColumnType("text");
        builder.Property(x => x.UploadedByUserId).HasColumnName("uploaded_by_user_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.Ignore(x => x.RowVersion);

        builder.HasAlternateKey(x => new { x.ClaimId, x.Id });
        builder.HasOne<ParcelClaim>().WithMany().HasForeignKey(x => x.ClaimId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.ClaimId, x.CreatedAt });
    }
}
