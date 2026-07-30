using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.Infrastructure.Persistence.Configurations;

internal sealed class ParcelConfiguration : IEntityTypeConfiguration<ParcelEntity>
{
    private const string ParcelSizeCategoryType = $"{ParcelDbContext.SchemaName}.parcel_size_category";
    private const string ParcelDeliveryMethodType = $"{ParcelDbContext.SchemaName}.parcel_delivery_method";
    private const string ParcelStatusType = $"{ParcelDbContext.SchemaName}.parcel_status";
    private const string ParcelReviewDecisionType = $"{ParcelDbContext.SchemaName}.parcel_review_decision";

    public void Configure(EntityTypeBuilder<ParcelEntity> builder)
    {
        builder.ToTable("parcels", table =>
        {
            table.HasCheckConstraint("chk_parcels_amounts_non_negative", "deposit_amount >= 0 AND additional_amount >= 0");
            table.HasCheckConstraint("chk_parcels_settlement_amounts_non_negative", "estimated_gross_price_vnd >= 0 AND final_gross_price_vnd >= 0 AND discount_amount_vnd >= 0 AND estimated_total_price_vnd >= 0 AND final_total_price_vnd >= 0 AND deposit_required_vnd >= 0 AND deposit_paid_vnd >= 0 AND balance_required_vnd >= 0 AND balance_paid_vnd >= 0 AND refund_due_vnd >= 0 AND refunded_amount_vnd >= 0 AND forfeited_deposit_vnd >= 0");
            table.HasCheckConstraint("chk_parcels_settlement_policy_version_positive", "settlement_policy_version > 0");
            table.HasCheckConstraint("chk_parcels_weight_positive", "estimated_weight_kg > 0");
            table.HasCheckConstraint("chk_parcels_dimensions_positive", "estimated_length_cm > 0 AND estimated_width_cm > 0 AND estimated_height_cm > 0");
            table.HasCheckConstraint("chk_parcels_volume_positive", "estimated_volume_m3 > 0");
            table.HasCheckConstraint("chk_parcels_actual_weight_positive", "actual_weight_kg IS NULL OR actual_weight_kg > 0");
            table.HasCheckConstraint("chk_parcels_actual_dimensions_positive", "(actual_length_cm IS NULL AND actual_width_cm IS NULL AND actual_height_cm IS NULL) OR (actual_length_cm > 0 AND actual_width_cm > 0 AND actual_height_cm > 0)");
            table.HasCheckConstraint(
                "chk_parcels_check_in_photo_urls_max_three",
                "check_in_photo_urls IS NULL OR (jsonb_typeof(check_in_photo_urls) = 'array' AND jsonb_array_length(check_in_photo_urls) <= 3)");
            table.HasCheckConstraint(
                "chk_parcels_delivery_photo_urls_max_three",
                "delivery_photo_urls IS NULL OR (jsonb_typeof(delivery_photo_urls) = 'array' AND jsonb_array_length(delivery_photo_urls) <= 3)");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.ParcelCode)
            .HasColumnName("parcel_code")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.SenderUserId)
            .HasColumnName("sender_user_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.RecipientUserId)
            .HasColumnName("recipient_user_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(x => x.RecipientName)
            .HasColumnName("recipient_name")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.RecipientPhone)
            .HasColumnName("recipient_phone")
            .HasMaxLength(20)
            .HasConversion(p => p.Value, s => PhoneNumber.Parse(s))
            .IsRequired();

        builder.Property(x => x.RecipientEmail)
            .HasColumnName("recipient_email")
            .HasMaxLength(255)
            .IsRequired(false);

        builder.Property(x => x.OperatorId)
            .HasColumnName("operator_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.TripId)
            .HasColumnName("trip_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.DropoffStopId)
            .HasColumnName("dropoff_stop_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(x => x.BookingId)
            .HasColumnName("booking_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(x => x.Description)
            .HasColumnName("description")
            .HasColumnType("text")
            .IsRequired(false);

        builder.Property(x => x.PhotoUrl)
            .HasColumnName("photo_url")
            .HasColumnType("text")
            .IsRequired(false);

        ConfigurePhotoUrls(builder, x => x.CheckInPhotoUrls, "check_in_photo_urls");
        ConfigurePhotoUrls(builder, x => x.DeliveryPhotoUrls, "delivery_photo_urls");

        builder.Property(x => x.SizeCategory)
            .HasColumnName("size_category")
            .HasColumnType(ParcelSizeCategoryType)
            .IsRequired();

        builder.Property(x => x.EstimatedSizeCategory)
            .HasColumnName("estimated_size_category")
            .HasColumnType(ParcelSizeCategoryType)
            .IsRequired();

        builder.Property(x => x.ActualSizeCategory)
            .HasColumnName("actual_size_category")
            .HasColumnType(ParcelSizeCategoryType)
            .IsRequired(false);

        builder.Property(x => x.EstimatedLengthCm)
            .HasColumnName("estimated_length_cm")
            .HasColumnType("decimal(8,2)")
            .HasDefaultValue(1m);
        builder.Property(x => x.EstimatedWidthCm)
            .HasColumnName("estimated_width_cm")
            .HasColumnType("decimal(8,2)")
            .HasDefaultValue(1m);
        builder.Property(x => x.EstimatedHeightCm)
            .HasColumnName("estimated_height_cm")
            .HasColumnType("decimal(8,2)")
            .HasDefaultValue(1m);
        builder.Property(x => x.EstimatedWeightKg)
            .HasColumnName("estimated_weight_kg")
            .HasColumnType("decimal(8,2)")
            .IsRequired();
        builder.Property(x => x.EstimatedVolumeM3)
            .HasColumnName("estimated_volume_m3")
            .HasColumnType("decimal(10,4)")
            .HasDefaultValue(0.0001m);
        builder.Property(x => x.EstimatedDimWeightKg)
            .HasColumnName("estimated_dim_weight_kg")
            .HasColumnType("decimal(8,2)")
            .HasDefaultValue(0.01m);
        builder.Property(x => x.EstimatedChargeableWeightKg)
            .HasColumnName("estimated_chargeable_weight_kg")
            .HasColumnType("decimal(8,2)")
            .HasDefaultValue(0.01m);

        builder.Property(x => x.ActualLengthCm)
            .HasColumnName("actual_length_cm")
            .HasColumnType("decimal(8,2)")
            .IsRequired(false);
        builder.Property(x => x.ActualWidthCm)
            .HasColumnName("actual_width_cm")
            .HasColumnType("decimal(8,2)")
            .IsRequired(false);
        builder.Property(x => x.ActualHeightCm)
            .HasColumnName("actual_height_cm")
            .HasColumnType("decimal(8,2)")
            .IsRequired(false);
        builder.Property(x => x.ActualWeightKg)
            .HasColumnName("actual_weight_kg")
            .HasColumnType("decimal(8,2)")
            .IsRequired(false);
        builder.Property(x => x.ActualVolumeM3)
            .HasColumnName("actual_volume_m3")
            .HasColumnType("decimal(10,4)")
            .IsRequired(false);
        builder.Property(x => x.ActualDimWeightKg)
            .HasColumnName("actual_dim_weight_kg")
            .HasColumnType("decimal(8,2)")
            .IsRequired(false);
        builder.Property(x => x.ActualChargeableWeightKg)
            .HasColumnName("actual_chargeable_weight_kg")
            .HasColumnType("decimal(8,2)")
            .IsRequired(false);

        builder.Property(x => x.DeliveryMethod)
            .HasColumnName("delivery_method")
            .HasColumnType(ParcelDeliveryMethodType)
            .HasDefaultValueSql("'TERMINAL_PICKUP'")
            .IsRequired();

        builder.Property(x => x.TotalPrice)
            .HasColumnName("total_price_vnd")
            .HasColumnType("bigint")
            .HasConversion(m => m.Amount, amount => Money.FromRaw(amount))
            .HasDefaultValueSql("0")
            .IsRequired();
        builder.Property(x => x.DepositPercent)
            .HasColumnName("deposit_percent")
            .HasColumnType("decimal(5,2)")
            .HasDefaultValue(100m)
            .IsRequired();
        builder.Property(x => x.DepositAmount)
            .HasColumnName("deposit_amount")
            .HasColumnType("bigint")
            .HasConversion(m => m.Amount, amount => Money.FromRaw(amount))
            .IsRequired();

        builder.Property(x => x.OriginalDepositAmount)
            .HasColumnName("original_deposit_amount")
            .HasColumnType("bigint")
            .HasConversion(m => m.Amount, amount => Money.FromRaw(amount))
            .IsRequired();

        builder.Property(x => x.DiscountAmount)
            .HasColumnName("discount_amount")
            .HasColumnType("bigint")
            .HasConversion(m => m.Amount, amount => Money.FromRaw(amount))
            .HasDefaultValueSql("0")
            .IsRequired();

        builder.Property(x => x.VoucherCode)
            .HasColumnName("voucher_code")
            .HasMaxLength(50)
            .IsRequired(false);

        builder.Property(x => x.VoucherUsageId)
            .HasColumnName("voucher_usage_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(x => x.AdditionalAmount)
            .HasColumnName("additional_amount")
            .HasColumnType("bigint")
            .HasConversion(m => m.Amount, amount => Money.FromRaw(amount))
            .HasDefaultValueSql("0")
            .IsRequired();
        builder.Property(x => x.RefundAmount)
            .HasColumnName("refund_amount")
            .HasColumnType("bigint")
            .HasConversion(m => m.Amount, amount => Money.FromRaw(amount))
            .HasDefaultValueSql("0")
            .IsRequired();

        builder.Property(x => x.AdditionalPaymentId)
            .HasColumnName("additional_payment_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(x => x.AdditionalPaymentDeadline)
            .HasColumnName("additional_payment_deadline")
            .IsRequired(false);

        ConfigureMoney(builder, x => x.EstimatedGrossPriceVnd, "estimated_gross_price_vnd");
        ConfigureMoney(builder, x => x.FinalGrossPriceVnd, "final_gross_price_vnd");
        ConfigureMoney(builder, x => x.DiscountAmountVnd, "discount_amount_vnd");
        ConfigureMoney(builder, x => x.EstimatedTotalPriceVnd, "estimated_total_price_vnd");
        ConfigureMoney(builder, x => x.FinalTotalPriceVnd, "final_total_price_vnd");
        ConfigureMoney(builder, x => x.DepositRequiredVnd, "deposit_required_vnd");
        ConfigureMoney(builder, x => x.DepositPaidVnd, "deposit_paid_vnd");
        ConfigureMoney(builder, x => x.BalanceRequiredVnd, "balance_required_vnd");
        ConfigureMoney(builder, x => x.BalancePaidVnd, "balance_paid_vnd");
        ConfigureMoney(builder, x => x.RefundDueVnd, "refund_due_vnd");
        ConfigureMoney(builder, x => x.RefundedAmountVnd, "refunded_amount_vnd");
        ConfigureMoney(builder, x => x.ForfeitedDepositVnd, "forfeited_deposit_vnd");

        builder.Property(x => x.DepositPaymentId)
            .HasColumnName("deposit_payment_id")
            .HasColumnType("uuid")
            .IsRequired(false);
        builder.Property(x => x.BalancePaymentId)
            .HasColumnName("balance_payment_id")
            .HasColumnType("uuid")
            .IsRequired(false);
        builder.Property(x => x.FinalPaymentDeadline)
            .HasColumnName("final_payment_deadline")
            .IsRequired(false);
        builder.Property(x => x.LoadCutoffAt)
            .HasColumnName("load_cutoff_at")
            .IsRequired(false);
        builder.Property(x => x.LatestCheckInAt)
            .HasColumnName("latest_check_in_at")
            .IsRequired(false);
        builder.Property(x => x.CheckedInAt)
            .HasColumnName("checked_in_at")
            .IsRequired(false);
        builder.Property(x => x.CheckedInByUserId)
            .HasColumnName("checked_in_by_user_id")
            .HasColumnType("uuid")
            .IsRequired(false);
        builder.Property(x => x.ReweighedAt)
            .HasColumnName("reweighed_at")
            .IsRequired(false);
        builder.Property(x => x.ReweighedByUserId)
            .HasColumnName("reweighed_by_user_id")
            .HasColumnType("uuid")
            .IsRequired(false);
        ConfigureMoney(builder, x => x.PricePerKgVnd, "price_per_kg_vnd");
        ConfigureMoney(builder, x => x.MinimumPriceVnd, "minimum_price_vnd");
        builder.Property(x => x.DimWeightFactor)
            .HasColumnName("dim_weight_factor")
            .HasColumnType("decimal(10,2)")
            .HasDefaultValue(6000m)
            .IsRequired();
        builder.Property(x => x.SettlementPolicyVersion)
            .HasColumnName("settlement_policy_version")
            .HasDefaultValue(1)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasColumnType(ParcelStatusType)
            .IsRequired();
        builder.Property(x => x.PendingActionType)
            .HasColumnName("pending_action_type")
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired(false);
        builder.Property(x => x.PendingActionResumeStatus)
            .HasColumnName("pending_action_resume_status")
            .HasColumnType(ParcelStatusType)
            .IsRequired(false);
        builder.Property(x => x.PendingActionReason)
            .HasColumnName("pending_action_reason")
            .HasColumnType("text")
            .IsRequired(false);

        builder.Property(x => x.RejectionReason)
            .HasColumnName("rejection_reason")
            .HasColumnType("text")
            .IsRequired(false);

        builder.Property(x => x.CancellationReason)
            .HasColumnName("cancellation_reason")
            .HasColumnType("text")
            .IsRequired(false);

        builder.Property(x => x.ReviewDecision)
            .HasColumnName("review_decision")
            .HasColumnType(ParcelReviewDecisionType)
            .IsRequired(false);

        builder.Property(x => x.ReviewedAt)
            .HasColumnName("reviewed_at")
            .IsRequired(false);

        builder.Property(x => x.ReviewedByUserId)
            .HasColumnName("reviewed_by_user_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(x => x.LoadedAt).HasColumnName("loaded_at").IsRequired(false);
        builder.Property(x => x.LoadedByUserId).HasColumnName("loaded_by_user_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.UnloadedAt).HasColumnName("unloaded_at").IsRequired(false);
        builder.Property(x => x.DeliveredPendingConfirmAt).HasColumnName("delivered_pending_confirm_at").IsRequired(false);
        builder.Property(x => x.ConfirmedAt).HasColumnName("confirmed_at").IsRequired(false);
        builder.Property(x => x.ConfirmedByUserId).HasColumnName("confirmed_by_user_id").HasColumnType("uuid").IsRequired(false);
        builder.Property(x => x.ConfirmedByIp).HasColumnName("confirmed_by_ip").HasMaxLength(45).IsRequired(false);
        builder.Property(x => x.ConfirmNote).HasColumnName("confirm_note").HasColumnType("text").IsRequired(false);
        builder.Property(x => x.RejectedAt).HasColumnName("rejected_at").IsRequired(false);
        builder.Property(x => x.LastReminderAt).HasColumnName("last_reminder_at").IsRequired(false);

        builder.Property(x => x.TransferTargetTripId)
            .HasColumnName("transfer_target_trip_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(x => x.TransferRequestedAt).HasColumnName("transfer_requested_at").IsRequired(false);
        builder.Property(x => x.TransferConfirmedAt).HasColumnName("transfer_confirmed_at").IsRequired(false);
        builder.Property(x => x.TransferConfirmedByUserId)
            .HasColumnName("transfer_confirmed_by_user_id")
            .HasColumnType("uuid")
            .IsRequired(false);
        builder.Property(x => x.TransferConfirmationClaimId)
            .HasColumnName("transfer_confirmation_claim_id")
            .HasColumnType("uuid")
            .IsRequired(false);
        builder.Property(x => x.TransferConfirmationClaimedAt)
            .HasColumnName("transfer_confirmation_claimed_at")
            .IsRequired(false);
        builder.Property(x => x.TransferConfirmationClaimedByUserId)
            .HasColumnName("transfer_confirmation_claimed_by_user_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(x => x.ReturnReason)
            .HasColumnName("return_reason")
            .HasColumnType("text")
            .IsRequired(false);

        builder.Property(x => x.ReturnedAt).HasColumnName("returned_at").IsRequired(false);
        builder.Property(x => x.ReturnedByUserId).HasColumnName("returned_by_user_id").HasColumnType("uuid").IsRequired(false);

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Ignore(x => x.RowVersion);

        builder.HasIndex(x => x.ParcelCode)
            .HasDatabaseName("uq_parcels_parcel_code")
            .IsUnique();

        builder.HasIndex(x => new { x.SenderUserId, x.CreatedAt })
            .HasDatabaseName("idx_parcels_sender_user_id_created_at")
            .IsDescending(false, true);

        builder.HasIndex(x => new { x.RecipientUserId, x.CreatedAt })
            .HasDatabaseName("idx_parcels_recipient_user_id_created_at")
            .HasFilter("recipient_user_id IS NOT NULL")
            .IsDescending(false, true);

        builder.HasIndex(x => new { x.TripId, x.Status })
            .HasDatabaseName("idx_parcels_trip_id_status");

        builder.HasIndex(x => new { x.OperatorId, x.Status })
            .HasDatabaseName("idx_parcels_operator_id_status");

        builder.HasIndex(x => new { x.Status, x.UpdatedAt })
            .HasDatabaseName("idx_parcels_status_updated_at")
            .HasFilter($"status IN ('PENDING_PAYMENT'::{ParcelStatusType}, 'RESERVED'::{ParcelStatusType}, 'CHECKED_IN'::{ParcelStatusType}, 'PENDING_FINAL_PAYMENT'::{ParcelStatusType}, 'READY_TO_LOAD'::{ParcelStatusType}, 'PENDING_OPERATOR_REVIEW'::{ParcelStatusType}, 'PENDING_OPERATOR_ACTION'::{ParcelStatusType}, 'PENDING_TRANSFER_CONFIRM'::{ParcelStatusType}, 'DELIVERED_PENDING_CONFIRM'::{ParcelStatusType}, 'DELIVERY_REJECTED'::{ParcelStatusType}, 'TRANSFER_ESCALATED'::{ParcelStatusType})");

        builder.HasIndex(x => x.AdditionalPaymentDeadline)
            .HasDatabaseName("idx_parcels_additional_payment_deadline")
            .HasFilter($"status = 'PENDING_ADDITIONAL_PAYMENT'::{ParcelStatusType}");

        builder.HasIndex(x => x.LatestCheckInAt)
            .HasDatabaseName("idx_parcels_latest_check_in_at")
            .HasFilter($"status = 'RESERVED'::{ParcelStatusType} AND latest_check_in_at IS NOT NULL");

        builder.HasIndex(x => x.FinalPaymentDeadline)
            .HasDatabaseName("idx_parcels_final_payment_deadline")
            .HasFilter($"status = 'PENDING_FINAL_PAYMENT'::{ParcelStatusType} AND final_payment_deadline IS NOT NULL");

        builder.HasIndex(x => x.DepositPaymentId)
            .HasDatabaseName("idx_parcels_deposit_payment_id")
            .HasFilter("deposit_payment_id IS NOT NULL");

        builder.HasIndex(x => x.BalancePaymentId)
            .HasDatabaseName("idx_parcels_balance_payment_id")
            .HasFilter("balance_payment_id IS NOT NULL");

        builder.HasIndex(x => x.TransferTargetTripId)
            .HasDatabaseName("idx_parcels_transfer_target_trip_id")
            .HasFilter("transfer_target_trip_id IS NOT NULL");

        builder.HasIndex(x => x.TransferConfirmationClaimedAt)
            .HasDatabaseName("idx_parcels_transfer_confirmation_claimed_at")
            .HasFilter(
                "status = 'PENDING_TRANSFER_CONFIRM' AND transfer_confirmation_claim_id IS NOT NULL");

        builder.HasIndex(x => x.AdditionalPaymentId)
            .HasDatabaseName("idx_parcels_additional_payment_id")
            .HasFilter("additional_payment_id IS NOT NULL");

        builder.HasIndex(x => x.VoucherUsageId)
            .HasDatabaseName("idx_parcels_voucher_usage_id")
            .HasFilter("voucher_usage_id IS NOT NULL");

        builder.HasIndex(x => x.ReviewedByUserId)
            .HasDatabaseName("idx_parcels_reviewed_by_user_id")
            .HasFilter("reviewed_by_user_id IS NOT NULL");

        builder.HasIndex(x => x.LoadedByUserId)
            .HasDatabaseName("idx_parcels_loaded_by_user_id")
            .HasFilter("loaded_by_user_id IS NOT NULL");

        builder.HasIndex(x => x.ConfirmedByUserId)
            .HasDatabaseName("idx_parcels_confirmed_by_user_id")
            .HasFilter("confirmed_by_user_id IS NOT NULL");

        builder.HasIndex(x => x.TransferConfirmedByUserId)
            .HasDatabaseName("idx_parcels_transfer_confirmed_by_user_id")
            .HasFilter("transfer_confirmed_by_user_id IS NOT NULL");

        builder.HasIndex(x => x.ReturnedByUserId)
            .HasDatabaseName("idx_parcels_returned_by_user_id")
            .HasFilter("returned_by_user_id IS NOT NULL");

        builder.HasIndex(x => new { x.ConfirmedAt, x.OperatorId })
            .HasDatabaseName("idx_parcels_confirmed_report")
            .HasFilter($"status = 'DELIVERY_CONFIRMED'::{ParcelStatusType} AND confirmed_at IS NOT NULL");
    }

    private static void ConfigureMoney(
        EntityTypeBuilder<ParcelEntity> builder,
        System.Linq.Expressions.Expression<Func<ParcelEntity, Money>> property,
        string columnName)
    {
        builder.Property(property)
            .HasColumnName(columnName)
            .HasColumnType("bigint")
            .HasConversion(m => m.Amount, amount => Money.FromRaw(amount))
            .HasDefaultValueSql("0")
            .IsRequired();
    }

    private static void ConfigurePhotoUrls(
        EntityTypeBuilder<ParcelEntity> builder,
        System.Linq.Expressions.Expression<Func<ParcelEntity, IReadOnlyCollection<string>?>> property,
        string columnName)
    {
        builder.Property(property)
            .HasColumnName(columnName)
            .HasColumnType("jsonb")
            .HasConversion(
                value => System.Text.Json.JsonSerializer.Serialize(
                    value,
                    (System.Text.Json.JsonSerializerOptions?)null),
                value => System.Text.Json.JsonSerializer.Deserialize<string[]>(
                    value,
                    (System.Text.Json.JsonSerializerOptions?)null))
            .Metadata.SetValueComparer(new ValueComparer<IReadOnlyCollection<string>?>(
                (left, right) => left == null
                    ? right == null
                    : right != null && left.SequenceEqual(right),
                value => value == null
                    ? 0
                    : value.Aggregate(
                        0,
                        (hash, item) => HashCode.Combine(
                            hash,
                            item.GetHashCode(StringComparison.Ordinal))),
                value => value == null ? null : value.ToArray()));
    }
}
