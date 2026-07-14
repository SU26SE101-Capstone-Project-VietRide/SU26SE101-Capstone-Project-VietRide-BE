using System;
using Microsoft.EntityFrameworkCore.Migrations;
using VietRide.Payment.Domain.Enums;

#nullable disable

namespace VietRide.Payment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDay38InvoiceOperatorWalletSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                .OldAnnotation("Npgsql:Enum:vietride_payment.outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .OldAnnotation("Npgsql:Enum:vietride_payment.payment_method", "WALLET,VNPAY")
                .OldAnnotation("Npgsql:Enum:vietride_payment.payment_reference_type", "BOOKING,BOOKING_GROUP,PARCEL,TOP_UP,SUBSCRIPTION,PARCEL_ADDITIONAL")
                .OldAnnotation("Npgsql:Enum:vietride_payment.payment_status", "PENDING_REDIRECT,SUCCEEDED,FAILED,EXPIRED,REFUNDED")
                .OldAnnotation("Npgsql:Enum:vietride_payment.platform_wallet_transaction_ref", "BOOKING_PAYMENT_HOLD,PARCEL_PAYMENT_HOLD,PARCEL_ADDITIONAL_PAYMENT_HOLD,BOOKING_REFUND,PARCEL_REFUND,TRIP_SETTLEMENT,SUBSCRIPTION_PAYMENT,MANUAL_ADJUSTMENT")
                .OldAnnotation("Npgsql:Enum:vietride_payment.platform_wallet_transaction_type", "CREDIT,DEBIT")
                .OldAnnotation("Npgsql:Enum:vietride_payment.top_up_request_status", "PENDING,SUCCEEDED,FAILED,EXPIRED")
                .OldAnnotation("Npgsql:Enum:vietride_payment.wallet_transaction_ref", "TOP_UP,BOOKING_PAYMENT,BOOKING_REFUND,PARCEL_PAYMENT,PARCEL_REFUND,MANUAL_ADJUSTMENT,PARCEL_ADDITIONAL_PAYMENT")
                .OldAnnotation("Npgsql:Enum:vietride_payment.wallet_transaction_type", "CREDIT,DEBIT");

            migrationBuilder.AddColumn<string>(
                name: "context",
                schema: "vietride_payment",
                table: "payments",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.AddColumn<bool>(
                name: "context_reconciliation_required",
                schema: "vietride_payment",
                table: "payments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "invoice_number_counters",
                schema: "vietride_payment",
                columns: table => new
                {
                    period_key = table.Column<string>(type: "char(6)", nullable: false),
                    last_value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_number_counters", x => x.period_key);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                schema: "vietride_payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    invoice_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operator_subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    period_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    period_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<InvoiceStatus>(type: "vietride_payment.invoice_status", nullable: false, defaultValueSql: "'DRAFT'"),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    pdf_url = table.Column<string>(type: "text", nullable: true),
                    storage_object_path = table.Column<string>(type: "text", nullable: true),
                    pdf_generation_status = table.Column<InvoicePdfGenerationStatus>(type: "vietride_payment.invoice_pdf_generation_status", nullable: false, defaultValueSql: "'PENDING'"),
                    pdf_generation_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    pdf_generation_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    pdf_generation_next_retry_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    pdf_generation_last_error = table.Column<string>(type: "text", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoices", x => x.id);
                    table.CheckConstraint("chk_invoices_amount_non_negative", "amount >= 0");
                    table.CheckConstraint("chk_invoices_issued_consistency", "status <> 'ISSUED' OR (issued_at IS NOT NULL AND pdf_url IS NOT NULL AND storage_object_path IS NOT NULL AND pdf_generation_status = 'COMPLETED')");
                    table.CheckConstraint("chk_invoices_pdf_attempts", "pdf_generation_attempts >= 0 AND pdf_generation_attempts <= 5");
                    table.CheckConstraint("chk_invoices_period_order", "period_to > period_from");
                    table.ForeignKey(
                        name: "fk_invoices_payments_payment_id",
                        column: x => x.payment_id,
                        principalSchema: "vietride_payment",
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "operator_ledger_entries",
                schema: "vietride_payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entry_type = table.Column<OperatorLedgerEntryType>(type: "vietride_payment.operator_ledger_entry_type", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    reference_type = table.Column<OperatorLedgerReferenceType>(type: "vietride_payment.operator_ledger_reference_type", nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operator_ledger_entries", x => x.id);
                    table.CheckConstraint("chk_operator_ledger_entries_amount_direction", "(entry_type IN ('BOOKING_REFUND','PARCEL_REFUND') AND amount < 0) OR (entry_type = 'VOUCHER_OPERATOR_FUNDED_AUDIT' AND amount = 0) OR (entry_type = 'ADJUSTMENT') OR (entry_type NOT IN ('BOOKING_REFUND','PARCEL_REFUND','VOUCHER_OPERATOR_FUNDED_AUDIT','ADJUSTMENT') AND amount > 0)");
                    table.CheckConstraint("chk_operator_ledger_entries_trip_required", "entry_type = 'ADJUSTMENT' OR trip_id IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "operator_wallet_transactions",
                schema: "vietride_payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<OperatorWalletTransactionType>(type: "vietride_payment.operator_wallet_transaction_type", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    balance_before = table.Column<long>(type: "bigint", nullable: false),
                    balance_after = table.Column<long>(type: "bigint", nullable: false),
                    reference_type = table.Column<OperatorWalletTransactionRef>(type: "vietride_payment.operator_wallet_transaction_ref", nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operator_wallet_transactions", x => x.id);
                    table.CheckConstraint("chk_operator_wallet_transactions_amount_positive", "amount > 0");
                    table.CheckConstraint("chk_operator_wallet_transactions_balance_non_negative", "balance_before >= 0 AND balance_after >= 0");
                });

            migrationBuilder.CreateTable(
                name: "operator_wallets",
                schema: "vietride_payment",
                columns: table => new
                {
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    balance = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "0"),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "VND"),
                    row_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operator_wallets", x => x.operator_id);
                    table.CheckConstraint("chk_operator_wallets_balance_non_negative", "balance >= 0");
                });

            migrationBuilder.CreateTable(
                name: "processed_integration_events",
                schema: "vietride_payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    consumer = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processed_integration_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "operator_trip_settlements",
                schema: "vietride_payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    net_amount = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    trip_terminal_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    eligible_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<OperatorTripSettlementStatus>(type: "vietride_payment.operator_trip_settlement_status", nullable: false, defaultValueSql: "'PENDING_HOLD'"),
                    settlement_method = table.Column<OperatorTripSettlementMethod>(type: "vietride_payment.operator_trip_settlement_method", nullable: true),
                    settled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    settled_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    wallet_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    settlement_failure_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_settlement_failure_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    active_failure_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    failure_resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    row_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operator_trip_settlements", x => x.id);
                    table.CheckConstraint("chk_operator_trip_settlements_eligible_after_terminal", "eligible_at >= trip_terminal_at");
                    table.CheckConstraint("chk_operator_trip_settlements_failure_consistency", "(active_failure_code IS NULL) OR (status = 'ELIGIBLE' AND settlement_failure_count > 0 AND last_settlement_failure_at IS NOT NULL)");
                    table.CheckConstraint("chk_operator_trip_settlements_settled_consistency", "(status IN ('PENDING_HOLD','ELIGIBLE') AND settled_at IS NULL AND settlement_method IS NULL AND wallet_transaction_id IS NULL) OR (status IN ('SETTLED','CANCELLED') AND settled_at IS NOT NULL AND settlement_method IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_operator_trip_settlements_operator_wallet_transactions_wall~",
                        column: x => x.wallet_transaction_id,
                        principalSchema: "vietride_payment",
                        principalTable: "operator_wallet_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "uq_platform_wallet_transactions_subscription",
                schema: "vietride_payment",
                table: "platform_wallet_transactions",
                columns: new[] { "type", "reference_type", "reference_id" },
                unique: true,
                filter: "reference_type = 'SUBSCRIPTION_PAYMENT'");

            migrationBuilder.CreateIndex(
                name: "idx_payments_context_reconciliation",
                schema: "vietride_payment",
                table: "payments",
                columns: new[] { "context_reconciliation_required", "status" },
                filter: "context_reconciliation_required = TRUE");

            migrationBuilder.CreateIndex(
                name: "idx_invoices_operator_id_created_at",
                schema: "vietride_payment",
                table: "invoices",
                columns: new[] { "operator_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "idx_invoices_pdf_retry",
                schema: "vietride_payment",
                table: "invoices",
                columns: new[] { "pdf_generation_status", "pdf_generation_next_retry_at" },
                filter: "pdf_generation_status IN ('PENDING', 'FAILED', 'PROCESSING')");

            migrationBuilder.CreateIndex(
                name: "idx_invoices_status",
                schema: "vietride_payment",
                table: "invoices",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "uq_invoices_invoice_number",
                schema: "vietride_payment",
                table: "invoices",
                column: "invoice_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_invoices_payment_id",
                schema: "vietride_payment",
                table: "invoices",
                column: "payment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_operator_ledger_entries_entry_type",
                schema: "vietride_payment",
                table: "operator_ledger_entries",
                columns: new[] { "operator_id", "entry_type" });

            migrationBuilder.CreateIndex(
                name: "idx_operator_ledger_entries_operator_id_created_at",
                schema: "vietride_payment",
                table: "operator_ledger_entries",
                columns: new[] { "operator_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "idx_operator_ledger_entries_operator_trip",
                schema: "vietride_payment",
                table: "operator_ledger_entries",
                columns: new[] { "operator_id", "trip_id" },
                filter: "trip_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_operator_ledger_entries_reference",
                schema: "vietride_payment",
                table: "operator_ledger_entries",
                columns: new[] { "reference_type", "reference_id" });

            migrationBuilder.CreateIndex(
                name: "uq_operator_ledger_entries_source",
                schema: "vietride_payment",
                table: "operator_ledger_entries",
                columns: new[] { "source_event_id", "entry_type", "reference_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_operator_trip_settlements_operator_status",
                schema: "vietride_payment",
                table: "operator_trip_settlements",
                columns: new[] { "operator_id", "status" });

            migrationBuilder.CreateIndex(
                name: "idx_operator_trip_settlements_settled_by_user_id",
                schema: "vietride_payment",
                table: "operator_trip_settlements",
                column: "settled_by_user_id",
                filter: "settled_by_user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_operator_trip_settlements_status_eligible",
                schema: "vietride_payment",
                table: "operator_trip_settlements",
                columns: new[] { "status", "eligible_at" },
                filter: "status IN ('PENDING_HOLD','ELIGIBLE')");

            migrationBuilder.CreateIndex(
                name: "idx_operator_trip_settlements_stuck",
                schema: "vietride_payment",
                table: "operator_trip_settlements",
                columns: new[] { "status", "active_failure_code", "last_settlement_failure_at" },
                filter: "status = 'ELIGIBLE' AND active_failure_code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_operator_trip_settlements_trip_id",
                schema: "vietride_payment",
                table: "operator_trip_settlements",
                column: "trip_id");

            migrationBuilder.CreateIndex(
                name: "idx_operator_trip_settlements_wallet_transaction_id",
                schema: "vietride_payment",
                table: "operator_trip_settlements",
                column: "wallet_transaction_id",
                filter: "wallet_transaction_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_operator_trip_settlements_operator_trip",
                schema: "vietride_payment",
                table: "operator_trip_settlements",
                columns: new[] { "operator_id", "trip_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_operator_wallet_transactions_operator_id_created_at",
                schema: "vietride_payment",
                table: "operator_wallet_transactions",
                columns: new[] { "operator_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "idx_operator_wallet_transactions_reference",
                schema: "vietride_payment",
                table: "operator_wallet_transactions",
                columns: new[] { "reference_type", "reference_id" },
                filter: "reference_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_operator_wallet_transactions_subscription",
                schema: "vietride_payment",
                table: "operator_wallet_transactions",
                columns: new[] { "operator_id", "type", "reference_type", "reference_id" },
                unique: true,
                filter: "reference_type = 'SUBSCRIPTION_PAYMENT'");

            migrationBuilder.CreateIndex(
                name: "uq_processed_integration_events_consumer_event",
                schema: "vietride_payment",
                table: "processed_integration_events",
                columns: new[] { "consumer", "event_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invoice_number_counters",
                schema: "vietride_payment");

            migrationBuilder.DropTable(
                name: "invoices",
                schema: "vietride_payment");

            migrationBuilder.DropTable(
                name: "operator_ledger_entries",
                schema: "vietride_payment");

            migrationBuilder.DropTable(
                name: "operator_trip_settlements",
                schema: "vietride_payment");

            migrationBuilder.DropTable(
                name: "operator_wallets",
                schema: "vietride_payment");

            migrationBuilder.DropTable(
                name: "processed_integration_events",
                schema: "vietride_payment");

            migrationBuilder.DropTable(
                name: "operator_wallet_transactions",
                schema: "vietride_payment");

            migrationBuilder.DropIndex(
                name: "uq_platform_wallet_transactions_subscription",
                schema: "vietride_payment",
                table: "platform_wallet_transactions");

            migrationBuilder.DropIndex(
                name: "idx_payments_context_reconciliation",
                schema: "vietride_payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "context",
                schema: "vietride_payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "context_reconciliation_required",
                schema: "vietride_payment",
                table: "payments");

            migrationBuilder.AlterDatabase()
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
        }
    }
}
