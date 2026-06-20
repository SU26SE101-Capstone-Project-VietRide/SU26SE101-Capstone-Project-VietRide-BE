ALTER TABLE "vietride_tracking"."outbox_events"
ADD COLUMN "next_retry_at" TIMESTAMPTZ(6);

CREATE INDEX "idx_outbox_events_status_next_retry"
  ON "vietride_tracking"."outbox_events"("status", "next_retry_at");
