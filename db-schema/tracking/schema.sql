-- =============================================================================
-- VietRide :: Tracking Service :: PostgreSQL 16 schema
-- Database: vietride_tracking
-- Schema: vietride_tracking
-- Framework: NestJS + Prisma ORM
-- =============================================================================
-- Minimal DB — most state lives in Redis (last position, GPS buffer, ETA cache).
-- This DB only persists batched GpsTrail history and OutboxEvent.
-- =============================================================================

CREATE EXTENSION IF NOT EXISTS "pgcrypto";

CREATE SCHEMA IF NOT EXISTS vietride_tracking;
SET search_path TO vietride_tracking, public;

-- =============================================================================
-- ENUMS
-- =============================================================================

CREATE TYPE outbox_event_status AS ENUM ('PENDING', 'PUBLISHING', 'PUBLISHED', 'FAILED');

-- =============================================================================
-- TABLES
-- =============================================================================

-- -----------------------------------------------------------------------------
-- gps_trails (batch-inserted from Redis buffer every 5–10 minutes)
-- -----------------------------------------------------------------------------
CREATE TABLE gps_trails (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    trip_id UUID NOT NULL,    -- logical FK trip.trips
    latitude DECIMAL(10,7) NOT NULL,
    longitude DECIMAL(10,7) NOT NULL,
    speed_kmh DECIMAL(6,2) NULL,
    heading_deg DECIMAL(5,2) NULL,
    recorded_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_gps_trails_lat_range CHECK (latitude BETWEEN -90 AND 90),
    CONSTRAINT chk_gps_trails_lng_range CHECK (longitude BETWEEN -180 AND 180),
    CONSTRAINT chk_gps_trails_speed_non_negative CHECK (speed_kmh IS NULL OR speed_kmh >= 0)
);

CREATE INDEX idx_gps_trails_trip_id_recorded_at
    ON gps_trails (trip_id, recorded_at);
CREATE INDEX idx_gps_trails_recorded_at ON gps_trails (recorded_at);

COMMENT ON TABLE gps_trails IS
    'GPS history per trip. Batch inserted by BullMQ scheduled job every 5–10 min from Redis buffer.';
COMMENT ON COLUMN gps_trails.recorded_at IS
    'Time GPS sample was captured by driver app (not insert time).';

-- -----------------------------------------------------------------------------
-- outbox_events
-- -----------------------------------------------------------------------------
CREATE TABLE outbox_events (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    event_type VARCHAR(100) NOT NULL,
    payload JSONB NOT NULL,
    status outbox_event_status NOT NULL DEFAULT 'PENDING',
    retry_count INT NOT NULL DEFAULT 0,
    last_error TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    published_at TIMESTAMPTZ NULL
);

CREATE INDEX idx_outbox_events_status_created
    ON outbox_events (status, created_at);

-- =============================================================================
-- NOTE: NestJS Tracking Service does NOT use Hangfire.
-- BullMQ scheduled jobs (Redis-backed) handle GPS batch flush + Outbox poll.
-- =============================================================================
