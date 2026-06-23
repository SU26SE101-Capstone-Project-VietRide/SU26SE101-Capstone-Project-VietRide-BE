using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;

namespace VietRide.Booking.Infrastructure.Persistence.Configurations;

internal sealed class OperatorVoucherConsentConfiguration : IEntityTypeConfiguration<OperatorVoucherConsent>
{
    public void Configure(EntityTypeBuilder<OperatorVoucherConsent> builder)
    {
        builder.ToTable("operator_voucher_consents");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.OperatorId)
            .HasColumnName("operator_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.VoucherId)
            .HasColumnName("voucher_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasColumnType("operator_voucher_consent_status")
            .HasDefaultValue(OperatorVoucherConsentStatus.PENDING)
            .IsRequired();

        builder.Property(x => x.RequestedAt)
            .HasColumnName("requested_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(x => x.RespondedAt)
            .HasColumnName("responded_at")
            .IsRequired(false);

        builder.Property(x => x.RespondedByUserId)
            .HasColumnName("responded_by_user_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(x => x.RejectReason)
            .HasColumnName("reject_reason")
            .HasColumnType("text")
            .IsRequired(false);

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Ignore(x => x.RowVersion);

        // Intra-service FK to vouchers (ON DELETE CASCADE) — no inverse collection on Voucher.
        builder.HasOne(x => x.Voucher)
            .WithMany()
            .HasForeignKey(x => x.VoucherId)
            .OnDelete(DeleteBehavior.Cascade);

        // One consent row per (operator, voucher).
        builder.HasIndex(x => new { x.OperatorId, x.VoucherId })
            .HasDatabaseName("uq_operator_voucher_consents_operator_voucher")
            .IsUnique();

        builder.HasIndex(x => x.Status)
            .HasDatabaseName("idx_operator_voucher_consents_status");

        builder.HasIndex(x => new { x.OperatorId, x.Status })
            .HasDatabaseName("idx_operator_voucher_consents_operator_status");

        builder.HasIndex(x => x.VoucherId)
            .HasDatabaseName("idx_operator_voucher_consents_voucher_id");
    }
}
