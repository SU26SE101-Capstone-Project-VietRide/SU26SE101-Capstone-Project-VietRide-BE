using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Payment.Domain.Entities;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Infrastructure.Persistence.Configurations;

internal sealed class PlatformWalletTransactionConfiguration : IEntityTypeConfiguration<PlatformWalletTransaction>
{
    public void Configure(EntityTypeBuilder<PlatformWalletTransaction> builder)
    {
        builder.ToTable("platform_wallet_transactions", table =>
        {
            table.HasCheckConstraint("chk_platform_wallet_transactions_amount_positive", "amount > 0");
            table.HasCheckConstraint(
                "chk_platform_wallet_transactions_balance_non_negative",
                "balance_before >= 0 AND balance_after >= 0");
            table.HasCheckConstraint(
                "chk_platform_wallet_transactions_actor_type",
                "actor_type IN ('USER','SYSTEM')");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.Type)
            .HasColumnName("type")
            .HasColumnType($"{PaymentDbContext.SchemaName}.platform_wallet_transaction_type")
            .IsRequired();

        builder.Property(x => x.Amount)
            .HasColumnName("amount")
            .HasColumnType("bigint")
            .HasConversion(m => m.Amount, amount => Money.FromRaw(amount))
            .IsRequired();

        builder.Property(x => x.BalanceBefore)
            .HasColumnName("balance_before")
            .HasColumnType("bigint")
            .HasConversion(m => m.Amount, amount => Money.FromRaw(amount))
            .IsRequired();

        builder.Property(x => x.BalanceAfter)
            .HasColumnName("balance_after")
            .HasColumnType("bigint")
            .HasConversion(m => m.Amount, amount => Money.FromRaw(amount))
            .IsRequired();

        builder.Property(x => x.ReferenceType)
            .HasColumnName("reference_type")
            .HasColumnType($"{PaymentDbContext.SchemaName}.platform_wallet_transaction_ref")
            .IsRequired();

        builder.Property(x => x.ReferenceId)
            .HasColumnName("reference_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(x => x.Note)
            .HasColumnName("note")
            .HasColumnType("text")
            .IsRequired(false);

        builder.Property(x => x.ActorType)
            .HasColumnName("actor_type")
            .HasConversion<string>()
            .HasMaxLength(16)
            .HasDefaultValueSql("'SYSTEM'")
            .IsRequired();

        builder.Property(x => x.ActorUserId)
            .HasColumnName("actor_user_id")
            .HasColumnType("uuid");

        builder.Property(x => x.ActorDisplayName)
            .HasColumnName("actor_display_name")
            .HasMaxLength(200);

        builder.Property(x => x.ActorEmail)
            .HasColumnName("actor_email")
            .HasMaxLength(320);

        builder.Property(x => x.ActorRole)
            .HasColumnName("actor_role")
            .HasMaxLength(50);

        builder.Property(x => x.ActorSnapshotResolved)
            .HasColumnName("actor_snapshot_resolved")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Ignore(x => x.UpdatedAt);
        builder.Ignore(x => x.RowVersion);

        builder.HasIndex(x => x.CreatedAt)
            .HasDatabaseName("idx_platform_wallet_transactions_created_at")
            .IsDescending();

        builder.HasIndex(x => new { x.ReferenceType, x.ReferenceId })
            .HasDatabaseName("idx_platform_wallet_transactions_reference")
            .HasFilter("reference_id IS NOT NULL");

        builder.HasIndex(x => new { x.Type, x.ReferenceType, x.ReferenceId })
            .HasDatabaseName("uq_platform_wallet_transactions_subscription")
            .HasFilter("reference_type = 'SUBSCRIPTION_PAYMENT'")
            .IsUnique();

        builder.HasIndex(x => x.ActorUserId)
            .HasDatabaseName("idx_platform_wallet_transactions_actor_user_id")
            .HasFilter("actor_user_id IS NOT NULL");
    }
}
