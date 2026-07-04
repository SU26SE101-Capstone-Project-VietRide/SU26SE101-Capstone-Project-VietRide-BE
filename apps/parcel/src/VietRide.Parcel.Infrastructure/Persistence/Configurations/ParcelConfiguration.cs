using Microsoft.EntityFrameworkCore;
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
            table.HasCheckConstraint("chk_parcels_weight_positive", "estimated_weight_kg > 0");
            table.HasCheckConstraint("chk_parcels_actual_weight_positive", "actual_weight_kg IS NULL OR actual_weight_kg > 0");
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

        builder.Property(x => x.SizeCategory)
            .HasColumnName("size_category")
            .HasColumnType(ParcelSizeCategoryType)
            .IsRequired();

        builder.Property(x => x.EstimatedWeightKg)
            .HasColumnName("estimated_weight_kg")
            .HasColumnType("decimal(8,2)")
            .IsRequired();

        builder.Property(x => x.ActualWeightKg)
            .HasColumnName("actual_weight_kg")
            .HasColumnType("decimal(8,2)")
            .IsRequired(false);

        builder.Property(x => x.DeliveryMethod)
            .HasColumnName("delivery_method")
            .HasColumnType(ParcelDeliveryMethodType)
            .HasDefaultValueSql("'TERMINAL_PICKUP'")
            .IsRequired();

        builder.Property(x => x.DepositAmount)
            .HasColumnName("deposit_amount")
            .HasColumnType("bigint")
            .HasConversion(m => m.Amount, amount => Money.FromRaw(amount))
            .IsRequired();

        builder.Property(x => x.AdditionalAmount)
            .HasColumnName("additional_amount")
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

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasColumnType(ParcelStatusType)
            .IsRequired();

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

        builder.Property(x => x.DeliveryToken)
            .HasColumnName("delivery_token")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(x => x.DeliveryTokenExpiresAt)
            .HasColumnName("delivery_token_expires_at")
            .IsRequired(false);

        builder.Property(x => x.DeliveryTokenRevokedAt)
            .HasColumnName("delivery_token_revoked_at")
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

        builder.HasIndex(x => x.DeliveryToken)
            .HasDatabaseName("uq_parcels_delivery_token")
            .HasFilter("delivery_token IS NOT NULL")
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
            .HasFilter($"status IN ('PENDING'::{ParcelStatusType}, 'PENDING_ADDITIONAL_PAYMENT'::{ParcelStatusType}, 'PENDING_OPERATOR_REVIEW'::{ParcelStatusType}, 'PENDING_OPERATOR_ACTION'::{ParcelStatusType}, 'PENDING_TRANSFER_CONFIRM'::{ParcelStatusType}, 'DELIVERED_PENDING_CONFIRM'::{ParcelStatusType}, 'DELIVERY_REJECTED'::{ParcelStatusType}, 'TRANSFER_ESCALATED'::{ParcelStatusType})");

        builder.HasIndex(x => x.AdditionalPaymentDeadline)
            .HasDatabaseName("idx_parcels_additional_payment_deadline")
            .HasFilter($"status = 'PENDING_ADDITIONAL_PAYMENT'::{ParcelStatusType}");

        builder.HasIndex(x => x.TransferTargetTripId)
            .HasDatabaseName("idx_parcels_transfer_target_trip_id")
            .HasFilter("transfer_target_trip_id IS NOT NULL");

        builder.HasIndex(x => x.AdditionalPaymentId)
            .HasDatabaseName("idx_parcels_additional_payment_id")
            .HasFilter("additional_payment_id IS NOT NULL");

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
    }
}
