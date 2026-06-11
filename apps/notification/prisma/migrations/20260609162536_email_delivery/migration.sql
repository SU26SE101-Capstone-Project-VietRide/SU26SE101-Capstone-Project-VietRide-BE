-- DropForeignKey
ALTER TABLE "vietride_notification"."notification_deliveries"
    DROP CONSTRAINT IF EXISTS "notification_deliveries_notification_id_fkey";

-- Drop existing partial index from the pre-migration canonical SQL baseline.
DROP INDEX IF EXISTS "vietride_notification"."idx_notification_deliveries_status_created_at";

-- CreateIndex
CREATE INDEX "idx_notification_deliveries_status_created_at" ON "vietride_notification"."notification_deliveries"("status", "created_at");

-- AddForeignKey
ALTER TABLE "vietride_notification"."notification_deliveries" ADD CONSTRAINT "notification_deliveries_notification_id_fkey" FOREIGN KEY ("notification_id") REFERENCES "vietride_notification"."notifications"("id") ON DELETE CASCADE ON UPDATE CASCADE;
