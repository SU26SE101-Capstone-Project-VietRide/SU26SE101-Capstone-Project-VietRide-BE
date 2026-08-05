ALTER TABLE "vietride_tracking"."outbox_events"
    ADD COLUMN "dedupe_key" VARCHAR(255);

CREATE UNIQUE INDEX "uq_outbox_events_dedupe_key"
    ON "vietride_tracking"."outbox_events"("dedupe_key")
    WHERE "dedupe_key" IS NOT NULL;
