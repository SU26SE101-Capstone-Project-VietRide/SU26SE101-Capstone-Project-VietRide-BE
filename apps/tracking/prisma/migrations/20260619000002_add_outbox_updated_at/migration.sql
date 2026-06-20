ALTER TABLE "vietride_tracking"."outbox_events"
ADD COLUMN "updated_at" TIMESTAMPTZ(6) NOT NULL DEFAULT NOW();

CREATE INDEX "idx_outbox_events_status_updated"
  ON "vietride_tracking"."outbox_events"("status", "updated_at");
