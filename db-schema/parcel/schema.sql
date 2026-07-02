-- =============================================================================
-- VietRide :: Parcel Service :: PostgreSQL 16 schema
-- Database: vietride_parcel
-- Framework: .NET Core 8 + EF Core 8
-- =============================================================================

CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- =============================================================================
-- ENUMS
-- =============================================================================

CREATE TYPE parcel_status AS ENUM (
    'PENDING_OPERATOR_REVIEW',
    'PENDING_PAYMENT',
    'PENDING',
    'PENDING_ADDITIONAL_PAYMENT',
    'LOADED',
    'IN_TRANSIT',
    'PENDING_TRANSFER_CONFIRM',
    'TRANSFER_ESCALATED',
    'UNLOADED',
    'DELIVERED_PENDING_CONFIRM',
    'DELIVERY_CONFIRMED',
    'DELIVERY_REJECTED',
    'RETURN_INITIATED',
    'RETURNED',
    'PENDING_OPERATOR_ACTION',
    'CANCELLED',
    'REJECTED',
    'EXPIRED'
);

CREATE TYPE parcel_size_category AS ENUM (
    'SMALL', 'MEDIUM', 'LARGE', 'EXTRA_LARGE'
);

CREATE TYPE parcel_review_decision AS ENUM (
    'PENDING', 'APPROVED', 'REJECTED'
);

CREATE TYPE parcel_delivery_method AS ENUM ('TERMINAL_PICKUP');

CREATE TYPE outbox_event_status AS ENUM ('PENDING', 'PUBLISHING', 'PUBLISHED', 'FAILED');

-- =============================================================================
-- TABLES
-- =============================================================================

-- -----------------------------------------------------------------------------
-- parcels
-- -----------------------------------------------------------------------------
CREATE TABLE parcels (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    parcel_code VARCHAR(30) NOT NULL,    -- "VRP-yyyyMMdd-XXXXXXXX"
    -- sender / recipient
    sender_user_id UUID NOT NULL,    -- logical FK identity.users (NOT NULL — no walk-in)
    recipient_user_id UUID NULL,     -- logical FK; null = recipient has no account
    recipient_name VARCHAR(255) NOT NULL,
    recipient_phone VARCHAR(20) NOT NULL,
    recipient_email VARCHAR(255) NULL,   -- optional; if null → manual confirm only
    -- trip context (logical FKs)
    operator_id UUID NOT NULL,
    trip_id UUID NOT NULL,
    dropoff_stop_id UUID NULL,    -- null = destination station terminal; not null = along-route Stop
    booking_id UUID NULL,         -- logical FK booking.bookings; null = parcel-only
    -- parcel info
    description TEXT NULL,
    photo_url TEXT NULL,
    size_category parcel_size_category NOT NULL,
    estimated_weight_kg DECIMAL(8,2) NOT NULL,
    actual_weight_kg DECIMAL(8,2) NULL,    -- set by staff after re-weigh
    delivery_method parcel_delivery_method NOT NULL DEFAULT 'TERMINAL_PICKUP',
    -- pricing
    deposit_amount BIGINT NOT NULL,
    additional_amount BIGINT NOT NULL DEFAULT 0,
    additional_payment_id UUID NULL,    -- logical FK payment.payments
    additional_payment_deadline TIMESTAMPTZ NULL,
    -- status
    status parcel_status NOT NULL,
    rejection_reason TEXT NULL,
    cancellation_reason TEXT NULL,
    -- EXTRA_LARGE review
    review_decision parcel_review_decision NULL,
    reviewed_at TIMESTAMPTZ NULL,
    reviewed_by_user_id UUID NULL,    -- logical FK
    -- delivery confirmation token (email link)
    delivery_token UUID NULL,
    delivery_token_expires_at TIMESTAMPTZ NULL,
    delivery_token_revoked_at TIMESTAMPTZ NULL,
    -- lifecycle timestamps
    loaded_at TIMESTAMPTZ NULL,
    loaded_by_user_id UUID NULL,
    unloaded_at TIMESTAMPTZ NULL,
    delivered_pending_confirm_at TIMESTAMPTZ NULL,
    confirmed_at TIMESTAMPTZ NULL,
    confirmed_by_user_id UUID NULL,
    confirmed_by_ip VARCHAR(45) NULL,
    confirm_note TEXT NULL,
    rejected_at TIMESTAMPTZ NULL,
    last_reminder_at TIMESTAMPTZ NULL,
    -- transfer fields (Vehicle Substitution)
    transfer_target_trip_id UUID NULL,
    transfer_requested_at TIMESTAMPTZ NULL,
    transfer_confirmed_at TIMESTAMPTZ NULL,
    transfer_confirmed_by_user_id UUID NULL,
    -- return fields
    return_reason TEXT NULL,
    returned_at TIMESTAMPTZ NULL,
    returned_by_user_id UUID NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_parcels_amounts_non_negative
        CHECK (deposit_amount >= 0 AND additional_amount >= 0),
    CONSTRAINT chk_parcels_weight_positive
        CHECK (estimated_weight_kg > 0),
    CONSTRAINT chk_parcels_actual_weight_positive
        CHECK (actual_weight_kg IS NULL OR actual_weight_kg > 0)
);

CREATE UNIQUE INDEX uq_parcels_parcel_code ON parcels (parcel_code);
CREATE UNIQUE INDEX uq_parcels_delivery_token ON parcels (delivery_token)
    WHERE delivery_token IS NOT NULL;
CREATE INDEX idx_parcels_sender_user_id_created_at
    ON parcels (sender_user_id, created_at DESC);
CREATE INDEX idx_parcels_recipient_user_id_created_at
    ON parcels (recipient_user_id, created_at DESC) WHERE recipient_user_id IS NOT NULL;
