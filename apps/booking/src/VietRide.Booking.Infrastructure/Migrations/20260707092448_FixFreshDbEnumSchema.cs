using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Booking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixFreshDbEnumSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE SCHEMA IF NOT EXISTS vietride_booking;

                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_type t
                        JOIN pg_namespace n ON n.oid = t.typnamespace
                        WHERE n.nspname = 'vietride_booking'
                          AND t.typname = 'outbox_event_status'
                    ) THEN
                        CREATE TYPE vietride_booking.outbox_event_status AS ENUM ('PENDING', 'PUBLISHING', 'PUBLISHED', 'FAILED');
                    END IF;
                END $$;

                DROP INDEX IF EXISTS vietride_booking.idx_outbox_events_status_created;

                ALTER TABLE vietride_booking.outbox_events
                    ALTER COLUMN status DROP DEFAULT;

                ALTER TABLE vietride_booking.outbox_events
                    ALTER COLUMN status TYPE vietride_booking.outbox_event_status
                        USING status::text::vietride_booking.outbox_event_status;

                ALTER TABLE vietride_booking.outbox_events
                    ALTER COLUMN status SET DEFAULT 'PENDING'::vietride_booking.outbox_event_status;

                CREATE INDEX IF NOT EXISTS idx_outbox_events_status_created
                    ON vietride_booking.outbox_events (status, created_at)
                    WHERE status IN (
                        'PENDING'::vietride_booking.outbox_event_status,
                        'PUBLISHING'::vietride_booking.outbox_event_status,
                        'FAILED'::vietride_booking.outbox_event_status);
                """);

            migrationBuilder.AlterColumn<int>(
                name: "status",
                schema: "vietride_booking",
                table: "tickets",
                type: "public.ticket_status",
                nullable: false,
                defaultValueSql: "'PENDING_PAYMENT'::public.ticket_status",
                oldClrType: typeof(int),
                oldType: "public.ticket_status",
                oldDefaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "status",
                schema: "vietride_booking",
                table: "tickets",
                type: "public.ticket_status",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "public.ticket_status",
                oldDefaultValueSql: "'PENDING_PAYMENT'::public.ticket_status");

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

                DROP INDEX IF EXISTS vietride_booking.idx_outbox_events_status_created;

                ALTER TABLE vietride_booking.outbox_events
                    ALTER COLUMN status DROP DEFAULT;

                ALTER TABLE vietride_booking.outbox_events
                    ALTER COLUMN status TYPE public.outbox_event_status
                        USING status::text::public.outbox_event_status;

                ALTER TABLE vietride_booking.outbox_events
                    ALTER COLUMN status SET DEFAULT 'PENDING'::public.outbox_event_status;

                CREATE INDEX IF NOT EXISTS idx_outbox_events_status_created
                    ON vietride_booking.outbox_events (status, created_at)
                    WHERE status IN (
                        'PENDING'::public.outbox_event_status,
                        'PUBLISHING'::public.outbox_event_status,
                        'FAILED'::public.outbox_event_status);
                """);
        }
    }
}
