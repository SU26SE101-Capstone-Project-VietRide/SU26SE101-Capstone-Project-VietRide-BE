using System;
using Microsoft.EntityFrameworkCore.Migrations;
using VietRide.Payment.Domain.Enums;

#nullable disable

namespace VietRide.Payment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitPaymentSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "vietride_payment");

            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS \"pgcrypto\";");
            migrationBuilder.Sql("SET search_path TO vietride_payment, public;");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:outbox_event_status", "PENDING,PUBLISHING,PUBLISHED,FAILED")
                .Annotation("Npgsql:Enum:payment_method", "WALLET,VNPAY")
                .Annotation("Npgsql:Enum:payment_reference_type", "BOOKING,BOOKING_GROUP,PARCEL,TOP_UP,SUBSCRIPTION")
                .Annotation("Npgsql:Enum:payment_status", "PENDING_REDIRECT,SUCCEEDED,FAILED,EXPIRED,REFUNDED")
                .Annotation("Npgsql:Enum:platform_wallet_transaction_ref", "BOOKING_PAYMENT_HOLD,PARCEL_PAYMENT_HOLD,BOOKING_REFUND,PARCEL_REFUND,TRIP_SETTLEMENT,SUBSCRIPTION_PAYMENT,MANUAL_ADJUSTMENT")
                .Annotation("Npgsql:Enum:platform_wallet_transaction_type", "CREDIT,DEBIT")
                .Annotation("Npgsql:Enum:top_up_request_status", "PENDING,SUCCEEDED,FAILED,EXPIRED")
                .Annotation("Npgsql:Enum:wallet_transaction_ref", "TOP_UP,BOOKING_PAYMENT,BOOKING_REFUND,PARCEL_PAYMENT,PARCEL_REFUND,MANUAL_ADJUSTMENT")
                .Annotation("Npgsql:Enum:wallet_transaction_type", "CREDIT,DEBIT");

            migrationBuilder.CreateTable(
                name: "outbox_events",
                schema: "vietride_payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "outbox_event_status", nullable: false, defaultValueSql: "'PENDING'"),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "vietride_payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    reference_type = table.Column<PaymentReferenceType>(type: "payment_reference_type", nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    method = table.Column<PaymentMethod>(type: "payment_method", nullable: false),
                    status = table.Column<PaymentStatus>(type: "payment_status", nullable: false),
                    vnpay_txn_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    vnpay_response_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    payment_redirect_url = table.Column<string>(type: "text", nullable: true),
                    succeeded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    refunded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.CheckConstraint("chk_payments_amount_non_negative", "amount >= 0");
                });

            migrationBuilder.CreateTable(
                name: "platform_wallet_transactions",
                schema: "vietride_payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    type = table.Column<PlatformWalletTransactionType>(type: "platform_wallet_transaction_type", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    balance_before = table.Column<long>(type: "bigint", nullable: false),
                    balance_after = table.Column<long>(type: "bigint", nullable: false),
                    reference_type = table.Column<PlatformWalletTransactionRef>(type: "platform_wallet_transaction_ref", nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_wallet_transactions", x => x.id);
                    table.CheckConstraint("chk_platform_wallet_transactions_amount_positive", "amount > 0");
                    table.CheckConstraint("chk_platform_wallet_transactions_balance_non_negative", "balance_before >= 0 AND balance_after >= 0");
                });

            migrationBuilder.CreateTable(
                name: "platform_wallets",
                schema: "vietride_payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    balance = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "0"),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "VND"),
                    row_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_wallets", x => x.id);
                    table.CheckConstraint("chk_platform_wallets_balance_non_negative", "balance >= 0");
                });

            migrationBuilder.CreateTable(
                name: "top_up_requests",
                schema: "vietride_payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<TopUpRequestStatus>(type: "top_up_request_status", nullable: false, defaultValueSql: "'PENDING'"),
                    vnpay_txn_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    vnpay_response_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    payment_redirect_url = table.Column<string>(type: "text", nullable: true),
                    succeeded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_top_up_requests", x => x.id);
                    table.CheckConstraint("chk_top_up_requests_amount_min", "amount >= 10000");
                });

            migrationBuilder.CreateTable(
                name: "wallet_transactions",
                schema: "vietride_payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<WalletTransactionType>(type: "wallet_transaction_type", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    balance_before = table.Column<long>(type: "bigint", nullable: false),
                    balance_after = table.Column<long>(type: "bigint", nullable: false),
                    reference_type = table.Column<WalletTransactionRef>(type: "wallet_transaction_ref", nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallet_transactions", x => x.id);
                    table.CheckConstraint("chk_wallet_transactions_amount_positive", "amount > 0");
                    table.CheckConstraint("chk_wallet_transactions_balance_non_negative", "balance_before >= 0 AND balance_after >= 0");
                });

            migrationBuilder.CreateTable(
                name: "wallets",
                schema: "vietride_payment",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    balance = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "0"),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "VND"),
                    row_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallets", x => x.user_id);
                    table.CheckConstraint("chk_wallets_balance_non_negative", "balance >= 0");
                });

            migrationBuilder.Sql("CREATE UNIQUE INDEX uq_platform_wallets_singleton ON vietride_payment.platform_wallets ((TRUE));");

            migrationBuilder.CreateIndex(
                name: "idx_outbox_events_status_created",
                schema: "vietride_payment",
                table: "outbox_events",
                columns: new[] { "status", "created_at" },
                filter: "status IN ('PENDING', 'PUBLISHING', 'FAILED')");

            migrationBuilder.CreateIndex(
                name: "idx_payments_operator_id_created_at",
                schema: "vietride_payment",
                table: "payments",
                columns: new[] { "operator_id", "created_at" },
                descending: new[] { false, true },
                filter: "operator_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_payments_reference",
                schema: "vietride_payment",
                table: "payments",
                columns: new[] { "reference_type", "reference_id" });

            migrationBuilder.CreateIndex(
                name: "idx_payments_status_created_at",
                schema: "vietride_payment",
                table: "payments",
                columns: new[] { "status", "created_at" },
                filter: "status IN ('PENDING_REDIRECT')");

            migrationBuilder.CreateIndex(
                name: "idx_payments_user_id_created_at",
                schema: "vietride_payment",
                table: "payments",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true },
                filter: "user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_payments_idempotency_key",
                schema: "vietride_payment",
                table: "payments",
                column: "idempotency_key",
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_payments_vnpay_txn_ref",
                schema: "vietride_payment",
                table: "payments",
                column: "vnpay_txn_ref",
                unique: true,
                filter: "vnpay_txn_ref IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_platform_wallet_transactions_created_at",
                schema: "vietride_payment",
                table: "platform_wallet_transactions",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "idx_platform_wallet_transactions_reference",
                schema: "vietride_payment",
                table: "platform_wallet_transactions",
                columns: new[] { "reference_type", "reference_id" },
                filter: "reference_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_top_up_requests_status_created_at",
                schema: "vietride_payment",
                table: "top_up_requests",
                columns: new[] { "status", "created_at" },
                filter: "status = 'PENDING'");

            migrationBuilder.CreateIndex(
                name: "idx_top_up_requests_user_id_created_at",
                schema: "vietride_payment",
                table: "top_up_requests",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "uq_top_up_requests_vnpay_txn_ref",
                schema: "vietride_payment",
                table: "top_up_requests",
                column: "vnpay_txn_ref",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_wallet_transactions_reference",
                schema: "vietride_payment",
                table: "wallet_transactions",
                columns: new[] { "reference_type", "reference_id" },
                filter: "reference_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_wallet_transactions_user_id_created_at",
                schema: "vietride_payment",
                table: "wallet_transactions",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_events",
                schema: "vietride_payment");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "vietride_payment");

            migrationBuilder.DropTable(
                name: "platform_wallet_transactions",
                schema: "vietride_payment");

            migrationBuilder.DropTable(
                name: "platform_wallets",
                schema: "vietride_payment");

            migrationBuilder.DropTable(
                name: "top_up_requests",
                schema: "vietride_payment");

            migrationBuilder.DropTable(
                name: "wallet_transactions",
                schema: "vietride_payment");

            migrationBuilder.DropTable(
                name: "wallets",
                schema: "vietride_payment");

            migrationBuilder.Sql("DROP TYPE IF EXISTS vietride_payment.platform_wallet_transaction_ref;");
            migrationBuilder.Sql("DROP TYPE IF EXISTS vietride_payment.platform_wallet_transaction_type;");
            migrationBuilder.Sql("DROP TYPE IF EXISTS vietride_payment.wallet_transaction_ref;");
            migrationBuilder.Sql("DROP TYPE IF EXISTS vietride_payment.wallet_transaction_type;");
            migrationBuilder.Sql("DROP TYPE IF EXISTS vietride_payment.top_up_request_status;");
            migrationBuilder.Sql("DROP TYPE IF EXISTS vietride_payment.payment_status;");
            migrationBuilder.Sql("DROP TYPE IF EXISTS vietride_payment.payment_method;");
            migrationBuilder.Sql("DROP TYPE IF EXISTS vietride_payment.payment_reference_type;");
            migrationBuilder.Sql("DROP TYPE IF EXISTS vietride_payment.outbox_event_status;");
            migrationBuilder.Sql("DROP EXTENSION IF EXISTS \"pgcrypto\";");
        }
    }
}
