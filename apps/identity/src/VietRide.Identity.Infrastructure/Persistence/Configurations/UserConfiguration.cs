using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(u => u.Email)
            .HasColumnName("email")
            .HasMaxLength(255)
            .IsRequired();

        // PhoneNumber value object — nullable struct; EF maps via ValueConverter<PhoneNumber?, string?>.
        // EF Core 8 requires explicit ValueConverter for nullable value types.
        var phoneConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<PhoneNumber?, string?>(
            pn => pn.HasValue ? pn.Value.Value : null,
            raw => raw != null ? PhoneNumber.Parse(raw) : (PhoneNumber?)null);

        builder.Property(u => u.Phone)
            .HasColumnName("phone")
            .HasMaxLength(20)
            .HasConversion(phoneConverter)
            .IsRequired(false);

        builder.Property(u => u.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(255)
            .IsRequired(false);

        builder.Property(u => u.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(u => u.AvatarUrl)
            .HasColumnName("avatar_url")
            .IsRequired(false);

        builder.Property(u => u.Role)
            .HasColumnName("role")
            .HasColumnType("user_role")
            .IsRequired();

        builder.Property(u => u.Status)
            .HasColumnName("status")
            .HasColumnType("user_status")
            .HasDefaultValue(UserStatus.PENDING_EMAIL_VERIFICATION)
            .IsRequired();

        builder.Property(u => u.LockedFromStatus)
            .HasColumnName("locked_from_status")
            .HasColumnType("user_status")
            .IsRequired(false);

        builder.Property(u => u.OperatorId)
            .HasColumnName("operator_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(u => u.FailedLoginAttempts)
            .HasColumnName("failed_login_attempts")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(u => u.LastFailedLoginAt)
            .HasColumnName("last_failed_login_at")
            .IsRequired(false);

        builder.Property(u => u.LastLoginAt)
            .HasColumnName("last_login_at")
            .IsRequired(false);

        builder.Property(u => u.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(u => u.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(u => u.DeletedAt)
            .HasColumnName("deleted_at")
            .IsRequired(false);

        builder.Ignore(u => u.RowVersion);

        // Intra-service FK to operators (same DB — operators.id is Day-3 stub).
        builder.HasOne<Operator>()
            .WithMany()
            .HasForeignKey(u => u.OperatorId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_users_operator_id");

        // Partial functional unique index on LOWER(email) — schema requires LOWER(email) expression.
        // EF Core 8 cannot model expression/functional indexes natively; it tracks the index
        // against the `email` column for model consistency, but the migration OVERRIDES the
        // auto-generated CreateIndex with raw DDL (see InitIdentityAuth.cs):
        //   CREATE UNIQUE INDEX uq_users_email ON vietride_identity.users (LOWER(email)) WHERE deleted_at IS NULL;
        // Schema ref: db-schema/identity-user/schema.sql lines 171-173.
        builder.HasIndex(u => u.Email)
            .HasDatabaseName("uq_users_email")
            .IsUnique()
            .HasFilter("deleted_at IS NULL");

        // Partial unique index: phone unique among non-deleted users with a phone.
        builder.HasIndex(u => u.Phone)
            .HasDatabaseName("uq_users_phone")
            .IsUnique()
            .HasFilter("deleted_at IS NULL AND phone IS NOT NULL");

        builder.HasIndex(u => u.OperatorId)
            .HasDatabaseName("idx_users_operator_id")
            .HasFilter("operator_id IS NOT NULL");

        builder.HasIndex(new[] { "Role", "Status" })
            .HasDatabaseName("idx_users_role_status");

        // Global query filter: exclude soft-deleted rows.
        builder.HasQueryFilter(u => u.DeletedAt == null);

        // DB-level CHECK constraints (documented — not enforced by EF itself, but
        // declared so migration emits them and the schema matches schema.sql).
        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "chk_users_phone_format",
                "phone IS NULL OR phone ~ '^\\+84[0-9]{9,10}$'");

            t.HasCheckConstraint(
                "chk_users_operator_role",
                "(role IN ('DRIVER', 'ASSISTANT', 'OPERATOR_STAFF', 'OPERATOR_ADMIN') AND operator_id IS NOT NULL) " +
                "OR (role IN ('PASSENGER', 'SYSTEM_ADMIN') AND operator_id IS NULL)");

            t.HasCheckConstraint(
                "chk_users_locked_from_status",
                "((status = 'LOCKED' AND locked_from_status IN ('ACTIVE', 'PENDING_EMAIL_VERIFICATION')) " +
                "OR (status <> 'LOCKED' AND locked_from_status IS NULL))");
        });
    }
}
