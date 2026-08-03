-- =============================================================================
-- VietRide :: Tracking Service :: PostgreSQL 16 schema
-- Database: vietride_tracking
-- Schema: vietride_tracking
-- Framework: NestJS + Prisma ORM
-- =============================================================================
-- Minimal DB — most state lives in Redis (last position, GPS buffer, ETA cache).
-- This DB persists batched GpsTrail history, Outbox reliability data, and
-- capability grants for guest trip sharing.
-- =============================================================================

CREATE EXTENSION IF NOT EXISTS "pgcrypto";

CREATE SCHEMA IF NOT EXISTS vietride_tracking;
SET search_path TO vietride_tracking, public;

-- =============================================================================
-- ENUMS
-- =============================================================================

CREATE TYPE outbox_event_status AS ENUM ('PENDING', 'PUBLISHING', 'PUBLISHED', 'FAILED');
CREATE TYPE trip_share_grant_revoke_reason AS ENUM (
    'USER_REVOKED',
    'TRIP_TERMINATED',
    'EXPIRED',
    'CREATION_ROLLBACK'
);

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

CREATE UNIQUE INDEX uq_gps_trails_trip_recorded_at
    ON gps_trails (trip_id, recorded_at);
CREATE INDEX idx_gps_trails_recorded_at ON gps_trails (recorded_at);

COMMENT ON TABLE gps_trails IS
    'GPS history per trip. Batch inserted by BullMQ scheduled job every 5–10 min from Redis buffer.';
COMMENT ON COLUMN gps_trails.recorded_at IS
    'Time GPS sample was captured by driver app (not insert time).';

-- -----------------------------------------------------------------------------
-- trip_share_grants (anonymous capability links for active main trips)
-- -----------------------------------------------------------------------------
CREATE TABLE trip_share_grants (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    trip_id UUID NOT NULL,                    -- logical FK trip.trips
    created_by_user_id UUID NOT NULL,         -- logical FK identity.users
    token_hash CHAR(64) NOT NULL,
    token_version SMALLINT NOT NULL DEFAULT 1,
    expires_at TIMESTAMPTZ NOT NULL,
    revoked_at TIMESTAMPTZ NULL,
    revoke_reason trip_share_grant_revoke_reason NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_trip_share_grants_expires_after_created
        CHECK (expires_at > created_at),
    CONSTRAINT chk_trip_share_grants_token_hash
        CHECK (token_hash ~ '^[0-9a-f]{64}$'),
    CONSTRAINT chk_trip_share_grants_token_version_positive
        CHECK (token_version > 0)
);

CREATE UNIQUE INDEX uq_trip_share_grants_token_hash
    ON trip_share_grants (token_hash);
CREATE UNIQUE INDEX uq_trip_share_grants_active_owner_trip
    ON trip_share_grants (trip_id, created_by_user_id)
    WHERE revoked_at IS NULL;
CREATE INDEX idx_trip_share_grants_active_expires_at
    ON trip_share_grants (expires_at)
    WHERE revoked_at IS NULL;

COMMENT ON TABLE trip_share_grants IS
    'Capability grants for anonymous guest tracking. Only a SHA-256 token hash is persisted.';

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
    next_retry_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    published_at TIMESTAMPTZ NULL
);

CREATE INDEX idx_outbox_events_status_created
    ON outbox_events (status, created_at);

CREATE INDEX idx_outbox_events_status_next_retry
    ON outbox_events (status, next_retry_at);

CREATE INDEX idx_outbox_events_status_updated
    ON outbox_events (status, updated_at);

-- -----------------------------------------------------------------------------
-- outbox_dlq (durable terminal path after retry_count > 5)
-- -----------------------------------------------------------------------------
CREATE TABLE outbox_dlq (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    event_id UUID NOT NULL,
    event_type VARCHAR(100) NOT NULL,
    payload JSONB NOT NULL,
    retry_count INT NOT NULL,
    last_error TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    terminal_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX uq_outbox_dlq_event_id ON outbox_dlq (event_id);
CREATE INDEX idx_outbox_dlq_terminal_event_id ON outbox_dlq (terminal_at, event_id);

-- =============================================================================
-- NOTE: NestJS Tracking Service does NOT use Hangfire.
-- BullMQ scheduled jobs (Redis-backed) handle GPS batch flush + Outbox poll.
-- =============================================================================
