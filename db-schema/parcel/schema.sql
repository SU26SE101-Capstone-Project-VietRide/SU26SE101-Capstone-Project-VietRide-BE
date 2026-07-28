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
    'RESERVED',
    'CHECKED_IN',
    'PENDING_FINAL_PAYMENT',
    'READY_TO_LOAD',
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
    check_in_photo_urls JSONB NULL,
    delivery_photo_urls JSONB NULL,
    size_category parcel_size_category NOT NULL,
    estimated_size_category parcel_size_category NOT NULL,
    actual_size_category parcel_size_category NULL,
    estimated_length_cm DECIMAL(8,2) NOT NULL DEFAULT 1,
    estimated_width_cm DECIMAL(8,2) NOT NULL DEFAULT 1,
    estimated_height_cm DECIMAL(8,2) NOT NULL DEFAULT 1,
    estimated_weight_kg DECIMAL(8,2) NOT NULL,
    estimated_volume_m3 DECIMAL(10,4) NOT NULL DEFAULT 0.0001,
    estimated_dim_weight_kg DECIMAL(8,2) NOT NULL DEFAULT 0.01,
    estimated_chargeable_weight_kg DECIMAL(8,2) NOT NULL DEFAULT 0.01,
    actual_length_cm DECIMAL(8,2) NULL,
    actual_width_cm DECIMAL(8,2) NULL,
    actual_height_cm DECIMAL(8,2) NULL,
    actual_weight_kg DECIMAL(8,2) NULL,    -- set by staff after re-weigh
    actual_volume_m3 DECIMAL(10,4) NULL,
    actual_dim_weight_kg DECIMAL(8,2) NULL,
    actual_chargeable_weight_kg DECIMAL(8,2) NULL,
    delivery_method parcel_delivery_method NOT NULL DEFAULT 'TERMINAL_PICKUP',
    -- pricing
    total_price_vnd BIGINT NOT NULL DEFAULT 0,
    deposit_percent DECIMAL(5,2) NOT NULL DEFAULT 100,
    deposit_amount BIGINT NOT NULL,
    original_deposit_amount BIGINT NOT NULL DEFAULT 0,
    discount_amount BIGINT NOT NULL DEFAULT 0,
    voucher_code VARCHAR(50) NULL,
    voucher_usage_id UUID NULL,
    additional_amount BIGINT NOT NULL DEFAULT 0,
    refund_amount BIGINT NOT NULL DEFAULT 0,
    additional_payment_id UUID NULL,    -- logical FK payment.payments
    additional_payment_deadline TIMESTAMPTZ NULL,
    -- settlement v2 canonical fields (legacy pricing columns above remain for rollout)
    estimated_gross_price_vnd BIGINT NOT NULL DEFAULT 0,
    final_gross_price_vnd BIGINT NOT NULL DEFAULT 0,
    discount_amount_vnd BIGINT NOT NULL DEFAULT 0,
    estimated_total_price_vnd BIGINT NOT NULL DEFAULT 0,
    final_total_price_vnd BIGINT NOT NULL DEFAULT 0,
    deposit_required_vnd BIGINT NOT NULL DEFAULT 0,
    deposit_paid_vnd BIGINT NOT NULL DEFAULT 0,
    balance_required_vnd BIGINT NOT NULL DEFAULT 0,
    balance_paid_vnd BIGINT NOT NULL DEFAULT 0,
    refund_due_vnd BIGINT NOT NULL DEFAULT 0,
    refunded_amount_vnd BIGINT NOT NULL DEFAULT 0,
    forfeited_deposit_vnd BIGINT NOT NULL DEFAULT 0,
    deposit_payment_id UUID NULL,
    balance_payment_id UUID NULL,
    final_payment_deadline TIMESTAMPTZ NULL,
    load_cutoff_at TIMESTAMPTZ NULL,
    latest_check_in_at TIMESTAMPTZ NULL,
    checked_in_at TIMESTAMPTZ NULL,
    checked_in_by_user_id UUID NULL,
    reweighed_at TIMESTAMPTZ NULL,
    reweighed_by_user_id UUID NULL,
    price_per_kg_vnd BIGINT NOT NULL DEFAULT 0,
    minimum_price_vnd BIGINT NOT NULL DEFAULT 0,
    dim_weight_factor DECIMAL(10,2) NOT NULL DEFAULT 6000,
    settlement_policy_version INT NOT NULL DEFAULT 1,
    -- status
    status parcel_status NOT NULL,
    pending_action_type VARCHAR(40) NULL,
    pending_action_resume_status parcel_status NULL,
    pending_action_reason TEXT NULL,
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
        CHECK (total_price_vnd >= 0 AND deposit_amount >= 0 AND original_deposit_amount >= 0 AND discount_amount >= 0 AND additional_amount >= 0 AND refund_amount >= 0),
    CONSTRAINT chk_parcels_settlement_amounts_non_negative
        CHECK (estimated_gross_price_vnd >= 0 AND final_gross_price_vnd >= 0 AND discount_amount_vnd >= 0 AND estimated_total_price_vnd >= 0 AND final_total_price_vnd >= 0 AND deposit_required_vnd >= 0 AND deposit_paid_vnd >= 0 AND balance_required_vnd >= 0 AND balance_paid_vnd >= 0 AND refund_due_vnd >= 0 AND refunded_amount_vnd >= 0 AND forfeited_deposit_vnd >= 0),
    CONSTRAINT chk_parcels_settlement_policy_version_positive
        CHECK (settlement_policy_version > 0),
    CONSTRAINT chk_parcels_dimensions_positive
        CHECK (estimated_length_cm > 0 AND estimated_width_cm > 0 AND estimated_height_cm > 0),
    CONSTRAINT chk_parcels_actual_dimensions_positive
        CHECK ((actual_length_cm IS NULL AND actual_width_cm IS NULL AND actual_height_cm IS NULL) OR (actual_length_cm > 0 AND actual_width_cm > 0 AND actual_height_cm > 0)),
    CONSTRAINT chk_parcels_volume_positive
        CHECK (estimated_volume_m3 > 0),
    CONSTRAINT chk_parcels_weight_positive
        CHECK (estimated_weight_kg > 0),
    CONSTRAINT chk_parcels_actual_weight_positive
        CHECK (actual_weight_kg IS NULL OR actual_weight_kg > 0),
    CONSTRAINT chk_parcels_check_in_photo_urls_max_three
        CHECK (check_in_photo_urls IS NULL OR (jsonb_typeof(check_in_photo_urls) = 'array' AND jsonb_array_length(check_in_photo_urls) <= 3)),
    CONSTRAINT chk_parcels_delivery_photo_urls_max_three
        CHECK (delivery_photo_urls IS NULL OR (jsonb_typeof(delivery_photo_urls) = 'array' AND jsonb_array_length(delivery_photo_urls) <= 3))
);

