CREATE EXTENSION IF NOT EXISTS "pgcrypto";

CREATE SCHEMA IF NOT EXISTS "vietride_tracking";

CREATE TYPE "vietride_tracking"."outbox_event_status" AS ENUM (
    'PENDING',
    'PUBLISHING',
    'PUBLISHED',
    'FAILED'
);

CREATE TABLE "vietride_tracking"."gps_trails" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "trip_id" UUID NOT NULL,
    "latitude" DECIMAL(10,7) NOT NULL,
    "longitude" DECIMAL(10,7) NOT NULL,
    "speed_kmh" DECIMAL(6,2),
    "heading_deg" DECIMAL(5,2),
    "recorded_at" TIMESTAMPTZ(6) NOT NULL,
    "created_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "gps_trails_pkey" PRIMARY KEY ("id"),
    CONSTRAINT "chk_gps_trails_lat_range" CHECK ("latitude" BETWEEN -90 AND 90),
    CONSTRAINT "chk_gps_trails_lng_range" CHECK ("longitude" BETWEEN -180 AND 180),
    CONSTRAINT "chk_gps_trails_speed_non_negative" CHECK ("speed_kmh" IS NULL OR "speed_kmh" >= 0)
);

CREATE TABLE "vietride_tracking"."outbox_events" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "event_type" VARCHAR(100) NOT NULL,
    "payload" JSONB NOT NULL,
    "status" "vietride_tracking"."outbox_event_status" NOT NULL DEFAULT 'PENDING',
    "retry_count" INTEGER NOT NULL DEFAULT 0,
    "last_error" TEXT,
    "created_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "published_at" TIMESTAMPTZ(6),

    CONSTRAINT "outbox_events_pkey" PRIMARY KEY ("id")
);

CREATE INDEX "idx_gps_trails_trip_id_recorded_at"
    ON "vietride_tracking"."gps_trails"("trip_id", "recorded_at");

CREATE INDEX "idx_gps_trails_recorded_at"
    ON "vietride_tracking"."gps_trails"("recorded_at");

CREATE INDEX "idx_outbox_events_status_created"
    ON "vietride_tracking"."outbox_events"("status", "created_at");
