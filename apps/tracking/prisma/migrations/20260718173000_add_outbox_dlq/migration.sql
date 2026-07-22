CREATE TABLE "vietride_tracking"."outbox_dlq" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "event_id" UUID NOT NULL,
    "event_type" VARCHAR(100) NOT NULL,
    "payload" JSONB NOT NULL,
    "retry_count" INTEGER NOT NULL,
    "last_error" TEXT NOT NULL,
    "created_at" TIMESTAMPTZ(6) NOT NULL,
    "terminal_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "outbox_dlq_pkey" PRIMARY KEY ("id")
);

CREATE UNIQUE INDEX "uq_outbox_dlq_event_id"
    ON "vietride_tracking"."outbox_dlq"("event_id");

CREATE INDEX "idx_outbox_dlq_terminal_event_id"
    ON "vietride_tracking"."outbox_dlq"("terminal_at", "event_id");
