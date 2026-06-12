using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Kernel.ValueObjects;
using PaymentEntity = VietRide.Payment.Domain.Entities.Payment;

namespace VietRide.Payment.Infrastructure.Persistence.Configurations;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<PaymentEntity>
{
    public void Configure(EntityTypeBuilder<PaymentEntity> builder)
    {
        builder.ToTable("payments", table =>
        {
            table.HasCheckConstraint("chk_payments_amount_non_negative", "amount >= 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.ReferenceType).HasColumnName("reference_type").HasColumnType("payment_reference_type").IsRequired();
        builder.Property(x => x.ReferenceId).HasColumnName("reference_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.UserId).HasColumnName("user_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.OperatorId).HasColumnName("operator_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.Amount).HasColumnName("amount").HasColumnType("bigint").HasConversion(m => m.Amount, amount => Money.FromRaw(amount)).IsRequired();
        builder.Property(x => x.Method).HasColumnName("method").HasColumnType("payment_method").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasColumnType("payment_status").IsRequired();
        builder.Property(x => x.VnPayTxnRef).HasColumnName("vnpay_txn_ref").HasMaxLength(100).IsRequired(false);
        builder.Property(x => x.VnPayResponseCode).HasColumnName("vnpay_response_code").HasMaxLength(10).IsRequired(false);
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100).IsRequired(false);
        builder.Property(x => x.PaymentRedirectUrl).HasColumnName("payment_redirect_url").IsRequired(false);
        builder.Property(x => x.SucceededAt).HasColumnName("succeeded_at").IsRequired(false);
        builder.Property(x => x.FailedAt).HasColumnName("failed_at").IsRequired(false);
        builder.Property(x => x.ExpiredAt).HasColumnName("expired_at").IsRequired(false);
        builder.Property(x => x.RefundedAt).HasColumnName("refunded_at").IsRequired(false);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()").IsRequired();

        builder.HasIndex(x => x.VnPayTxnRef).IsUnique().HasFilter("vnpay_txn_ref IS NOT NULL");
        builder.HasIndex(x => x.IdempotencyKey).IsUnique().HasFilter("idempotency_key IS NOT NULL");
        builder.HasIndex(x => new { x.ReferenceType, x.ReferenceId }).HasDatabaseName("idx_payments_reference");
    }
}
