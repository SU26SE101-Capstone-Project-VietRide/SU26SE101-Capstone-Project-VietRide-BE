using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixFreshDbOutboxEnumSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE SCHEMA IF NOT EXISTS vietride_trip;

                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_type t
                        JOIN pg_namespace n ON n.oid = t.typnamespace
                        WHERE n.nspname = 'vietride_trip'
                          AND t.typname = 'outbox_event_status'
                    ) THEN
                        CREATE TYPE vietride_trip.outbox_event_status AS ENUM ('PENDING', 'PUBLISHING', 'PUBLISHED', 'FAILED');
                    END IF;
                END $$;

                DROP INDEX IF EXISTS vietride_trip.idx_outbox_events_status_created;

                ALTER TABLE vietride_trip.outbox_events
                    ALTER COLUMN status DROP DEFAULT;

                ALTER TABLE vietride_trip.outbox_events
                    ALTER COLUMN status TYPE vietride_trip.outbox_event_status
                        USING status::text::vietride_trip.outbox_event_status;

                ALTER TABLE vietride_trip.outbox_events
                    ALTER COLUMN status SET DEFAULT 'PENDING'::vietride_trip.outbox_event_status;

                CREATE INDEX IF NOT EXISTS idx_outbox_events_status_created
                    ON vietride_trip.outbox_events (status, created_at)
                    WHERE status IN (
                        'PENDING'::vietride_trip.outbox_event_status,
                        'PUBLISHING'::vietride_trip.outbox_event_status,
                        'FAILED'::vietride_trip.outbox_event_status);
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

                DROP INDEX IF EXISTS vietride_trip.idx_outbox_events_status_created;

                ALTER TABLE vietride_trip.outbox_events
                    ALTER COLUMN status DROP DEFAULT;

                ALTER TABLE vietride_trip.outbox_events
                    ALTER COLUMN status TYPE public.outbox_event_status
                        USING status::text::public.outbox_event_status;

                ALTER TABLE vietride_trip.outbox_events
                    ALTER COLUMN status SET DEFAULT 'PENDING'::public.outbox_event_status;

                CREATE INDEX IF NOT EXISTS idx_outbox_events_status_created
                    ON vietride_trip.outbox_events (status, created_at)
                    WHERE status IN (
                        'PENDING'::public.outbox_event_status,
                        'PUBLISHING'::public.outbox_event_status,
                        'FAILED'::public.outbox_event_status);
                """);
        }
    }
}
