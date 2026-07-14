using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Payment.Domain.Entities;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Infrastructure.Persistence.Configurations;

internal sealed class OperatorWalletTransactionConfiguration : IEntityTypeConfiguration<OperatorWalletTransaction>
{
    public void Configure(EntityTypeBuilder<OperatorWalletTransaction> builder)
    {
        builder.ToTable("operator_wallet_transactions", table =>
        {
            table.HasCheckConstraint("chk_operator_wallet_transactions_amount_positive", "amount > 0");
            table.HasCheckConstraint("chk_operator_wallet_transactions_balance_non_negative", "balance_before >= 0 AND balance_after >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.Type).HasColumnName("type").HasColumnType($"{PaymentDbContext.SchemaName}.operator_wallet_transaction_type").IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount").HasColumnType("bigint").HasConversion(m => m.Amount, value => Money.FromRaw(value)).IsRequired();
        builder.Property(x => x.BalanceBefore).HasColumnName("balance_before").HasColumnType("bigint").HasConversion(m => m.Amount, value => Money.FromRaw(value)).IsRequired();
        builder.Property(x => x.BalanceAfter).HasColumnName("balance_after").HasColumnType("bigint").HasConversion(m => m.Amount, value => Money.FromRaw(value)).IsRequired();
        builder.Property(x => x.ReferenceType).HasColumnName("reference_type").HasColumnType($"{PaymentDbContext.SchemaName}.operator_wallet_transaction_ref").IsRequired();
        builder.Property(x => x.ReferenceId).HasColumnName("reference_id").HasColumnType("uuid");
        builder.Property(x => x.Note).HasColumnName("note").HasColumnType("text");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();
        builder.Ignore(x => x.UpdatedAt);
        builder.Ignore(x => x.RowVersion);
        builder.HasIndex(x => new { x.OperatorId, x.CreatedAt }).HasDatabaseName("idx_operator_wallet_transactions_operator_id_created_at").IsDescending(false, true);
        builder.HasIndex(x => new { x.ReferenceType, x.ReferenceId }).HasDatabaseName("idx_operator_wallet_transactions_reference").HasFilter("reference_id IS NOT NULL");
        builder.HasIndex(x => new { x.OperatorId, x.Type, x.ReferenceType, x.ReferenceId })
            .HasDatabaseName("uq_operator_wallet_transactions_subscription")
            .HasFilter("reference_type = 'SUBSCRIPTION_PAYMENT'").IsUnique();
    }
}
