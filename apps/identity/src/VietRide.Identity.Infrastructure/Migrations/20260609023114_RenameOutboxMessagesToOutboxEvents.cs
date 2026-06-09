using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameOutboxMessagesToOutboxEvents : Migration
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
                          AND n.nspname = current_schema()
                    ) THEN
                        CREATE TYPE outbox_event_status AS ENUM ('PENDING', 'PUBLISHING', 'PUBLISHED', 'FAILED');
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE IF EXISTS vietride_identity.outbox_messages
                    RENAME TO outbox_events;

                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM pg_constraint
                        WHERE conname = 'pk_outbox_messages'
                          AND conrelid = 'vietride_identity.outbox_events'::regclass
                    ) THEN
                        ALTER TABLE vietride_identity.outbox_events
                            RENAME CONSTRAINT pk_outbox_messages TO pk_outbox_events;
                    END IF;
                END $$;

                DROP INDEX IF EXISTS vietride_identity.ix_outbox_messages_processed_at_occurred_at;

                ALTER TABLE vietride_identity.outbox_events
                    ALTER COLUMN id SET DEFAULT gen_random_uuid();

                ALTER TABLE vietride_identity.outbox_events
                    ADD COLUMN IF NOT EXISTS event_type VARCHAR(100);
                UPDATE vietride_identity.outbox_events
                   SET event_type = LEFT(type, 100)
                 WHERE event_type IS NULL
                   AND EXISTS (
                       SELECT 1
                       FROM information_schema.columns
                       WHERE table_schema = 'vietride_identity'
                         AND table_name = 'outbox_events'
                         AND column_name = 'type'
                   );
                UPDATE vietride_identity.outbox_events
                   SET event_type = 'unknown.event'
                 WHERE event_type IS NULL OR event_type = '';
                ALTER TABLE vietride_identity.outbox_events
                    ALTER COLUMN event_type SET NOT NULL;

                ALTER TABLE vietride_identity.outbox_events
                    ADD COLUMN IF NOT EXISTS status outbox_event_status NOT NULL DEFAULT 'PENDING';

                ALTER TABLE vietride_identity.outbox_events
                    ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ;
                UPDATE vietride_identity.outbox_events
                   SET created_at = occurred_at
                 WHERE created_at IS NULL
                   AND EXISTS (
                       SELECT 1
                       FROM information_schema.columns
                       WHERE table_schema = 'vietride_identity'
                         AND table_name = 'outbox_events'
                         AND column_name = 'occurred_at'
                   );
                UPDATE vietride_identity.outbox_events
                   SET created_at = now()
                 WHERE created_at IS NULL;
                ALTER TABLE vietride_identity.outbox_events
                    ALTER COLUMN created_at SET DEFAULT now(),
                    ALTER COLUMN created_at SET NOT NULL;

                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'vietride_identity'
                          AND table_name = 'outbox_events'
                          AND column_name = 'processed_at'
                    ) AND NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'vietride_identity'
                          AND table_name = 'outbox_events'
                          AND column_name = 'published_at'
                    ) THEN
                        ALTER TABLE vietride_identity.outbox_events
                            RENAME COLUMN processed_at TO published_at;
                    END IF;
                END $$;

                ALTER TABLE vietride_identity.outbox_events
                    DROP COLUMN IF EXISTS occurred_at,
                    DROP COLUMN IF EXISTS type;

                CREATE INDEX IF NOT EXISTS idx_outbox_events_status_created
                    ON vietride_identity.outbox_events (status, created_at)
                    WHERE status IN ('PENDING', 'PUBLISHING', 'FAILED');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS vietride_identity.idx_outbox_events_status_created;

                ALTER TABLE IF EXISTS vietride_identity.outbox_events
                    RENAME TO outbox_messages;

                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM pg_constraint
                        WHERE conname = 'pk_outbox_events'
                          AND conrelid = 'vietride_identity.outbox_messages'::regclass
                    ) THEN
                        ALTER TABLE vietride_identity.outbox_messages
                            RENAME CONSTRAINT pk_outbox_events TO pk_outbox_messages;
                    END IF;
                END $$;

                ALTER TABLE vietride_identity.outbox_messages
                    ALTER COLUMN id DROP DEFAULT;

                ALTER TABLE vietride_identity.outbox_messages
                    ADD COLUMN IF NOT EXISTS type VARCHAR(200);
                UPDATE vietride_identity.outbox_messages
                   SET type = event_type
                 WHERE type IS NULL
                   AND EXISTS (
                       SELECT 1
                       FROM information_schema.columns
                       WHERE table_schema = 'vietride_identity'
                         AND table_name = 'outbox_messages'
                         AND column_name = 'event_type'
                   );
                UPDATE vietride_identity.outbox_messages
                   SET type = 'unknown.event'
                 WHERE type IS NULL OR type = '';
                ALTER TABLE vietride_identity.outbox_messages
                    ALTER COLUMN type SET NOT NULL;

                ALTER TABLE vietride_identity.outbox_messages
                    ADD COLUMN IF NOT EXISTS occurred_at TIMESTAMPTZ;
                UPDATE vietride_identity.outbox_messages
                   SET occurred_at = created_at
                 WHERE occurred_at IS NULL
                   AND EXISTS (
                       SELECT 1
                       FROM information_schema.columns
                       WHERE table_schema = 'vietride_identity'
                         AND table_name = 'outbox_messages'
                         AND column_name = 'created_at'
                   );
                UPDATE vietride_identity.outbox_messages
                   SET occurred_at = now()
                 WHERE occurred_at IS NULL;
                ALTER TABLE vietride_identity.outbox_messages
                    ALTER COLUMN occurred_at SET NOT NULL;

                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'vietride_identity'
                          AND table_name = 'outbox_messages'
                          AND column_name = 'published_at'
                    ) AND NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'vietride_identity'
                          AND table_name = 'outbox_messages'
                          AND column_name = 'processed_at'
                    ) THEN
                        ALTER TABLE vietride_identity.outbox_messages
                            RENAME COLUMN published_at TO processed_at;
                    END IF;
                END $$;

                ALTER TABLE vietride_identity.outbox_messages
                    DROP COLUMN IF EXISTS event_type,
                    DROP COLUMN IF EXISTS status,
                    DROP COLUMN IF EXISTS created_at;

                CREATE INDEX IF NOT EXISTS ix_outbox_messages_processed_at_occurred_at
                    ON vietride_identity.outbox_messages (processed_at, occurred_at);

                DROP TYPE IF EXISTS outbox_event_status;
                """);
        }
    }
}
