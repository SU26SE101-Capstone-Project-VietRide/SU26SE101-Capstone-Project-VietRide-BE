using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Infrastructure.Persistence.Configurations;

internal sealed class OAuthIdentityConfiguration : IEntityTypeConfiguration<OAuthIdentity>
{
    public void Configure(EntityTypeBuilder<OAuthIdentity> builder)
    {
        builder.ToTable("oauth_identities");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(o => o.UserId)
            .HasColumnName("user_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(o => o.Provider)
            .HasColumnName("provider")
            .HasColumnType("oauth_provider")
            .HasConversion(
                p => p.ToString(),
                s => Enum.Parse<OAuthProvider>(s))
            .IsRequired();

        builder.Property(o => o.ProviderSubject)
            .HasColumnName("provider_subject")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(o => o.ProviderEmail)
            .HasColumnName("provider_email")
            .HasMaxLength(255)
            .IsRequired(false);

        builder.Property(o => o.LinkedAt)
            .HasColumnName("linked_at")
            .IsRequired();

        builder.Property(o => o.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(o => o.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.Ignore(o => o.RowVersion);

        // FK to users (CASCADE on delete — when user deleted, OAuth identities go too).
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(o => o.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_oauth_identities_user_id");

        // Unique composite: (provider, provider_subject) — one Google account per identity.
        builder.HasIndex(o => new { o.Provider, o.ProviderSubject })
            .HasDatabaseName("uq_oauth_identities_provider_subject")
            .IsUnique();

        // Unique composite: (user_id, provider) — one provider entry per user.
        builder.HasIndex(o => new { o.UserId, o.Provider })
            .HasDatabaseName("uq_oauth_identities_user_provider")
            .IsUnique();

        builder.HasIndex(o => o.UserId)
            .HasDatabaseName("idx_oauth_identities_user_id");
    }
}
