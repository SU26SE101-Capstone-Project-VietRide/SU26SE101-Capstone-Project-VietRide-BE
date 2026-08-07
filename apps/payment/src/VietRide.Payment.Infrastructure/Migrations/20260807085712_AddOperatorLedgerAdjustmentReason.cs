using Microsoft.EntityFrameworkCore.Migrations;
using VietRide.Payment.Domain.Enums;

#nullable disable

namespace VietRide.Payment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOperatorLedgerAdjustmentReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:vietride_payment.invoice_pdf_generation_status", "PENDING,PROCESSING,FAILED,COMPLETED")
                .Annotation("Npgsql:Enum:vietride_payment.invoice_status", "DRAFT,ISSUED,CANCELLED")
                .Annotation("Npgsql:Enum:vietride_payment.operator_ledger_adjustment_reason", "VIETRIDE_FUNDED_VOUCHER_REVERSAL,GENERIC_BOOKING_REFUND_ENTITLEMENT,MANUAL_WALLET_ADJUSTMENT,LEGACY_UNCLASSIFIED")
                .Annotation("Npgsql:Enum:vietride_payment.operator_ledger_entry_type", "BOOKING_REVENUE,PARCEL_REVENUE,BOOKING_REFUND,PARCEL_REFUND,VOUCHER_VIETRIDE_FUNDED_CREDIT,VOUCHER_OPERATOR_FUNDED_AUDIT,ADJUSTMENT")
                .Annotation("Npgsql:Enum:vietride_payment.operator_ledger_reference_type", "BOOKING,PARCEL,VOUCHER_USAGE,MANUAL")
                .Annotation("Npgsql:Enum:vietride_payment.operator_trip_settlement_method", "AUTO_WEEKLY,ADMIN_MANUAL")
                .Annotation("Npgsql:Enum:vietride_payment.operator_trip_settlement_status", "PENDING_HOLD,ELIGIBLE,SETTLED,CANCELLED")
                .Annotation("Npgsql:Enum:vietride_payment.operator_wallet_transaction_ref", "TRIP_SETTLEMENT,ADJUSTMENT,SUBSCRIPTION_PAYMENT")
                .Annotation("Npgsql:Enum:vietride_payment.operator_wallet_transaction_type", "CREDIT,DEBIT")
                .Annotation("Npgsql:Enum:vietride_payment.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .Annotation("Npgsql:Enum:vietride_payment.payment_method", "WALLET,VNPAY")
                .Annotation("Npgsql:Enum:vietride_payment.payment_reference_type", "BOOKING,BOOKING_GROUP,PARCEL,TOP_UP,SUBSCRIPTION,PARCEL_ADDITIONAL")
                .Annotation("Npgsql:Enum:vietride_payment.payment_status", "PENDING_REDIRECT,SUCCEEDED,FAILED,EXPIRED,REFUNDED")
                .Annotation("Npgsql:Enum:vietride_payment.platform_wallet_transaction_ref", "BOOKING_PAYMENT_HOLD,PARCEL_PAYMENT_HOLD,PARCEL_ADDITIONAL_PAYMENT_HOLD,BOOKING_REFUND,PARCEL_REFUND,TRIP_SETTLEMENT,SUBSCRIPTION_PAYMENT,MANUAL_ADJUSTMENT")
                .Annotation("Npgsql:Enum:vietride_payment.platform_wallet_transaction_type", "CREDIT,DEBIT")
                .Annotation("Npgsql:Enum:vietride_payment.top_up_request_status", "PENDING,SUCCEEDED,FAILED,EXPIRED")
                .Annotation("Npgsql:Enum:vietride_payment.wallet_transaction_ref", "TOP_UP,BOOKING_PAYMENT,BOOKING_REFUND,PARCEL_PAYMENT,PARCEL_REFUND,MANUAL_ADJUSTMENT,PARCEL_ADDITIONAL_PAYMENT")
                .Annotation("Npgsql:Enum:vietride_payment.wallet_transaction_type", "CREDIT,DEBIT")
                .OldAnnotation("Npgsql:Enum:vietride_payment.invoice_pdf_generation_status", "PENDING,PROCESSING,FAILED,COMPLETED")
                .OldAnnotation("Npgsql:Enum:vietride_payment.invoice_status", "DRAFT,ISSUED,CANCELLED")
                .OldAnnotation("Npgsql:Enum:vietride_payment.operator_ledger_entry_type", "BOOKING_REVENUE,PARCEL_REVENUE,BOOKING_REFUND,PARCEL_REFUND,VOUCHER_VIETRIDE_FUNDED_CREDIT,VOUCHER_OPERATOR_FUNDED_AUDIT,ADJUSTMENT")
                .OldAnnotation("Npgsql:Enum:vietride_payment.operator_ledger_reference_type", "BOOKING,PARCEL,VOUCHER_USAGE,MANUAL")
                .OldAnnotation("Npgsql:Enum:vietride_payment.operator_trip_settlement_method", "AUTO_WEEKLY,ADMIN_MANUAL")
                .OldAnnotation("Npgsql:Enum:vietride_payment.operator_trip_settlement_status", "PENDING_HOLD,ELIGIBLE,SETTLED,CANCELLED")
                .OldAnnotation("Npgsql:Enum:vietride_payment.operator_wallet_transaction_ref", "TRIP_SETTLEMENT,ADJUSTMENT,SUBSCRIPTION_PAYMENT")
                .OldAnnotation("Npgsql:Enum:vietride_payment.operator_wallet_transaction_type", "CREDIT,DEBIT")
                .OldAnnotation("Npgsql:Enum:vietride_payment.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .OldAnnotation("Npgsql:Enum:vietride_payment.payment_method", "WALLET,VNPAY")
                .OldAnnotation("Npgsql:Enum:vietride_payment.payment_reference_type", "BOOKING,BOOKING_GROUP,PARCEL,TOP_UP,SUBSCRIPTION,PARCEL_ADDITIONAL")
                .OldAnnotation("Npgsql:Enum:vietride_payment.payment_status", "PENDING_REDIRECT,SUCCEEDED,FAILED,EXPIRED,REFUNDED")
                .OldAnnotation("Npgsql:Enum:vietride_payment.platform_wallet_transaction_ref", "BOOKING_PAYMENT_HOLD,PARCEL_PAYMENT_HOLD,PARCEL_ADDITIONAL_PAYMENT_HOLD,BOOKING_REFUND,PARCEL_REFUND,TRIP_SETTLEMENT,SUBSCRIPTION_PAYMENT,MANUAL_ADJUSTMENT")
                .OldAnnotation("Npgsql:Enum:vietride_payment.platform_wallet_transaction_type", "CREDIT,DEBIT")
                .OldAnnotation("Npgsql:Enum:vietride_payment.top_up_request_status", "PENDING,SUCCEEDED,FAILED,EXPIRED")
                .OldAnnotation("Npgsql:Enum:vietride_payment.wallet_transaction_ref", "TOP_UP,BOOKING_PAYMENT,BOOKING_REFUND,PARCEL_PAYMENT,PARCEL_REFUND,MANUAL_ADJUSTMENT,PARCEL_ADDITIONAL_PAYMENT")
                .OldAnnotation("Npgsql:Enum:vietride_payment.wallet_transaction_type", "CREDIT,DEBIT");

            migrationBuilder.AddColumn<OperatorLedgerAdjustmentReason>(
                name: "adjustment_reason",
                schema: "vietride_payment",
                table: "operator_ledger_entries",
                type: "vietride_payment.operator_ledger_adjustment_reason",
                nullable: true);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "adjustment_reason",
                schema: "vietride_payment",
                table: "operator_ledger_entries");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:vietride_payment.invoice_pdf_generation_status", "PENDING,PROCESSING,FAILED,COMPLETED")
                .Annotation("Npgsql:Enum:vietride_payment.invoice_status", "DRAFT,ISSUED,CANCELLED")
                .Annotation("Npgsql:Enum:vietride_payment.operator_ledger_entry_type", "BOOKING_REVENUE,PARCEL_REVENUE,BOOKING_REFUND,PARCEL_REFUND,VOUCHER_VIETRIDE_FUNDED_CREDIT,VOUCHER_OPERATOR_FUNDED_AUDIT,ADJUSTMENT")
                .Annotation("Npgsql:Enum:vietride_payment.operator_ledger_reference_type", "BOOKING,PARCEL,VOUCHER_USAGE,MANUAL")
                .Annotation("Npgsql:Enum:vietride_payment.operator_trip_settlement_method", "AUTO_WEEKLY,ADMIN_MANUAL")
                .Annotation("Npgsql:Enum:vietride_payment.operator_trip_settlement_status", "PENDING_HOLD,ELIGIBLE,SETTLED,CANCELLED")
                .Annotation("Npgsql:Enum:vietride_payment.operator_wallet_transaction_ref", "TRIP_SETTLEMENT,ADJUSTMENT,SUBSCRIPTION_PAYMENT")
                .Annotation("Npgsql:Enum:vietride_payment.operator_wallet_transaction_type", "CREDIT,DEBIT")
                .Annotation("Npgsql:Enum:vietride_payment.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .Annotation("Npgsql:Enum:vietride_payment.payment_method", "WALLET,VNPAY")
                .Annotation("Npgsql:Enum:vietride_payment.payment_reference_type", "BOOKING,BOOKING_GROUP,PARCEL,TOP_UP,SUBSCRIPTION,PARCEL_ADDITIONAL")
                .Annotation("Npgsql:Enum:vietride_payment.payment_status", "PENDING_REDIRECT,SUCCEEDED,FAILED,EXPIRED,REFUNDED")
                .Annotation("Npgsql:Enum:vietride_payment.platform_wallet_transaction_ref", "BOOKING_PAYMENT_HOLD,PARCEL_PAYMENT_HOLD,PARCEL_ADDITIONAL_PAYMENT_HOLD,BOOKING_REFUND,PARCEL_REFUND,TRIP_SETTLEMENT,SUBSCRIPTION_PAYMENT,MANUAL_ADJUSTMENT")
                .Annotation("Npgsql:Enum:vietride_payment.platform_wallet_transaction_type", "CREDIT,DEBIT")
                .Annotation("Npgsql:Enum:vietride_payment.top_up_request_status", "PENDING,SUCCEEDED,FAILED,EXPIRED")
                .Annotation("Npgsql:Enum:vietride_payment.wallet_transaction_ref", "TOP_UP,BOOKING_PAYMENT,BOOKING_REFUND,PARCEL_PAYMENT,PARCEL_REFUND,MANUAL_ADJUSTMENT,PARCEL_ADDITIONAL_PAYMENT")
                .Annotation("Npgsql:Enum:vietride_payment.wallet_transaction_type", "CREDIT,DEBIT")
                .OldAnnotation("Npgsql:Enum:vietride_payment.invoice_pdf_generation_status", "PENDING,PROCESSING,FAILED,COMPLETED")
                .OldAnnotation("Npgsql:Enum:vietride_payment.invoice_status", "DRAFT,ISSUED,CANCELLED")
                .OldAnnotation("Npgsql:Enum:vietride_payment.operator_ledger_adjustment_reason", "VIETRIDE_FUNDED_VOUCHER_REVERSAL,GENERIC_BOOKING_REFUND_ENTITLEMENT,MANUAL_WALLET_ADJUSTMENT,LEGACY_UNCLASSIFIED")
                .OldAnnotation("Npgsql:Enum:vietride_payment.operator_ledger_entry_type", "BOOKING_REVENUE,PARCEL_REVENUE,BOOKING_REFUND,PARCEL_REFUND,VOUCHER_VIETRIDE_FUNDED_CREDIT,VOUCHER_OPERATOR_FUNDED_AUDIT,ADJUSTMENT")
                .OldAnnotation("Npgsql:Enum:vietride_payment.operator_ledger_reference_type", "BOOKING,PARCEL,VOUCHER_USAGE,MANUAL")
                .OldAnnotation("Npgsql:Enum:vietride_payment.operator_trip_settlement_method", "AUTO_WEEKLY,ADMIN_MANUAL")
                .OldAnnotation("Npgsql:Enum:vietride_payment.operator_trip_settlement_status", "PENDING_HOLD,ELIGIBLE,SETTLED,CANCELLED")
                .OldAnnotation("Npgsql:Enum:vietride_payment.operator_wallet_transaction_ref", "TRIP_SETTLEMENT,ADJUSTMENT,SUBSCRIPTION_PAYMENT")
                .OldAnnotation("Npgsql:Enum:vietride_payment.operator_wallet_transaction_type", "CREDIT,DEBIT")
                .OldAnnotation("Npgsql:Enum:vietride_payment.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .OldAnnotation("Npgsql:Enum:vietride_payment.payment_method", "WALLET,VNPAY")
                .OldAnnotation("Npgsql:Enum:vietride_payment.payment_reference_type", "BOOKING,BOOKING_GROUP,PARCEL,TOP_UP,SUBSCRIPTION,PARCEL_ADDITIONAL")
                .OldAnnotation("Npgsql:Enum:vietride_payment.payment_status", "PENDING_REDIRECT,SUCCEEDED,FAILED,EXPIRED,REFUNDED")
                .OldAnnotation("Npgsql:Enum:vietride_payment.platform_wallet_transaction_ref", "BOOKING_PAYMENT_HOLD,PARCEL_PAYMENT_HOLD,PARCEL_ADDITIONAL_PAYMENT_HOLD,BOOKING_REFUND,PARCEL_REFUND,TRIP_SETTLEMENT,SUBSCRIPTION_PAYMENT,MANUAL_ADJUSTMENT")
                .OldAnnotation("Npgsql:Enum:vietride_payment.platform_wallet_transaction_type", "CREDIT,DEBIT")
                .OldAnnotation("Npgsql:Enum:vietride_payment.top_up_request_status", "PENDING,SUCCEEDED,FAILED,EXPIRED")
                .OldAnnotation("Npgsql:Enum:vietride_payment.wallet_transaction_ref", "TOP_UP,BOOKING_PAYMENT,BOOKING_REFUND,PARCEL_PAYMENT,PARCEL_REFUND,MANUAL_ADJUSTMENT,PARCEL_ADDITIONAL_PAYMENT")
                .OldAnnotation("Npgsql:Enum:vietride_payment.wallet_transaction_type", "CREDIT,DEBIT");
        }
    }
}