CREATE INDEX idx_parcels_trip_id_status ON parcels (trip_id, status);
CREATE INDEX idx_parcels_operator_id_status ON parcels (operator_id, status);
CREATE INDEX idx_parcels_status_updated_at ON parcels (status, updated_at)
    WHERE status IN (
        'PENDING', 'PENDING_ADDITIONAL_PAYMENT', 'PENDING_OPERATOR_REVIEW',
        'PENDING_OPERATOR_ACTION', 'PENDING_TRANSFER_CONFIRM', 'DELIVERED_PENDING_CONFIRM',
        'DELIVERY_REJECTED', 'TRANSFER_ESCALATED'
    );
CREATE INDEX idx_parcels_additional_payment_deadline
    ON parcels (additional_payment_deadline)
    WHERE status = 'PENDING_ADDITIONAL_PAYMENT';
CREATE INDEX idx_parcels_transfer_target_trip_id
    ON parcels (transfer_target_trip_id)
    WHERE transfer_target_trip_id IS NOT NULL;
-- Additional payment & audit FK indexes (rare-query lookups for support/audit).
CREATE INDEX idx_parcels_additional_payment_id
    ON parcels (additional_payment_id) WHERE additional_payment_id IS NOT NULL;
CREATE INDEX idx_parcels_reviewed_by_user_id
    ON parcels (reviewed_by_user_id) WHERE reviewed_by_user_id IS NOT NULL;
CREATE INDEX idx_parcels_loaded_by_user_id
    ON parcels (loaded_by_user_id) WHERE loaded_by_user_id IS NOT NULL;
CREATE INDEX idx_parcels_confirmed_by_user_id
    ON parcels (confirmed_by_user_id) WHERE confirmed_by_user_id IS NOT NULL;
CREATE INDEX idx_parcels_transfer_confirmed_by_user_id
    ON parcels (transfer_confirmed_by_user_id) WHERE transfer_confirmed_by_user_id IS NOT NULL;
CREATE INDEX idx_parcels_returned_by_user_id
    ON parcels (returned_by_user_id) WHERE returned_by_user_id IS NOT NULL;

COMMENT ON COLUMN parcels.parcel_code IS
    'Format VRP-yyyyMMdd-XXXXXXXX. Distinct from booking VR- prefix.';
COMMENT ON COLUMN parcels.sender_user_id IS
    'REQUIRED — no walk-in parcel. Sender must have VietRide account.';
COMMENT ON COLUMN parcels.dropoff_stop_id IS
    'NULL = deliver at destination station terminal. NOT NULL = along-route Stop with allowDropoff=true.';
COMMENT ON COLUMN parcels.delivery_token IS
    'UUID v4 for email link; revoked on resend.';

-- -----------------------------------------------------------------------------
-- parcel_route_fares (operator config per route per size)
-- -----------------------------------------------------------------------------
CREATE TABLE parcel_route_fares (
    route_id UUID NOT NULL,    -- logical FK trip.routes
    size_category parcel_size_category NOT NULL,
    operator_id UUID NOT NULL,    -- logical FK (denormalized for tenant filter)
    price_vnd BIGINT NOT NULL,
    effective_from TIMESTAMPTZ NOT NULL DEFAULT now(),
    effective_until TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (route_id, size_category),
    CONSTRAINT chk_parcel_route_fares_price_non_negative CHECK (price_vnd >= 0),
    CONSTRAINT chk_parcel_route_fares_effective_order
        CHECK (effective_until IS NULL OR effective_until > effective_from)
);

CREATE INDEX idx_parcel_route_fares_operator_id ON parcel_route_fares (operator_id);

COMMENT ON COLUMN parcel_route_fares.operator_id IS
    'Denormalized from Route.operator_id for tenant filter without cross-service join.';

-- -----------------------------------------------------------------------------
-- parcel_stats (counter table per operator per day)
-- -----------------------------------------------------------------------------
CREATE TABLE parcel_stats (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id UUID NOT NULL,
    stat_date DATE NOT NULL,
    total_parcels INT NOT NULL DEFAULT 0,
    total_loaded INT NOT NULL DEFAULT 0,
    total_delivered INT NOT NULL DEFAULT 0,
    total_rejected INT NOT NULL DEFAULT 0,
    total_returned INT NOT NULL DEFAULT 0,
    total_revenue BIGINT NOT NULL DEFAULT 0,
    total_refunded BIGINT NOT NULL DEFAULT 0,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX uq_parcel_stats_operator_date ON parcel_stats (operator_id, stat_date);
CREATE INDEX idx_parcel_stats_stat_date ON parcel_stats (stat_date DESC);

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
    ON outbox_events (status, created_at) WHERE status IN ('PENDING', 'PUBLISHING', 'FAILED');

-- =============================================================================
-- TRIGGERS
-- =============================================================================

CREATE OR REPLACE FUNCTION trg_set_updated_at()
RETURNS TRIGGER AS $$
BEGIN NEW.updated_at = now(); RETURN NEW; END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_parcels_updated_at BEFORE UPDATE ON parcels
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_parcel_route_fares_updated_at BEFORE UPDATE ON parcel_route_fares
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_parcel_stats_updated_at BEFORE UPDATE ON parcel_stats
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();

-- =============================================================================
-- Hangfire schema (auto-created): undo-reject 15m, auto-reject EXTRA_LARGE 24h,
-- auto-reject PENDING 30m after Trip IN_PROGRESS, auto-reject
-- PENDING_ADDITIONAL_PAYMENT timeout (5m interval), PENDING_TRANSFER_CONFIRM
-- 30m escalation, PENDING_OPERATOR_ACTION 2h re-alert,
-- DELIVERED_PENDING_CONFIRM 7-day re-alert (daily 9am).
-- =============================================================================
