using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Payment.Domain.Entities;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Infrastructure.Persistence.Configurations;

internal sealed class OperatorWalletConfiguration : IEntityTypeConfiguration<OperatorWallet>
{
    public void Configure(EntityTypeBuilder<OperatorWallet> builder)
    {
        builder.ToTable("operator_wallets", table =>
            table.HasCheckConstraint("chk_operator_wallets_balance_non_negative", "balance >= 0"));
        builder.HasKey(x => x.OperatorId);
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").HasColumnType("uuid");
        builder.Property(x => x.Balance).HasColumnName("balance").HasColumnType("bigint")
            .HasConversion(m => m.Amount, amount => Money.FromRaw(amount)).HasDefaultValueSql("0").IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).HasDefaultValue("VND").IsRequired();
        builder.Property(x => x.RowVersion).HasColumnName("row_version").IsConcurrencyToken().HasDefaultValue(0).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()").IsRequired();
    }
}
