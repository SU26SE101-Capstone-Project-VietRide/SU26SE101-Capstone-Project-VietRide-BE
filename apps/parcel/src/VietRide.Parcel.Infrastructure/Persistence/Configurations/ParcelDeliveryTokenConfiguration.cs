using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Parcel.Domain.Entities;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.Infrastructure.Persistence.Configurations;

internal sealed class ParcelDeliveryTokenConfiguration
    : IEntityTypeConfiguration<ParcelDeliveryToken>
{
    public void Configure(EntityTypeBuilder<ParcelDeliveryToken> builder)
    {
        builder.ToTable("parcel_delivery_tokens", table =>
        {
            table.HasCheckConstraint(
                "chk_parcel_delivery_tokens_issue_reason",
                "issue_reason IN ('INITIAL_DELIVERY', 'RESEND', 'MIGRATION_BACKFILL')");
        });

        builder.HasKey(token => token.Id);

        builder.Property(token => token.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(token => token.ParcelId)
            .HasColumnName("parcel_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(token => token.TokenHash)
            .HasColumnName("token_hash")
            .HasColumnType("char(64)")
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();

        builder.Property(token => token.ExpiresAt)
            .HasColumnName("expires_at")
            .IsRequired();

        builder.Property(token => token.RevokedAt)
            .HasColumnName("revoked_at")
            .IsRequired(false);

        builder.Property(token => token.IssuedByUserId)
            .HasColumnName("issued_by_user_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(token => token.IssueReason)
            .HasColumnName("issue_reason")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(token => token.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(token => token.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Ignore(token => token.RowVersion);

        builder.HasOne<ParcelEntity>()
            .WithMany()
            .HasForeignKey(token => token.ParcelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(token => token.TokenHash)
            .HasDatabaseName("uq_parcel_delivery_tokens_token_hash")
            .IsUnique();

        builder.HasIndex(token => token.ParcelId)
            .HasDatabaseName("uq_parcel_delivery_tokens_active_parcel")
            .HasFilter("revoked_at IS NULL")
            .IsUnique();

        builder.HasIndex(token => token.ExpiresAt)
            .HasDatabaseName("idx_parcel_delivery_tokens_expires_at_active")
            .HasFilter("revoked_at IS NULL");
    }
}
