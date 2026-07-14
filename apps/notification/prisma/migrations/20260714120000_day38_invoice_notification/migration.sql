ALTER TYPE "vietride_notification"."notification_type"
    ADD VALUE IF NOT EXISTS 'INVOICE_ISSUED';

ALTER TYPE "vietride_notification"."email_delivery_status"
    ADD VALUE IF NOT EXISTS 'SENDING';

ALTER TABLE "vietride_notification"."email_deliveries"
    ADD COLUMN "dedupe_key" VARCHAR(200);

CREATE UNIQUE INDEX "email_deliveries_dedupe_key_key"
    ON "vietride_notification"."email_deliveries"("dedupe_key");
