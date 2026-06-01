using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Infrastructure.Persistence.Configurations;

internal sealed class EmailVerificationTokenConfiguration : IEntityTypeConfiguration<EmailVerificationToken>
{
    public void Configure(EntityTypeBuilder<EmailVerificationToken> builder)
    {
        builder.ToTable("email_verification_tokens");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(e => e.UserId)
            .HasColumnName("user_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(e => e.Purpose)
            .HasColumnName("purpose")
            .HasColumnType("email_verification_purpose")
            .HasConversion(
                p => p.ToString(),
                s => Enum.Parse<EmailVerificationPurpose>(s))
            .IsRequired();

        builder.Property(e => e.Code)
            .HasColumnName("code")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(e => e.ExpiresAt)
            .HasColumnName("expires_at")
            .IsRequired();

        builder.Property(e => e.FailedAttempts)
            .HasColumnName("failed_attempts")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(e => e.UsedAt)
            .HasColumnName("used_at")
            .IsRequired(false);

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        // email_verification_tokens has no updated_at column in schema.sql.
        builder.Ignore(e => e.UpdatedAt);
        builder.Ignore(e => e.RowVersion);

        // FK to users (CASCADE).
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_email_verification_tokens_user_id");

        // Unique: (code, purpose) — full table-wide unique (schema line 252-253).
        // Lookup is scoped to userId+code+purpose in FindActiveAsync to prevent cross-user match.
        builder.HasIndex(e => new { e.Code, e.Purpose })
            .HasDatabaseName("uq_email_verification_tokens_code_purpose")
            .IsUnique();

        builder.HasIndex(e => new { e.UserId, e.Purpose })
            .HasDatabaseName("idx_email_verification_tokens_user_purpose")
            .HasFilter("used_at IS NULL");

        builder.HasIndex(e => e.ExpiresAt)
            .HasDatabaseName("idx_email_verification_tokens_expires_at")
            .HasFilter("used_at IS NULL");
    }
}
