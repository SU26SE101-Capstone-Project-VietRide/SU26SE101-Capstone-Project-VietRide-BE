using System;
using Microsoft.EntityFrameworkCore.Migrations;
using VietRide.Payment.Domain.Enums;

#nullable disable

namespace VietRide.Payment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformWalletTransactionLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:vietride_payment.invoice_pdf_generation_status", "PENDING,PROCESSING,FAILED,COMPLETED")
                .Annotation("Npgsql:Enum:vietride_payment.invoice_status", "DRAFT,ISSUED,CANCELLED")
                .Annotation("Npgsql:Enum:vietride_payment.operator_ledger_adjustment_reason", "VIETRIDE_FUNDED_VOUCHER_REVERSAL,GENERIC_BOOKING_REFUND_ENTITLEMENT,MANUAL_WALLET_ADJUSTMENT,LEGACY_UNCLASSIFIED")
                .Annotation("Npgsql:Enum:vietride_payment.operator_ledger_entry_type", "BOOKING_REVENUE,PARCEL_REVENUE,BOOKING_REFUND,PARCEL_REFUND,VOUCHER_VIETRIDE_FUNDED_CREDIT,VOUCHER_OPERATOR_FUNDED_AUDIT,ADJUSTMENT,PARCEL_COMPENSATION")
                .Annotation("Npgsql:Enum:vietride_payment.operator_ledger_reference_type", "BOOKING,PARCEL,VOUCHER_USAGE,MANUAL")
                .Annotation("Npgsql:Enum:vietride_payment.operator_trip_settlement_method", "AUTO_WEEKLY,ADMIN_MANUAL")
                .Annotation("Npgsql:Enum:vietride_payment.operator_trip_settlement_status", "PENDING_HOLD,ELIGIBLE,SETTLED,CANCELLED")
                .Annotation("Npgsql:Enum:vietride_payment.operator_wallet_transaction_ref", "TRIP_SETTLEMENT,ADJUSTMENT,SUBSCRIPTION_PAYMENT,PARCEL_COMPENSATION")
                .Annotation("Npgsql:Enum:vietride_payment.operator_wallet_transaction_type", "CREDIT,DEBIT")
                .Annotation("Npgsql:Enum:vietride_payment.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .Annotation("Npgsql:Enum:vietride_payment.payment_method", "WALLET,VNPAY")
                .Annotation("Npgsql:Enum:vietride_payment.payment_reference_type", "BOOKING,BOOKING_GROUP,PARCEL,TOP_UP,SUBSCRIPTION,PARCEL_ADDITIONAL")
                .Annotation("Npgsql:Enum:vietride_payment.payment_status", "PENDING_REDIRECT,SUCCEEDED,FAILED,EXPIRED,REFUNDED")
                .Annotation("Npgsql:Enum:vietride_payment.platform_wallet_transaction_link_type", "BOOKING,PARCEL,TRIP_SETTLEMENT,SUBSCRIPTION,PARCEL_CLAIM")
                .Annotation("Npgsql:Enum:vietride_payment.platform_wallet_transaction_ref", "BOOKING_PAYMENT_HOLD,PARCEL_PAYMENT_HOLD,PARCEL_ADDITIONAL_PAYMENT_HOLD,BOOKING_REFUND,PARCEL_REFUND,TRIP_SETTLEMENT,SUBSCRIPTION_PAYMENT,MANUAL_ADJUSTMENT,PARCEL_COMPENSATION")
                .Annotation("Npgsql:Enum:vietride_payment.platform_wallet_transaction_type", "CREDIT,DEBIT")
                .Annotation("Npgsql:Enum:vietride_payment.top_up_request_status", "PENDING,SUCCEEDED,FAILED,EXPIRED")
                .Annotation("Npgsql:Enum:vietride_payment.vnpay_return_mode", "OPERATOR_WEB,MOBILE_SDK")
                .Annotation("Npgsql:Enum:vietride_payment.wallet_transaction_ref", "TOP_UP,BOOKING_PAYMENT,BOOKING_REFUND,PARCEL_PAYMENT,PARCEL_REFUND,MANUAL_ADJUSTMENT,PARCEL_ADDITIONAL_PAYMENT,PARCEL_COMPENSATION")
                .Annotation("Npgsql:Enum:vietride_payment.wallet_transaction_type", "CREDIT,DEBIT")
                .OldAnnotation("Npgsql:Enum:vietride_payment.invoice_pdf_generation_status", "PENDING,PROCESSING,FAILED,COMPLETED")
                .OldAnnotation("Npgsql:Enum:vietride_payment.invoice_status", "DRAFT,ISSUED,CANCELLED")
                .OldAnnotation("Npgsql:Enum:vietride_payment.operator_ledger_adjustment_reason", "VIETRIDE_FUNDED_VOUCHER_REVERSAL,GENERIC_BOOKING_REFUND_ENTITLEMENT,MANUAL_WALLET_ADJUSTMENT,LEGACY_UNCLASSIFIED")
                .OldAnnotation("Npgsql:Enum:vietride_payment.operator_ledger_entry_type", "BOOKING_REVENUE,PARCEL_REVENUE,BOOKING_REFUND,PARCEL_REFUND,VOUCHER_VIETRIDE_FUNDED_CREDIT,VOUCHER_OPERATOR_FUNDED_AUDIT,ADJUSTMENT,PARCEL_COMPENSATION")
                .OldAnnotation("Npgsql:Enum:vietride_payment.operator_ledger_reference_type", "BOOKING,PARCEL,VOUCHER_USAGE,MANUAL")
                .OldAnnotation("Npgsql:Enum:vietride_payment.operator_trip_settlement_method", "AUTO_WEEKLY,ADMIN_MANUAL")
                .OldAnnotation("Npgsql:Enum:vietride_payment.operator_trip_settlement_status", "PENDING_HOLD,ELIGIBLE,SETTLED,CANCELLED")
                .OldAnnotation("Npgsql:Enum:vietride_payment.operator_wallet_transaction_ref", "TRIP_SETTLEMENT,ADJUSTMENT,SUBSCRIPTION_PAYMENT,PARCEL_COMPENSATION")
                .OldAnnotation("Npgsql:Enum:vietride_payment.operator_wallet_transaction_type", "CREDIT,DEBIT")
                .OldAnnotation("Npgsql:Enum:vietride_payment.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .OldAnnotation("Npgsql:Enum:vietride_payment.payment_method", "WALLET,VNPAY")
                .OldAnnotation("Npgsql:Enum:vietride_payment.payment_reference_type", "BOOKING,BOOKING_GROUP,PARCEL,TOP_UP,SUBSCRIPTION,PARCEL_ADDITIONAL")
                .OldAnnotation("Npgsql:Enum:vietride_payment.payment_status", "PENDING_REDIRECT,SUCCEEDED,FAILED,EXPIRED,REFUNDED")
                .OldAnnotation("Npgsql:Enum:vietride_payment.platform_wallet_transaction_ref", "BOOKING_PAYMENT_HOLD,PARCEL_PAYMENT_HOLD,PARCEL_ADDITIONAL_PAYMENT_HOLD,BOOKING_REFUND,PARCEL_REFUND,TRIP_SETTLEMENT,SUBSCRIPTION_PAYMENT,MANUAL_ADJUSTMENT,PARCEL_COMPENSATION")
                .OldAnnotation("Npgsql:Enum:vietride_payment.platform_wallet_transaction_type", "CREDIT,DEBIT")
                .OldAnnotation("Npgsql:Enum:vietride_payment.top_up_request_status", "PENDING,SUCCEEDED,FAILED,EXPIRED")
                .OldAnnotation("Npgsql:Enum:vietride_payment.vnpay_return_mode", "OPERATOR_WEB,MOBILE_SDK")
                .OldAnnotation("Npgsql:Enum:vietride_payment.wallet_transaction_ref", "TOP_UP,BOOKING_PAYMENT,BOOKING_REFUND,PARCEL_PAYMENT,PARCEL_REFUND,MANUAL_ADJUSTMENT,PARCEL_ADDITIONAL_PAYMENT,PARCEL_COMPENSATION")
                .OldAnnotation("Npgsql:Enum:vietride_payment.wallet_transaction_type", "CREDIT,DEBIT");

            migrationBuilder.CreateTable(
                name: "platform_wallet_transaction_links",
                schema: "vietride_payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    platform_wallet_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    link_type = table.Column<PlatformWalletTransactionLinkType>(type: "vietride_payment.platform_wallet_transaction_link_type", nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reference_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    allocated_amount = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_wallet_transaction_links", x => x.id);
                    table.CheckConstraint("chk_platform_wallet_tx_links_amount_non_negative", "allocated_amount >= 0");
                    table.ForeignKey(
                        name: "fk_platform_wallet_transaction_links_transaction",
                        column: x => x.platform_wallet_transaction_id,
                        principalSchema: "vietride_payment",
                        principalTable: "platform_wallet_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_platform_wallet_transaction_links_operator",
                schema: "vietride_payment",
                table: "platform_wallet_transaction_links",
                column: "operator_id",
                filter: "operator_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_platform_wallet_transaction_links_reference",
                schema: "vietride_payment",
                table: "platform_wallet_transaction_links",
                columns: new[] { "reference_id", "reference_code" },
                filter: "reference_id IS NOT NULL OR reference_code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_platform_wallet_transaction_links_transaction",
                schema: "vietride_payment",
                table: "platform_wallet_transaction_links",
                column: "platform_wallet_transaction_id");

            migrationBuilder.CreateIndex(
                name: "idx_platform_wallet_transaction_links_trip",
                schema: "vietride_payment",
                table: "platform_wallet_transaction_links",
                column: "trip_id",
                filter: "trip_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_platform_wallet_transaction_links_identity",
                schema: "vietride_payment",
                table: "platform_wallet_transaction_links",
                columns: new[] { "platform_wallet_transaction_id", "link_type", "reference_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platform_wallet_transaction_links",
                schema: "vietride_payment");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:vietride_payment.invoice_pdf_generation_status", "PENDING,PROCESSING,FAILED,COMPLETED")
                .Annotation("Npgsql:Enum:vietride_payment.invoice_status", "DRAFT,ISSUED,CANCELLED")
                .Annotation("Npgsql:Enum:vietride_payment.operator_ledger_adjustment_reason", "VIETRIDE_FUNDED_VOUCHER_REVERSAL,GENERIC_BOOKING_REFUND_ENTITLEMENT,MANUAL_WALLET_ADJUSTMENT,LEGACY_UNCLASSIFIED")
                .Annotation("Npgsql:Enum:vietride_payment.operator_ledger_entry_type", "BOOKING_REVENUE,PARCEL_REVENUE,BOOKING_REFUND,PARCEL_REFUND,VOUCHER_VIETRIDE_FUNDED_CREDIT,VOUCHER_OPERATOR_FUNDED_AUDIT,ADJUSTMENT,PARCEL_COMPENSATION")
                .Annotation("Npgsql:Enum:vietride_payment.operator_ledger_reference_type", "BOOKING,PARCEL,VOUCHER_USAGE,MANUAL")
                .Annotation("Npgsql:Enum:vietride_payment.operator_trip_settlement_method", "AUTO_WEEKLY,ADMIN_MANUAL")
                .Annotation("Npgsql:Enum:vietride_payment.operator_trip_settlement_status", "PENDING_HOLD,ELIGIBLE,SETTLED,CANCELLED")
                .Annotation("Npgsql:Enum:vietride_payment.operator_wallet_transaction_ref", "TRIP_SETTLEMENT,ADJUSTMENT,SUBSCRIPTION_PAYMENT,PARCEL_COMPENSATION")
                .Annotation("Npgsql:Enum:vietride_payment.operator_wallet_transaction_type", "CREDIT,DEBIT")
                .Annotation("Npgsql:Enum:vietride_payment.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .Annotation("Npgsql:Enum:vietride_payment.payment_method", "WALLET,VNPAY")
                .Annotation("Npgsql:Enum:vietride_payment.payment_reference_type", "BOOKING,BOOKING_GROUP,PARCEL,TOP_UP,SUBSCRIPTION,PARCEL_ADDITIONAL")
                .Annotation("Npgsql:Enum:vietride_payment.payment_status", "PENDING_REDIRECT,SUCCEEDED,FAILED,EXPIRED,REFUNDED")
                .Annotation("Npgsql:Enum:vietride_payment.platform_wallet_transaction_ref", "BOOKING_PAYMENT_HOLD,PARCEL_PAYMENT_HOLD,PARCEL_ADDITIONAL_PAYMENT_HOLD,BOOKING_REFUND,PARCEL_REFUND,TRIP_SETTLEMENT,SUBSCRIPTION_PAYMENT,MANUAL_ADJUSTMENT,PARCEL_COMPENSATION")
                .Annotation("Npgsql:Enum:vietride_payment.platform_wallet_transaction_type", "CREDIT,DEBIT")
                .Annotation("Npgsql:Enum:vietride_payment.top_up_request_status", "PENDING,SUCCEEDED,FAILED,EXPIRED")
                .Annotation("Npgsql:Enum:vietride_payment.vnpay_return_mode", "OPERATOR_WEB,MOBILE_SDK")
                .Annotation("Npgsql:Enum:vietride_payment.wallet_transaction_ref", "TOP_UP,BOOKING_PAYMENT,BOOKING_REFUND,PARCEL_PAYMENT,PARCEL_REFUND,MANUAL_ADJUSTMENT,PARCEL_ADDITIONAL_PAYMENT,PARCEL_COMPENSATION")
                .Annotation("Npgsql:Enum:vietride_payment.wallet_transaction_type", "CREDIT,DEBIT")
                .OldAnnotation("Npgsql:Enum:vietride_payment.invoice_pdf_generation_status", "PENDING,PROCESSING,FAILED,COMPLETED")
                .OldAnnotation("Npgsql:Enum:vietride_payment.invoice_status", "DRAFT,ISSUED,CANCELLED")
                .OldAnnotation("Npgsql:Enum:vietride_payment.operator_ledger_adjustment_reason", "VIETRIDE_FUNDED_VOUCHER_REVERSAL,GENERIC_BOOKING_REFUND_ENTITLEMENT,MANUAL_WALLET_ADJUSTMENT,LEGACY_UNCLASSIFIED")
                .OldAnnotation("Npgsql:Enum:vietride_payment.operator_ledger_entry_type", "BOOKING_REVENUE,PARCEL_REVENUE,BOOKING_REFUND,PARCEL_REFUND,VOUCHER_VIETRIDE_FUNDED_CREDIT,VOUCHER_OPERATOR_FUNDED_AUDIT,ADJUSTMENT,PARCEL_COMPENSATION")
                .OldAnnotation("Npgsql:Enum:vietride_payment.operator_ledger_reference_type", "BOOKING,PARCEL,VOUCHER_USAGE,MANUAL")
                .OldAnnotation("Npgsql:Enum:vietride_payment.operator_trip_settlement_method", "AUTO_WEEKLY,ADMIN_MANUAL")
                .OldAnnotation("Npgsql:Enum:vietride_payment.operator_trip_settlement_status", "PENDING_HOLD,ELIGIBLE,SETTLED,CANCELLED")
                .OldAnnotation("Npgsql:Enum:vietride_payment.operator_wallet_transaction_ref", "TRIP_SETTLEMENT,ADJUSTMENT,SUBSCRIPTION_PAYMENT,PARCEL_COMPENSATION")
                .OldAnnotation("Npgsql:Enum:vietride_payment.operator_wallet_transaction_type", "CREDIT,DEBIT")
                .OldAnnotation("Npgsql:Enum:vietride_payment.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .OldAnnotation("Npgsql:Enum:vietride_payment.payment_method", "WALLET,VNPAY")
                .OldAnnotation("Npgsql:Enum:vietride_payment.payment_reference_type", "BOOKING,BOOKING_GROUP,PARCEL,TOP_UP,SUBSCRIPTION,PARCEL_ADDITIONAL")
                .OldAnnotation("Npgsql:Enum:vietride_payment.payment_status", "PENDING_REDIRECT,SUCCEEDED,FAILED,EXPIRED,REFUNDED")
                .OldAnnotation("Npgsql:Enum:vietride_payment.platform_wallet_transaction_link_type", "BOOKING,PARCEL,TRIP_SETTLEMENT,SUBSCRIPTION,PARCEL_CLAIM")
                .OldAnnotation("Npgsql:Enum:vietride_payment.platform_wallet_transaction_ref", "BOOKING_PAYMENT_HOLD,PARCEL_PAYMENT_HOLD,PARCEL_ADDITIONAL_PAYMENT_HOLD,BOOKING_REFUND,PARCEL_REFUND,TRIP_SETTLEMENT,SUBSCRIPTION_PAYMENT,MANUAL_ADJUSTMENT,PARCEL_COMPENSATION")
                .OldAnnotation("Npgsql:Enum:vietride_payment.platform_wallet_transaction_type", "CREDIT,DEBIT")
                .OldAnnotation("Npgsql:Enum:vietride_payment.top_up_request_status", "PENDING,SUCCEEDED,FAILED,EXPIRED")
                .OldAnnotation("Npgsql:Enum:vietride_payment.vnpay_return_mode", "OPERATOR_WEB,MOBILE_SDK")
                .OldAnnotation("Npgsql:Enum:vietride_payment.wallet_transaction_ref", "TOP_UP,BOOKING_PAYMENT,BOOKING_REFUND,PARCEL_PAYMENT,PARCEL_REFUND,MANUAL_ADJUSTMENT,PARCEL_ADDITIONAL_PAYMENT,PARCEL_COMPENSATION")
                .OldAnnotation("Npgsql:Enum:vietride_payment.wallet_transaction_type", "CREDIT,DEBIT");
        }
    }
}