CREATE UNIQUE INDEX uq_parcels_parcel_code ON parcels (parcel_code);
CREATE UNIQUE INDEX uq_parcels_delivery_token ON parcels (delivery_token)
    WHERE delivery_token IS NOT NULL;
CREATE INDEX idx_parcels_voucher_usage_id ON parcels (voucher_usage_id)
    WHERE voucher_usage_id IS NOT NULL;
CREATE INDEX idx_parcels_sender_user_id_created_at
    ON parcels (sender_user_id, created_at DESC);
CREATE INDEX idx_parcels_recipient_user_id_created_at
    ON parcels (recipient_user_id, created_at DESC) WHERE recipient_user_id IS NOT NULL;
CREATE INDEX idx_parcels_trip_id_status ON parcels (trip_id, status);
CREATE INDEX idx_parcels_operator_id_status ON parcels (operator_id, status);
CREATE INDEX idx_parcels_status_updated_at ON parcels (status, updated_at)
    WHERE status IN (
        'PENDING_PAYMENT', 'RESERVED', 'CHECKED_IN', 'PENDING_FINAL_PAYMENT',
        'READY_TO_LOAD', 'PENDING_OPERATOR_REVIEW',
        'PENDING_OPERATOR_ACTION', 'PENDING_TRANSFER_CONFIRM', 'DELIVERED_PENDING_CONFIRM',
        'DELIVERY_REJECTED', 'TRANSFER_ESCALATED'
    );
