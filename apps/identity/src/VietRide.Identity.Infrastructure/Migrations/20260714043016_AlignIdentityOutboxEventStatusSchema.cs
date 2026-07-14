using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AlignIdentityOutboxEventStatusSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_type t
                        JOIN pg_namespace n ON n.oid = t.typnamespace
                        WHERE t.typname = 'outbox_event_status'
                          AND n.nspname = 'vietride_identity') THEN
                        CREATE TYPE vietride_identity.outbox_event_status
                            AS ENUM ('PENDING', 'PUBLISHING', 'PUBLISHED', 'FAILED');
                    END IF;
                END $$;

                DROP INDEX IF EXISTS vietride_identity.idx_outbox_events_status_created;
                ALTER TABLE vietride_identity.outbox_events
                    ALTER COLUMN status DROP DEFAULT,
                    ALTER COLUMN status TYPE vietride_identity.outbox_event_status
                        USING status::text::vietride_identity.outbox_event_status,
                    ALTER COLUMN status SET DEFAULT 'PENDING'::vietride_identity.outbox_event_status;
                CREATE INDEX idx_outbox_events_status_created
                    ON vietride_identity.outbox_events (status, created_at)
                    WHERE status IN (
                        'PENDING'::vietride_identity.outbox_event_status,
                        'PUBLISHING'::vietride_identity.outbox_event_status,
                        'FAILED'::vietride_identity.outbox_event_status);
                DROP TYPE IF EXISTS public.outbox_event_status;
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
                        WHERE t.typname = 'outbox_event_status'
                          AND n.nspname = 'public') THEN
                        CREATE TYPE public.outbox_event_status
                            AS ENUM ('PENDING', 'PUBLISHING', 'PUBLISHED', 'FAILED');
                    END IF;
                END $$;

                DROP INDEX IF EXISTS vietride_identity.idx_outbox_events_status_created;
                ALTER TABLE vietride_identity.outbox_events
                    ALTER COLUMN status DROP DEFAULT,
                    ALTER COLUMN status TYPE public.outbox_event_status
                        USING status::text::public.outbox_event_status,
                    ALTER COLUMN status SET DEFAULT 'PENDING'::public.outbox_event_status;
                CREATE INDEX idx_outbox_events_status_created
                    ON vietride_identity.outbox_events (status, created_at)
                    WHERE status IN (
                        'PENDING'::public.outbox_event_status,
                        'PUBLISHING'::public.outbox_event_status,
                        'FAILED'::public.outbox_event_status);
                DROP TYPE IF EXISTS vietride_identity.outbox_event_status;
                """);
        }
    }
}
