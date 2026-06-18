ALTER TABLE "vietride_notification"."notifications"
    ADD COLUMN "dedupe_key" VARCHAR(200);

CREATE UNIQUE INDEX "notifications_dedupe_key_key"
    ON "vietride_notification"."notifications"("dedupe_key");
