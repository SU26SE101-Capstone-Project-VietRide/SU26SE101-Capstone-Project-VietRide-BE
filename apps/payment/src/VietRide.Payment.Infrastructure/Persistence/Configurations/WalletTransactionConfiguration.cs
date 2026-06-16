using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Payment.Domain.Entities;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Infrastructure.Persistence.Configurations;

internal sealed class WalletTransactionConfiguration : IEntityTypeConfiguration<WalletTransaction>
{
    public void Configure(EntityTypeBuilder<WalletTransaction> builder)
    {
        builder.ToTable("wallet_transactions", table =>
        {
            table.HasCheckConstraint("chk_wallet_transactions_amount_positive", "amount > 0");
            table.HasCheckConstraint(
                "chk_wallet_transactions_balance_non_negative",
                "balance_before >= 0 AND balance_after >= 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.UserId)
            .HasColumnName("user_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.Type)
            .HasColumnName("type")
            .HasColumnType("wallet_transaction_type")
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
            .HasColumnType("wallet_transaction_ref")
            .IsRequired();

        builder.Property(x => x.ReferenceId)
            .HasColumnName("reference_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(x => x.Note)
            .HasColumnName("note")
            .HasColumnType("text")
            .IsRequired(false);

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Ignore(x => x.UpdatedAt);
        builder.Ignore(x => x.RowVersion);

        builder.HasIndex(x => new { x.UserId, x.CreatedAt })
            .HasDatabaseName("idx_wallet_transactions_user_id_created_at")
            .IsDescending(false, true);

        builder.HasIndex(x => new { x.ReferenceType, x.ReferenceId })
            .HasDatabaseName("idx_wallet_transactions_reference")
            .HasFilter("reference_id IS NOT NULL");
    }
}
