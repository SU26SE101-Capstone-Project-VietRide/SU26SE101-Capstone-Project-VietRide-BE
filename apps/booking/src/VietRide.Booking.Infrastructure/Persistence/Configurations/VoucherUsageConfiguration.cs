using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.Infrastructure.Persistence.Configurations;

internal sealed class VoucherUsageConfiguration : IEntityTypeConfiguration<VoucherUsage>
{
    public void Configure(EntityTypeBuilder<VoucherUsage> builder)
    {
        builder.ToTable("voucher_usages");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.VoucherId)
            .HasColumnName("voucher_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.UserId)
            .HasColumnName("user_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.BookingId)
            .HasColumnName("booking_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(x => x.ReferenceType)
            .HasColumnName("reference_type")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.ReferenceId)
            .HasColumnName("reference_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.BookingGroupId)
            .HasColumnName("booking_group_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(x => x.DiscountAmount)
            .HasColumnName("discount_amount")
            .HasColumnType("bigint")
            .HasConversion(m => m.Amount, amount => Money.FromRaw(amount))
            .IsRequired();

        builder.Property(x => x.FundedBy)
            .HasColumnName("funded_by")
            .HasColumnType("voucher_funding_type")
            .IsRequired();

        // voucher_usages has no updated_at column (append/delete only) — ignore the inherited
        // audit property + optimistic-concurrency row version.
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Ignore(x => x.UpdatedAt);
        builder.Ignore(x => x.RowVersion);

        // Intra-service FKs (allowed — same DB). No inverse collections on Voucher/Booking to
        // keep the aggregates decoupled; the FK constraint is still emitted.
        builder.HasOne(x => x.Voucher)
            .WithMany()
            .HasForeignKey(x => x.VoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Booking)
            .WithMany()
            .HasForeignKey(x => x.BookingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.VoucherId, x.UserId })
            .HasDatabaseName("idx_voucher_usages_voucher_user");

        builder.HasIndex(x => new { x.VoucherId, x.BookingGroupId })
            .HasDatabaseName("idx_voucher_usages_voucher_group")
            .HasFilter("booking_group_id IS NOT NULL");

        builder.HasIndex(x => x.BookingId)
            .HasDatabaseName("idx_voucher_usages_booking_id")
            .HasFilter("booking_id IS NOT NULL");

        builder.HasIndex(x => new { x.ReferenceType, x.ReferenceId })
            .HasDatabaseName("idx_voucher_usages_reference");
    }
}
