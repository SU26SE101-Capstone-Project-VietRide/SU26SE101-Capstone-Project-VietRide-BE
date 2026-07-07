using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Payment.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixFreshDbEnumValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TYPE vietride_payment.payment_reference_type ADD VALUE IF NOT EXISTS 'PARCEL_ADDITIONAL';
                ALTER TYPE vietride_payment.wallet_transaction_ref ADD VALUE IF NOT EXISTS 'PARCEL_ADDITIONAL_PAYMENT';
                ALTER TYPE vietride_payment.platform_wallet_transaction_ref ADD VALUE IF NOT EXISTS 'PARCEL_ADDITIONAL_PAYMENT_HOLD';

                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_type t
                        JOIN pg_namespace n ON n.oid = t.typnamespace
                        WHERE n.nspname = 'vietride_payment'
                          AND t.typname = 'outbox_event_status'
                    ) THEN
                        CREATE TYPE vietride_payment.outbox_event_status AS ENUM ('PENDING', 'PUBLISHING', 'PUBLISHED', 'FAILED');
                    END IF;
                END $$;

                DROP INDEX IF EXISTS vietride_payment.idx_outbox_events_status_created;

                ALTER TABLE vietride_payment.outbox_events
                    ALTER COLUMN status DROP DEFAULT;

                ALTER TABLE vietride_payment.outbox_events
                    ALTER COLUMN status TYPE vietride_payment.outbox_event_status
                        USING status::text::vietride_payment.outbox_event_status;

                ALTER TABLE vietride_payment.outbox_events
                    ALTER COLUMN status SET DEFAULT 'PENDING'::vietride_payment.outbox_event_status;

                CREATE INDEX IF NOT EXISTS idx_outbox_events_status_created
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
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_type t
                        JOIN pg_namespace n ON n.oid = t.typnamespace
                        WHERE n.nspname = 'public'
                          AND t.typname = 'outbox_event_status'
                    ) THEN
                        CREATE TYPE public.outbox_event_status AS ENUM ('PENDING', 'PUBLISHING', 'PUBLISHED', 'FAILED');
                    END IF;
                END $$;

                DROP INDEX IF EXISTS vietride_payment.idx_outbox_events_status_created;

                ALTER TABLE vietride_payment.outbox_events
                    ALTER COLUMN status DROP DEFAULT;

                ALTER TABLE vietride_payment.outbox_events
                    ALTER COLUMN status TYPE public.outbox_event_status
                        USING status::text::public.outbox_event_status;

                ALTER TABLE vietride_payment.outbox_events
                    ALTER COLUMN status SET DEFAULT 'PENDING'::public.outbox_event_status;

                CREATE INDEX IF NOT EXISTS idx_outbox_events_status_created
                    ON vietride_payment.outbox_events (status, created_at)
                    WHERE status IN (
                        'PENDING'::public.outbox_event_status,
                        'PUBLISHING'::public.outbox_event_status,
                        'FAILED'::public.outbox_event_status);
                """);
        }
    }
}
