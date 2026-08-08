using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Payment.Domain.Entities;

namespace VietRide.Payment.Infrastructure.Persistence.Configurations;

internal sealed class OperatorTripSettlementConfiguration : IEntityTypeConfiguration<OperatorTripSettlement>
{
    public void Configure(EntityTypeBuilder<OperatorTripSettlement> builder)
    {
        builder.ToTable("operator_trip_settlements", table =>
        {
            table.HasCheckConstraint("chk_operator_trip_settlements_eligible_after_terminal", "eligible_at >= trip_terminal_at");
            table.HasCheckConstraint(
                "chk_operator_trip_settlements_settled_consistency",
                "(status IN ('PENDING_HOLD','ELIGIBLE') AND settled_at IS NULL AND settlement_method IS NULL AND wallet_transaction_id IS NULL) OR " +
                "(status IN ('SETTLED','CANCELLED') AND settled_at IS NOT NULL AND settlement_method IS NOT NULL)");
            table.HasCheckConstraint(
                "chk_operator_trip_settlements_failure_consistency",
                "(active_failure_code IS NULL) OR (status = 'ELIGIBLE' AND settlement_failure_count > 0 AND last_settlement_failure_at IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.TripId).HasColumnName("trip_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.NetAmount).HasColumnName("net_amount").HasColumnType("bigint").HasDefaultValue(0L).IsRequired();
        builder.Property(x => x.TripTerminalAt).HasColumnName("trip_terminal_at").IsRequired();
        builder.Property(x => x.EligibleAt).HasColumnName("eligible_at").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasColumnType($"{PaymentDbContext.SchemaName}.operator_trip_settlement_status").HasDefaultValueSql("'PENDING_HOLD'").IsRequired();
        builder.Property(x => x.SettlementMethod).HasColumnName("settlement_method").HasColumnType($"{PaymentDbContext.SchemaName}.operator_trip_settlement_method");
        builder.Property(x => x.SettledAt).HasColumnName("settled_at");
        builder.Property(x => x.SettledByUserId).HasColumnName("settled_by_user_id").HasColumnType("uuid");
        builder.Property(x => x.OperatorSnapshotResolved).HasColumnName("operator_snapshot_resolved").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.OperatorName).HasColumnName("operator_name").HasMaxLength(200);
        builder.Property(x => x.OperatorLogoUrl).HasColumnName("operator_logo_url").HasMaxLength(2048);
        builder.Property(x => x.OperatorContactPhone).HasColumnName("operator_contact_phone").HasMaxLength(32);
        builder.Property(x => x.SettledBySnapshotResolved).HasColumnName("settled_by_snapshot_resolved").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.SettledByDisplayName).HasColumnName("settled_by_display_name").HasMaxLength(200);
        builder.Property(x => x.SettledByEmail).HasColumnName("settled_by_email").HasMaxLength(320);
        builder.Property(x => x.SettledByRole).HasColumnName("settled_by_role").HasMaxLength(50);
        builder.Property(x => x.WalletTransactionId).HasColumnName("wallet_transaction_id").HasColumnType("uuid");
        builder.Property(x => x.SettlementFailureCount).HasColumnName("settlement_failure_count").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.LastSettlementFailureAt).HasColumnName("last_settlement_failure_at");
        builder.Property(x => x.ActiveFailureCode).HasColumnName("active_failure_code").HasMaxLength(100);
        builder.Property(x => x.FailureResolvedAt).HasColumnName("failure_resolved_at");
        builder.Property(x => x.RowVersion).HasColumnName("row_version").IsConcurrencyToken().HasDefaultValue(0).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()").IsRequired();
        builder.HasIndex(x => new { x.OperatorId, x.TripId }).HasDatabaseName("uq_operator_trip_settlements_operator_trip").IsUnique();
        builder.HasIndex(x => new { x.Status, x.EligibleAt }).HasDatabaseName("idx_operator_trip_settlements_status_eligible").HasFilter("status IN ('PENDING_HOLD','ELIGIBLE')");
        builder.HasIndex(x => new { x.OperatorId, x.Status }).HasDatabaseName("idx_operator_trip_settlements_operator_status");
        builder.HasIndex(x => x.TripId).HasDatabaseName("idx_operator_trip_settlements_trip_id");
        builder.HasIndex(x => x.WalletTransactionId).HasDatabaseName("idx_operator_trip_settlements_wallet_transaction_id").HasFilter("wallet_transaction_id IS NOT NULL");
        builder.HasIndex(x => x.SettledByUserId).HasDatabaseName("idx_operator_trip_settlements_settled_by_user_id").HasFilter("settled_by_user_id IS NOT NULL");
        builder.HasIndex(x => new { x.Status, x.ActiveFailureCode, x.LastSettlementFailureAt }).HasDatabaseName("idx_operator_trip_settlements_stuck").HasFilter("status = 'ELIGIBLE' AND active_failure_code IS NOT NULL");
        builder.HasIndex(x => x.SettledAt).HasDatabaseName("idx_operator_trip_settlements_settled_at").HasFilter("status = 'SETTLED'");
        builder.HasOne<OperatorWalletTransaction>()
            .WithMany()
            .HasForeignKey(x => x.WalletTransactionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
