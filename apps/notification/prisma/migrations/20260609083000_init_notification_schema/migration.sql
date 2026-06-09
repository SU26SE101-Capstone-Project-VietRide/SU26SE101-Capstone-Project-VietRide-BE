CREATE EXTENSION IF NOT EXISTS "pgcrypto";

CREATE SCHEMA IF NOT EXISTS "vietride_notification";

CREATE TYPE "vietride_notification"."notification_type" AS ENUM (
    'BOOKING_CONFIRMED',
    'BOOKING_CANCELLED',
    'BOOKING_DISRUPTED',
    'BOOKING_REFUNDED',
    'PASSENGER_NO_SHOW',
    'TRIP_BOARDING_REMINDER',
    'TRIP_VEHICLE_APPROACHING',
    'TRIP_ROUTE_CHANGED',
    'TRIP_SCHEDULE_CHANGED',
    'TRIP_CANCELLED',
    'TRIP_DELAYED',
    'TRIP_DISRUPTED',
    'VEHICLE_SUBSTITUTED',
    'VEHICLE_SWAPPED',
    'PARCEL_LOADED',
    'PARCEL_IN_TRANSIT',
    'PARCEL_DELIVERED_PENDING_CONFIRM',
    'PARCEL_REJECTED',
    'PARCEL_RETURNED',
    'WALLET_CREDITED',
    'WALLET_DEBITED',
    'INCIDENT_REPORTED',
    'OFF_ROUTE_ALERT',
    'TRIP_DELAYED_ALERT',
    'CARGO_NEAR_FULL_ALERT',
    'PARCEL_REVIEW_REQUESTED',
    'VOUCHER_CONSENT_REQUESTED',
    'SUBSCRIPTION_LIMIT_EXCEEDED',
    'SUBSCRIPTION_TRIAL_EXPIRING',
    'SUBSCRIPTION_EXPIRED',
    'SUBSCRIPTION_APPROVED',
    'DRIVER_SCHEDULE_EDITED',
    'PAYOUT_PROCESSED',
    'PAYOUT_FAILED'
);

CREATE TYPE "vietride_notification"."notification_delivery_status" AS ENUM (
    'PENDING',
    'SENT',
    'FAILED',
    'RETRYING'
);

CREATE TYPE "vietride_notification"."device_platform" AS ENUM (
    'IOS',
    'ANDROID',
    'WEB'
);

CREATE TABLE "vietride_notification"."notifications" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "user_id" UUID NOT NULL,
    "type" "vietride_notification"."notification_type" NOT NULL,
    "title" VARCHAR(255) NOT NULL,
    "body" TEXT NOT NULL,
    "data" JSONB,
    "read_at" TIMESTAMPTZ(6),
    "created_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "notifications_pkey" PRIMARY KEY ("id")
);

CREATE TABLE "vietride_notification"."notification_deliveries" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "notification_id" UUID NOT NULL,
    "fcm_token" VARCHAR(500) NOT NULL,
    "platform" "vietride_notification"."device_platform" NOT NULL,
    "status" "vietride_notification"."notification_delivery_status" NOT NULL DEFAULT 'PENDING',
    "retry_count" INTEGER NOT NULL DEFAULT 0,
    "last_error" TEXT,
    "sent_at" TIMESTAMPTZ(6),
    "created_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "notification_deliveries_pkey" PRIMARY KEY ("id")
);

CREATE INDEX "idx_notifications_user_id_created_at"
    ON "vietride_notification"."notifications"("user_id", "created_at" DESC);

CREATE INDEX "idx_notifications_type_created_at"
    ON "vietride_notification"."notifications"("type", "created_at" DESC);

CREATE INDEX "idx_notifications_user_id_unread"
    ON "vietride_notification"."notifications"("user_id", "created_at" DESC)
    WHERE "read_at" IS NULL;

CREATE INDEX "idx_notification_deliveries_notification_id"
    ON "vietride_notification"."notification_deliveries"("notification_id");

CREATE INDEX "idx_notification_deliveries_status_created_at"
    ON "vietride_notification"."notification_deliveries"("status", "created_at")
    WHERE "status" IN ('PENDING', 'RETRYING', 'FAILED');

ALTER TABLE "vietride_notification"."notification_deliveries"
    ADD CONSTRAINT "notification_deliveries_notification_id_fkey"
    FOREIGN KEY ("notification_id")
    REFERENCES "vietride_notification"."notifications"("id")
    ON DELETE CASCADE;

CREATE OR REPLACE FUNCTION "vietride_notification"."trg_set_updated_at"()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = now();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER "trg_notification_deliveries_updated_at"
    BEFORE UPDATE ON "vietride_notification"."notification_deliveries"
    FOR EACH ROW EXECUTE FUNCTION "vietride_notification"."trg_set_updated_at"();
