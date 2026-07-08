using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Infrastructure.Persistence.Configurations;

internal sealed class TopUpRequestConfiguration : IEntityTypeConfiguration<TopUpRequest>
{
    public void Configure(EntityTypeBuilder<TopUpRequest> builder)
    {
        builder.ToTable("top_up_requests", table =>
        {
            table.HasCheckConstraint("chk_top_up_requests_amount_min", "amount >= 10000");
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

        builder.Property(x => x.Amount)
            .HasColumnName("amount")
            .HasColumnType("bigint")
            .HasConversion(m => m.Amount, amount => Money.FromRaw(amount))
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasColumnType($"{PaymentDbContext.SchemaName}.top_up_request_status")
            .HasDefaultValueSql($"'PENDING'::{PaymentDbContext.SchemaName}.top_up_request_status")
            .IsRequired();

        builder.Property(x => x.VnPayTxnRef)
            .HasColumnName("vnpay_txn_ref")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.VnPayResponseCode)
            .HasColumnName("vnpay_response_code")
            .HasMaxLength(10)
            .IsRequired(false);

        builder.Property(x => x.PaymentRedirectUrl)
            .HasColumnName("payment_redirect_url")
            .HasColumnType("text")
            .IsRequired(false);

        builder.Property(x => x.SucceededAt).HasColumnName("succeeded_at").IsRequired(false);
        builder.Property(x => x.ExpiredAt).HasColumnName("expired_at").IsRequired(false);

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Ignore(x => x.RowVersion);

        builder.HasIndex(x => x.VnPayTxnRef)
            .HasDatabaseName("uq_top_up_requests_vnpay_txn_ref")
            .IsUnique();

        builder.HasIndex(x => new { x.UserId, x.CreatedAt })
            .HasDatabaseName("idx_top_up_requests_user_id_created_at")
            .IsDescending(false, true);

        builder.HasIndex(x => new { x.Status, x.CreatedAt })
            .HasDatabaseName("idx_top_up_requests_status_created_at")
            .HasFilter($"status = 'PENDING'::{PaymentDbContext.SchemaName}.top_up_request_status");
    }
}
