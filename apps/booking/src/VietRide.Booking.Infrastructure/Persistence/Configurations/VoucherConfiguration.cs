using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.Infrastructure.Persistence.Configurations;

internal sealed class VoucherConfiguration : IEntityTypeConfiguration<Voucher>
{
    public void Configure(EntityTypeBuilder<Voucher> builder)
    {
        builder.ToTable("vouchers", table =>
        {
            table.HasCheckConstraint(
                "chk_vouchers_value_positive",
                "value > 0");

            table.HasCheckConstraint(
                "chk_vouchers_validity_window",
                "valid_until > valid_from");

            table.HasCheckConstraint(
                "chk_vouchers_min_order_non_negative",
                "min_order_amount >= 0");

            // Explicit ::voucher_funding_type cast is required for Postgres enum comparison.
            table.HasCheckConstraint(
                "chk_vouchers_operator_owned_funding",
                "owner_operator_id IS NULL OR funding_type = 'OPERATOR_FUNDED'::voucher_funding_type");

            table.HasCheckConstraint(
                "chk_vouchers_applicable_services_valid",
                "applicable_services <@ ARRAY['BOOKING', 'PARCEL']::text[] AND cardinality(applicable_services) > 0");

            table.HasCheckConstraint(
                "chk_vouchers_applicable_payment_methods_valid",
                "applicable_payment_methods IS NULL OR applicable_payment_methods <@ ARRAY['WALLET', 'VNPAY']::text[]");
        });

        // BSOT sec 9.6 — soft-delete query filter excludes soft-deleted vouchers from normal queries.
        builder.HasQueryFilter(v => v.DeletedAt == null);

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.Code)
            .HasColumnName("code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasColumnName("name")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(x => x.Type)
            .HasColumnName("type")
            .HasColumnType("voucher_type")
            .IsRequired();

        // value is dual-purpose (percent for PERCENT_OFF, VND for FIXED_AMOUNT) — raw bigint, NOT Money.
        builder.Property(x => x.Value)
            .HasColumnName("value")
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(x => x.MinOrderAmount)
            .HasColumnName("min_order_amount")
            .HasColumnType("bigint")
            .HasConversion(m => m.Amount, amount => Money.FromRaw(amount))
            .HasDefaultValueSql("0")
            .IsRequired();

        builder.Property(x => x.MaxDiscountAmount)
            .HasColumnName("max_discount_amount")
            .HasColumnType("bigint")
            .HasConversion(
                m => m != null ? (long?)m.Value.Amount : null,
                v => v.HasValue ? Money.FromRaw(v.Value) : (Money?)null)
            .IsRequired(false);

        builder.Property(x => x.TotalUsageLimit)
            .HasColumnName("total_usage_limit")
            .IsRequired(false);

        builder.Property(x => x.PerUserLimit)
            .HasColumnName("per_user_limit")
            .IsRequired(false);

        builder.Property(x => x.ValidFrom)
            .HasColumnName("valid_from")
            .IsRequired();

        builder.Property(x => x.ValidUntil)
            .HasColumnName("valid_until")
            .IsRequired();

        builder.Property(x => x.NewUserOnly)
            .HasColumnName("new_user_only")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(x => x.ApplicablePaymentMethods)
            .HasColumnName("applicable_payment_methods")
            .HasColumnType("text[]")
            .IsRequired(false);

        builder.Property(x => x.ApplicableServices)
            .HasColumnName("applicable_services")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(x => x.ApplicableOperatorIds)
            .HasColumnName("applicable_operator_ids")
            .HasColumnType("uuid[]")
            .IsRequired(false);

        builder.Property(x => x.ApplicableRouteIds)
            .HasColumnName("applicable_route_ids")
            .HasColumnType("uuid[]")
            .IsRequired(false);

        builder.Property(x => x.FundingType)
            .HasColumnName("funding_type")
            .HasColumnType("voucher_funding_type")
            .IsRequired();

        builder.Property(x => x.OwnerOperatorId)
            .HasColumnName("owner_operator_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(x => x.CreatedByUserId)
            .HasColumnName("created_by_user_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(x => x.DeletedAt)
            .HasColumnName("deleted_at")
            .IsRequired(false);

        builder.Ignore(x => x.RowVersion);

        // Partial unique: code is unique among non-soft-deleted vouchers (ADR 0003).
        builder.HasIndex(x => x.Code)
            .HasDatabaseName("uq_vouchers_code")
            .IsUnique()
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(x => x.ValidUntil)
            .HasDatabaseName("idx_vouchers_active_validity")
            .HasFilter("is_active = TRUE");

        builder.HasIndex(x => x.OwnerOperatorId)
            .HasDatabaseName("idx_vouchers_owner_operator")
            .HasFilter("owner_operator_id IS NOT NULL AND deleted_at IS NULL");

        builder.HasIndex(x => x.NewUserOnly)
            .HasDatabaseName("idx_vouchers_new_user_only");
    }
}
