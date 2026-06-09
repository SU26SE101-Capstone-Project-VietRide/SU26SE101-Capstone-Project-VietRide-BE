CREATE TYPE "vietride_notification"."email_template_key" AS ENUM (
    'AUTH_OTP',
    'SET_INITIAL_PASSWORD',
    'PARCEL_DELIVERY_LINK',
    'OPERATOR_SUBSCRIPTION_NOTICE',
    'INVOICE_NOTICE'
);

CREATE TYPE "vietride_notification"."email_delivery_status" AS ENUM (
    'PENDING',
    'SENT',
    'FAILED',
    'RETRYING'
);

CREATE TABLE "vietride_notification"."email_deliveries" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "notification_id" UUID,
    "to_email" VARCHAR(320) NOT NULL,
    "template_key" "vietride_notification"."email_template_key" NOT NULL,
    "subject" VARCHAR(255) NOT NULL,
    "sanitized_data" JSONB,
    "status" "vietride_notification"."email_delivery_status" NOT NULL DEFAULT 'PENDING',
    "retry_count" INTEGER NOT NULL DEFAULT 0,
    "last_error" TEXT,
    "provider_message_id" VARCHAR(255),
    "sent_at" TIMESTAMPTZ(6),
    "created_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "email_deliveries_pkey" PRIMARY KEY ("id")
);

CREATE INDEX "idx_email_deliveries_notification_id"
    ON "vietride_notification"."email_deliveries"("notification_id");

CREATE INDEX "idx_email_deliveries_status_created_at"
    ON "vietride_notification"."email_deliveries"("status", "created_at");

CREATE INDEX "idx_email_deliveries_to_email_created_at"
    ON "vietride_notification"."email_deliveries"("to_email", "created_at" DESC);

ALTER TABLE "vietride_notification"."email_deliveries"
    ADD CONSTRAINT "email_deliveries_notification_id_fkey"
    FOREIGN KEY ("notification_id")
    REFERENCES "vietride_notification"."notifications"("id")
    ON DELETE SET NULL
    ON UPDATE CASCADE;

CREATE TRIGGER "trg_email_deliveries_updated_at"
    BEFORE UPDATE ON "vietride_notification"."email_deliveries"
    FOR EACH ROW EXECUTE FUNCTION "vietride_notification"."trg_set_updated_at"();
