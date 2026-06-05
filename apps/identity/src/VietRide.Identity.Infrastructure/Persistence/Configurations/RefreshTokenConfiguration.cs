using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Infrastructure.Persistence.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(r => r.UserId)
            .HasColumnName("user_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(r => r.TokenHash)
            .HasColumnName("token_hash")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(r => r.FamilyId)
            .HasColumnName("family_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(r => r.ParentTokenId)
            .HasColumnName("parent_token_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(r => r.IssuedAt)
            .HasColumnName("issued_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(r => r.ExpiresAt)
            .HasColumnName("expires_at")
            .IsRequired();

        builder.Property(r => r.RevokedAt)
            .HasColumnName("revoked_at")
            .IsRequired(false);

        builder.Property(r => r.RevokedReason)
            .HasColumnName("revoked_reason")
            .HasColumnType("refresh_token_revoke_reason")
            .IsRequired(false);

        builder.Property(r => r.UserAgent)
            .HasColumnName("user_agent")
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(r => r.IpAddress)
            .HasColumnName("ip_address")
            .HasMaxLength(45)
            .IsRequired(false);

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(r => r.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Ignore(r => r.RowVersion);

        // FK to users (CASCADE — when user deleted, tokens deleted too).
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_refresh_tokens_user_id");

        // Self-FK: parent_token_id → refresh_tokens.id (SET NULL on delete).
        builder.HasOne<RefreshToken>()
            .WithMany()
            .HasForeignKey(r => r.ParentTokenId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("fk_refresh_tokens_parent_token_id");

        // Unique on token_hash (deterministic SHA-256 hex lookup).
        builder.HasIndex(r => r.TokenHash)
            .HasDatabaseName("uq_refresh_tokens_token_hash")
            .IsUnique();

        builder.HasIndex(r => r.UserId)
            .HasDatabaseName("idx_refresh_tokens_user_id");

        builder.HasIndex(r => r.FamilyId)
            .HasDatabaseName("idx_refresh_tokens_family_id");

        builder.HasIndex(r => r.ExpiresAt)
            .HasDatabaseName("idx_refresh_tokens_expires_at")
            .HasFilter("revoked_at IS NULL");

        builder.HasIndex(r => r.ParentTokenId)
            .HasDatabaseName("idx_refresh_tokens_parent_token_id")
            .HasFilter("parent_token_id IS NOT NULL");
    }
}
