using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Payment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NormalizePaymentEnumSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE SCHEMA IF NOT EXISTS vietride_payment;

                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace WHERE n.nspname = 'vietride_payment' AND t.typname = 'outbox_event_status') THEN
                        CREATE TYPE vietride_payment.outbox_event_status AS ENUM ('PENDING', 'PUBLISHING', 'PUBLISHED', 'FAILED');
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace WHERE n.nspname = 'vietride_payment' AND t.typname = 'payment_method') THEN
                        CREATE TYPE vietride_payment.payment_method AS ENUM ('WALLET', 'VNPAY');
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace WHERE n.nspname = 'vietride_payment' AND t.typname = 'payment_reference_type') THEN
                        CREATE TYPE vietride_payment.payment_reference_type AS ENUM ('BOOKING', 'BOOKING_GROUP', 'PARCEL', 'TOP_UP', 'SUBSCRIPTION');
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace WHERE n.nspname = 'vietride_payment' AND t.typname = 'payment_status') THEN
                        CREATE TYPE vietride_payment.payment_status AS ENUM ('PENDING_REDIRECT', 'SUCCEEDED', 'FAILED', 'EXPIRED', 'REFUNDED');
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace WHERE n.nspname = 'vietride_payment' AND t.typname = 'top_up_request_status') THEN
                        CREATE TYPE vietride_payment.top_up_request_status AS ENUM ('PENDING', 'SUCCEEDED', 'FAILED', 'EXPIRED');
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace WHERE n.nspname = 'vietride_payment' AND t.typname = 'wallet_transaction_type') THEN
                        CREATE TYPE vietride_payment.wallet_transaction_type AS ENUM ('CREDIT', 'DEBIT');
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace WHERE n.nspname = 'vietride_payment' AND t.typname = 'wallet_transaction_ref') THEN
                        CREATE TYPE vietride_payment.wallet_transaction_ref AS ENUM ('TOP_UP', 'BOOKING_PAYMENT', 'BOOKING_REFUND', 'PARCEL_PAYMENT', 'PARCEL_REFUND', 'MANUAL_ADJUSTMENT');
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace WHERE n.nspname = 'vietride_payment' AND t.typname = 'platform_wallet_transaction_type') THEN
                        CREATE TYPE vietride_payment.platform_wallet_transaction_type AS ENUM ('CREDIT', 'DEBIT');
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace WHERE n.nspname = 'vietride_payment' AND t.typname = 'platform_wallet_transaction_ref') THEN
                        CREATE TYPE vietride_payment.platform_wallet_transaction_ref AS ENUM ('BOOKING_PAYMENT_HOLD', 'PARCEL_PAYMENT_HOLD', 'BOOKING_REFUND', 'PARCEL_REFUND', 'TRIP_SETTLEMENT', 'SUBSCRIPTION_PAYMENT', 'MANUAL_ADJUSTMENT');
                    END IF;
                END $$;

                ALTER TYPE vietride_payment.payment_reference_type ADD VALUE IF NOT EXISTS 'PARCEL_ADDITIONAL';
                ALTER TYPE vietride_payment.wallet_transaction_ref ADD VALUE IF NOT EXISTS 'PARCEL_ADDITIONAL_PAYMENT';
                ALTER TYPE vietride_payment.platform_wallet_transaction_ref ADD VALUE IF NOT EXISTS 'PARCEL_ADDITIONAL_PAYMENT_HOLD';

                DROP INDEX IF EXISTS vietride_payment.idx_top_up_requests_status_created_at;
                DROP INDEX IF EXISTS vietride_payment.idx_payments_status_created_at;
                DROP INDEX IF EXISTS vietride_payment.idx_outbox_events_status_created;

                ALTER TABLE vietride_payment.top_up_requests
                    ALTER COLUMN status DROP DEFAULT,
                    ALTER COLUMN status TYPE vietride_payment.top_up_request_status
                        USING status::text::vietride_payment.top_up_request_status,
                    ALTER COLUMN status SET DEFAULT 'PENDING'::vietride_payment.top_up_request_status;

                ALTER TABLE vietride_payment.payments
                    ALTER COLUMN reference_type TYPE vietride_payment.payment_reference_type
                        USING reference_type::text::vietride_payment.payment_reference_type,
                    ALTER COLUMN method TYPE vietride_payment.payment_method
                        USING method::text::vietride_payment.payment_method,
                    ALTER COLUMN status TYPE vietride_payment.payment_status
                        USING status::text::vietride_payment.payment_status;

                ALTER TABLE vietride_payment.wallet_transactions
                    ALTER COLUMN type TYPE vietride_payment.wallet_transaction_type
                        USING type::text::vietride_payment.wallet_transaction_type,
                    ALTER COLUMN reference_type TYPE vietride_payment.wallet_transaction_ref
                        USING reference_type::text::vietride_payment.wallet_transaction_ref;

                ALTER TABLE vietride_payment.platform_wallet_transactions
                    ALTER COLUMN type TYPE vietride_payment.platform_wallet_transaction_type
                        USING type::text::vietride_payment.platform_wallet_transaction_type,
                    ALTER COLUMN reference_type TYPE vietride_payment.platform_wallet_transaction_ref
                        USING reference_type::text::vietride_payment.platform_wallet_transaction_ref;

                ALTER TABLE vietride_payment.outbox_events
                    ALTER COLUMN status DROP DEFAULT,
                    ALTER COLUMN status TYPE vietride_payment.outbox_event_status
                        USING status::text::vietride_payment.outbox_event_status,
                    ALTER COLUMN status SET DEFAULT 'PENDING'::vietride_payment.outbox_event_status;

                CREATE INDEX idx_top_up_requests_status_created_at
                    ON vietride_payment.top_up_requests (status, created_at)
                    WHERE status = 'PENDING'::vietride_payment.top_up_request_status;

                CREATE INDEX idx_payments_status_created_at
                    ON vietride_payment.payments (status, created_at)
                    WHERE status IN ('PENDING_REDIRECT'::vietride_payment.payment_status);

                CREATE INDEX idx_outbox_events_status_created
                    ON vietride_payment.outbox_events (status, created_at)
                    WHERE status IN (
                        'PENDING'::vietride_payment.outbox_event_status,
                        'PUBLISHING'::vietride_payment.outbox_event_status,
                        'FAILED'::vietride_payment.outbox_event_status);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS vietride_payment.idx_top_up_requests_status_created_at;
                DROP INDEX IF EXISTS vietride_payment.idx_payments_status_created_at;
                DROP INDEX IF EXISTS vietride_payment.idx_outbox_events_status_created;

                CREATE INDEX idx_top_up_requests_status_created_at
                    ON vietride_payment.top_up_requests (status, created_at)
                    WHERE status = 'PENDING'::vietride_payment.top_up_request_status;

                CREATE INDEX idx_payments_status_created_at
                    ON vietride_payment.payments (status, created_at)
                    WHERE status IN ('PENDING_REDIRECT'::vietride_payment.payment_status);

                CREATE INDEX idx_outbox_events_status_created
                    ON vietride_payment.outbox_events (status, created_at)
                    WHERE status IN (
                        'PENDING'::vietride_payment.outbox_event_status,
                        'PUBLISHING'::vietride_payment.outbox_event_status,
                        'FAILED'::vietride_payment.outbox_event_status);
                """);
        }
    }
}
