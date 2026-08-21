using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.Infrastructure.Persistence.Configurations;

internal sealed class SubscriptionUpgradeAttemptConfiguration : IEntityTypeConfiguration<SubscriptionUpgradeAttempt>
{
    public void Configure(EntityTypeBuilder<SubscriptionUpgradeAttempt> builder)
    {
        builder.ToTable("subscription_upgrade_attempts");
        builder.HasKey(attempt => attempt.Id);
        builder.Property(attempt => attempt.Id).HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(attempt => attempt.SubscriptionId).HasColumnName("subscription_id").HasColumnType("uuid").IsRequired();
        builder.Property(attempt => attempt.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired();
        builder.Property(attempt => attempt.TargetPlanId).HasColumnName("target_plan_id").HasColumnType("uuid").IsRequired();
        builder.Property(attempt => attempt.SourcePlanId).HasColumnName("source_plan_id").HasColumnType("uuid").IsRequired();
        builder.Property(attempt => attempt.BillingPeriod).HasColumnName("billing_period").HasColumnType("subscription_billing_period").IsRequired();
        builder.Property(attempt => attempt.Amount).HasColumnName("amount").HasColumnType("bigint").HasConversion(money => money.Amount, amount => Money.FromRaw(amount)).IsRequired();
        builder.Property(attempt => attempt.PaymentMethod).HasColumnName("payment_method").HasColumnType("subscription_payment_method").IsRequired();
        builder.Property(attempt => attempt.Status).HasColumnName("status").HasColumnType("subscription_upgrade_attempt_status").IsRequired();
        builder.Property(attempt => attempt.PaymentId).HasColumnName("latest_payment_id").HasColumnType("uuid");
        builder.Property(attempt => attempt.LatestPaymentStatus).HasColumnName("latest_payment_status").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(attempt => attempt.PaymentSessionVersion).HasColumnName("payment_session_version").HasDefaultValue(0).IsRequired();
        builder.Property(attempt => attempt.FallbackPolicy).HasColumnName("fallback_policy").HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(attempt => attempt.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100).IsRequired();
        builder.Property(attempt => attempt.DueAt).HasColumnName("due_at").IsRequired();
        builder.Property(attempt => attempt.QuotedAt).HasColumnName("quoted_at").IsRequired();
        builder.Property(attempt => attempt.PeriodFrom).HasColumnName("period_from").IsRequired();
        builder.Property(attempt => attempt.PeriodTo).HasColumnName("period_to").IsRequired();
        builder.Property(attempt => attempt.CurrentCyclePrice).HasColumnName("current_cycle_price_amount").HasColumnType("bigint").HasConversion(money => money.Amount, amount => Money.FromRaw(amount)).IsRequired();
        builder.Property(attempt => attempt.TargetCyclePrice).HasColumnName("target_cycle_price_amount").HasColumnType("bigint").HasConversion(money => money.Amount, amount => Money.FromRaw(amount)).IsRequired();
        builder.Property(attempt => attempt.UnusedCredit).HasColumnName("unused_credit_amount").HasColumnType("bigint").HasConversion(money => money.Amount, amount => Money.FromRaw(amount)).IsRequired();
        builder.Property(attempt => attempt.ProratedTargetAmount).HasColumnName("prorated_target_amount").HasColumnType("bigint").HasConversion(money => money.Amount, amount => Money.FromRaw(amount)).IsRequired();
        builder.Property(attempt => attempt.IsProrated).HasColumnName("is_prorated").IsRequired();
        builder.Property(attempt => attempt.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();
        builder.Property(attempt => attempt.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()").IsRequired();
        builder.Ignore(attempt => attempt.RowVersion);
        builder.HasIndex(attempt => attempt.IdempotencyKey).HasDatabaseName("uq_subscription_upgrade_attempts_idempotency_key").IsUnique();
        builder.HasIndex(attempt => new { attempt.Status, attempt.DueAt }).HasDatabaseName("idx_subscription_upgrade_attempts_status_due_at");
        builder.HasIndex(attempt => attempt.PaymentId).HasDatabaseName("idx_subscription_upgrade_attempts_latest_payment_id").HasFilter("latest_payment_id IS NOT NULL");
        builder.HasIndex(attempt => attempt.SubscriptionId)
            .HasDatabaseName("uq_subscription_upgrade_attempts_active_subscription")
            .IsUnique()
            .HasFilter("status IN ('INITIATED', 'PAYMENT_PENDING')");
        builder.HasOne<OperatorSubscription>().WithMany().HasForeignKey(attempt => attempt.SubscriptionId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_subscription_upgrade_attempts_subscription_id");
        builder.HasOne<Operator>().WithMany().HasForeignKey(attempt => attempt.OperatorId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_subscription_upgrade_attempts_operator_id");
        builder.HasOne<SubscriptionPlan>().WithMany().HasForeignKey(attempt => attempt.TargetPlanId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_subscription_upgrade_attempts_target_plan_id");
        builder.HasOne<SubscriptionPlan>().WithMany().HasForeignKey(attempt => attempt.SourcePlanId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_subscription_upgrade_attempts_source_plan_id");
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("chk_subscription_upgrade_attempts_quote_amounts", "amount > 0 AND current_cycle_price_amount >= 0 AND target_cycle_price_amount >= 0 AND unused_credit_amount >= 0 AND prorated_target_amount >= 0 AND prorated_target_amount = unused_credit_amount + amount");
            table.HasCheckConstraint("chk_subscription_upgrade_attempts_quote_period", "quoted_at < due_at AND period_from < period_to");
        });
    }
}
