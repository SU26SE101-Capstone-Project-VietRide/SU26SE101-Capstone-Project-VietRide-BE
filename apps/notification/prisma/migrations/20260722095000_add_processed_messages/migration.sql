CREATE TABLE "vietride_notification"."processed_messages" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "consumer_name" VARCHAR(200) NOT NULL,
    "message_id" VARCHAR(100) NOT NULL,
    "routing_key" VARCHAR(200) NOT NULL,
    "payload_hash" CHAR(64) NOT NULL,
    "processed_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT "processed_messages_pkey" PRIMARY KEY ("id")
);

CREATE UNIQUE INDEX "uq_processed_messages_consumer_message"
    ON "vietride_notification"."processed_messages"("consumer_name", "message_id");
CREATE INDEX "idx_processed_messages_processed_at"
    ON "vietride_notification"."processed_messages"("processed_at");