CREATE INDEX idx_parcels_additional_payment_deadline
    ON parcels (additional_payment_deadline)
    WHERE status = 'PENDING_ADDITIONAL_PAYMENT';
CREATE INDEX idx_parcels_latest_check_in_at
    ON parcels (latest_check_in_at)
    WHERE status = 'RESERVED' AND latest_check_in_at IS NOT NULL;
CREATE INDEX idx_parcels_final_payment_deadline
    ON parcels (final_payment_deadline)
    WHERE status = 'PENDING_FINAL_PAYMENT' AND final_payment_deadline IS NOT NULL;
CREATE INDEX idx_parcels_deposit_payment_id
    ON parcels (deposit_payment_id) WHERE deposit_payment_id IS NOT NULL;
CREATE INDEX idx_parcels_balance_payment_id
    ON parcels (balance_payment_id) WHERE balance_payment_id IS NOT NULL;
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
CREATE INDEX idx_parcels_confirmed_report ON parcels (confirmed_at, operator_id)
    WHERE status = 'DELIVERY_CONFIRMED' AND confirmed_at IS NOT NULL;

-- -----------------------------------------------------------------------------
-- platform_parcel_stats (Day 42 exact-range earned projection)
-- -----------------------------------------------------------------------------
CREATE TABLE platform_parcel_stats (
    parcel_id UUID PRIMARY KEY REFERENCES parcels(id) ON DELETE CASCADE,
    operator_id UUID NOT NULL,
    confirmed_at TIMESTAMPTZ NOT NULL,
    parcel_revenue_vnd BIGINT NOT NULL,
    projected_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_platform_parcel_stats_confirmed_operator
    ON platform_parcel_stats (confirmed_at, operator_id);

CREATE OR REPLACE FUNCTION sync_platform_parcel_stats()
RETURNS TRIGGER AS $$
DECLARE
    source_id UUID := CASE WHEN TG_OP = 'DELETE' THEN OLD.id ELSE NEW.id END;
BEGIN
    IF TG_OP <> 'DELETE'
       AND NEW.status = 'DELIVERY_CONFIRMED'::vietride_parcel.parcel_status
       AND NEW.confirmed_at IS NOT NULL THEN
        INSERT INTO platform_parcel_stats (
            parcel_id, operator_id, confirmed_at, parcel_revenue_vnd, projected_at
        )
        VALUES (
            NEW.id,
            NEW.operator_id,
            NEW.confirmed_at,
            (NEW.deposit_amount::numeric
                + NEW.additional_amount::numeric
                - NEW.refund_amount::numeric)::bigint,
            now()
        )
        ON CONFLICT (parcel_id) DO UPDATE SET
            operator_id = EXCLUDED.operator_id,
            confirmed_at = EXCLUDED.confirmed_at,
            parcel_revenue_vnd = EXCLUDED.parcel_revenue_vnd,
            projected_at = now();
    ELSE
        DELETE FROM platform_parcel_stats WHERE parcel_id = source_id;
    END IF;

    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_sync_platform_parcel_stats
    AFTER INSERT OR UPDATE OR DELETE ON parcels
    FOR EACH ROW EXECUTE FUNCTION sync_platform_parcel_stats();

CREATE OR REPLACE FUNCTION rebuild_platform_parcel_stats()
RETURNS VOID AS $$
BEGIN
    INSERT INTO platform_parcel_stats (
        parcel_id, operator_id, confirmed_at, parcel_revenue_vnd, projected_at
    )
    SELECT
        id,
        operator_id,
        confirmed_at,
        (deposit_amount::numeric
            + additional_amount::numeric
            - refund_amount::numeric)::bigint,
        now()
    FROM parcels
    WHERE status = 'DELIVERY_CONFIRMED'::vietride_parcel.parcel_status
      AND confirmed_at IS NOT NULL
    ON CONFLICT (parcel_id) DO UPDATE SET
        operator_id = EXCLUDED.operator_id,
        confirmed_at = EXCLUDED.confirmed_at,
        parcel_revenue_vnd = EXCLUDED.parcel_revenue_vnd,
        projected_at = now();

    DELETE FROM platform_parcel_stats projection
    WHERE NOT EXISTS (
        SELECT 1
        FROM parcels source
        WHERE source.id = projection.parcel_id
          AND source.status = 'DELIVERY_CONFIRMED'::vietride_parcel.parcel_status
          AND source.confirmed_at IS NOT NULL
    );
END;
$$ LANGUAGE plpgsql;

SELECT rebuild_platform_parcel_stats();

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
    price_per_chargeable_kg_vnd BIGINT NOT NULL DEFAULT 0,
    minimum_price_vnd BIGINT NOT NULL DEFAULT 0,
    effective_from TIMESTAMPTZ NOT NULL DEFAULT now(),
    effective_until TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (route_id, size_category),
    CONSTRAINT chk_parcel_route_fares_price_non_negative CHECK (price_vnd >= 0),
    CONSTRAINT chk_parcel_route_fares_weight_price_non_negative
        CHECK (price_per_chargeable_kg_vnd >= 0 AND minimum_price_vnd >= 0),
    CONSTRAINT chk_parcel_route_fares_effective_order
        CHECK (effective_until IS NULL OR effective_until > effective_from)
);

CREATE INDEX idx_parcel_route_fares_operator_id ON parcel_route_fares (operator_id);

COMMENT ON COLUMN parcel_route_fares.operator_id IS
    'Denormalized from Route.operator_id for tenant filter without cross-service join.';

-- -----------------------------------------------------------------------------
-- system_configs / operator_deposit_policies (Parcel logistics policy)
-- -----------------------------------------------------------------------------
CREATE TABLE system_configs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    key VARCHAR(100) NOT NULL,
    decimal_value DECIMAL(12,4) NOT NULL,
    version INT NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    effective_from TIMESTAMPTZ NOT NULL,
    effective_to TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_system_configs_version_positive CHECK (version > 0)
);

CREATE UNIQUE INDEX uq_system_configs_key_version ON system_configs (key, version);
CREATE INDEX idx_system_configs_lookup ON system_configs (key, is_active, effective_from);

CREATE TABLE operator_deposit_policies (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id UUID NOT NULL,
    route_id UUID NULL,
    deposit_percent DECIMAL(5,2) NOT NULL,
    effective_from TIMESTAMPTZ NOT NULL,
    effective_to TIMESTAMPTZ NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_operator_deposit_policies_percent
        CHECK (deposit_percent > 0 AND deposit_percent <= 100)
);

CREATE INDEX idx_operator_deposit_policies_lookup
    ON operator_deposit_policies (operator_id, route_id, is_active, effective_from);

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
-- -----------------------------------------------------------------------------
-- integration_inbox (durable RabbitMQ consumer idempotency)
-- -----------------------------------------------------------------------------
CREATE TABLE integration_inbox (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    consumer_name VARCHAR(200) NOT NULL,
    message_id UUID NOT NULL,
    payload_hash CHAR(64) NOT NULL,
    processed_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX uq_integration_inbox_consumer_message
    ON integration_inbox (consumer_name, message_id);

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

-- -----------------------------------------------------------------------------
-- outbox_dlq (terminal publish failures; one row per event)
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
-- TRIGGERS
-- =============================================================================

CREATE OR REPLACE FUNCTION trg_set_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    IF NEW.updated_at IS NOT DISTINCT FROM OLD.updated_at THEN
        NEW.updated_at = now();
    END IF;
    RETURN NEW;
END;
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
