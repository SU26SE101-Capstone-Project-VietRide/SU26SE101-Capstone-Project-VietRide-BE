-- =============================================================================
-- VietRide :: Booking Service :: PostgreSQL 16 schema
-- Database: vietride_booking
-- Framework: .NET Core 8 + EF Core 8
-- =============================================================================

CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- =============================================================================
-- ENUMS
-- =============================================================================

CREATE TYPE public.booking_status AS ENUM (
    'PENDING_PAYMENT', 'CONFIRMED', 'COMPLETED',
    'EXPIRED', 'CANCELLED', 'NO_SHOW', 'PARTIAL_NO_SHOW',
    'REFUNDED', 'DISRUPTED'
);

CREATE TYPE booking_cancellation_reason AS ENUM (
    'USER_INITIATED',
    'OPERATOR_CANCELLED_TRIP',
    'OPERATOR_DISRUPTED_IN_PROGRESS',
    'SCHEDULE_CHANGED',
    'ROUTE_CHANGED_REFUSED',
    'VEHICLE_SUBSTITUTION_DOWNGRADE',
    'VEHICLE_SUBSTITUTION_NO_SEAT',
    'STOP_DISABLED_REFUSED'
);

CREATE TYPE trip_direction AS ENUM ('OUTBOUND', 'RETURN');

CREATE TYPE passenger_boarding_status AS ENUM ('PENDING', 'BOARDED', 'NO_SHOW');

CREATE TYPE booking_transfer_confirmation_status AS ENUM (
    'PENDING_CONFIRM', 'ESCALATED', 'CONFIRMED', 'NOT_REQUIRED'
);

CREATE TYPE ticket_status AS ENUM (
    'PENDING_PAYMENT', 'ISSUED', 'USED',
    'CANCELLED', 'REFUNDED', 'EXPIRED'
);

CREATE TYPE booking_pending_action_reason AS ENUM (
    'ROUTE_CHANGE',
    'SEAT_DOWNGRADE',
    'SCHEDULE_CHANGE',
    'PENDING_SEAT_ASSIGNMENT',
    'STOP_DISABLED'
);

CREATE TYPE booking_pending_action_severity AS ENUM ('MEDIUM', 'MAJOR');

CREATE TYPE booking_pending_action_resolved AS ENUM (
    'ACCEPTED', 'REJECTED', 'AUTO_FALLBACK_DESTINATION',
    'AUTO_CANCELLED_NO_SEAT', 'OPERATOR_RESOLVED', 'SUPERSEDED'
);

CREATE TYPE voucher_type AS ENUM ('PERCENT_OFF', 'FIXED_AMOUNT');

CREATE TYPE voucher_funding_type AS ENUM ('VIETRIDE_FUNDED', 'OPERATOR_FUNDED');

CREATE TYPE operator_voucher_consent_status AS ENUM ('PENDING', 'ACCEPTED', 'REJECTED');

CREATE TYPE outbox_event_status AS ENUM ('PENDING', 'PUBLISHING', 'PUBLISHED', 'FAILED');

-- =============================================================================
-- TABLES
-- =============================================================================

-- -----------------------------------------------------------------------------
-- bookings
-- -----------------------------------------------------------------------------
CREATE TABLE bookings (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    booking_code VARCHAR(30) NOT NULL,    -- "VR-yyyyMMdd-XXXXXXXX"
    -- buyer (logical FK to identity.users)
    passenger_user_id UUID NOT NULL,
    buyer_display_name VARCHAR(255) NULL,
    buyer_phone VARCHAR(20) NULL,
    buyer_email VARCHAR(255) NULL,
    buyer_avatar_url VARCHAR(2048) NULL,
    -- trip context (logical FKs)
    trip_id UUID NOT NULL,
    operator_id UUID NOT NULL,
    seat_lock_token UUID NULL,
    -- pickup/dropoff: 4 columns mutually exclusive
    pickup_station_id UUID NULL,
    pickup_stop_id UUID NULL,
    dropoff_station_id UUID NULL,
    dropoff_stop_id UUID NULL,
    -- stable passenger-selected point snapshots; nullable for legacy rows
    pickup_point_type_snapshot VARCHAR(16) NULL,
    pickup_point_id_snapshot UUID NULL,
    pickup_point_name_snapshot VARCHAR(255) NULL,
    pickup_point_address_snapshot VARCHAR(500) NULL,
    pickup_point_planned_at_snapshot TIMESTAMPTZ NULL,
    dropoff_point_type_snapshot VARCHAR(16) NULL,
    dropoff_point_id_snapshot UUID NULL,
    dropoff_point_name_snapshot VARCHAR(255) NULL,
    dropoff_point_address_snapshot VARCHAR(500) NULL,
    dropoff_point_planned_at_snapshot TIMESTAMPTZ NULL,
    -- amounts
    base_fare BIGINT NOT NULL,
    discount_amount BIGINT NOT NULL DEFAULT 0,
    total_amount BIGINT NOT NULL,
    -- lifecycle
    status booking_status NOT NULL DEFAULT 'PENDING_PAYMENT',
    cancellation_reason booking_cancellation_reason NULL,
    refund_override BOOLEAN NOT NULL DEFAULT FALSE,
    -- round-trip
    booking_group_id UUID NULL,
    trip_direction trip_direction NULL,
    -- trip snapshot (avoid cross-service call for history list)
    trip_snapshot_origin_name VARCHAR(255) NULL,
    trip_snapshot_dest_name VARCHAR(255) NULL,
    trip_snapshot_departure TIMESTAMPTZ NULL,
    trip_current_departure TIMESTAMPTZ NULL,
    trip_snapshot_route_name VARCHAR(255) NULL,
    -- timestamps
    confirmed_at TIMESTAMPTZ NULL,
    cancelled_at TIMESTAMPTZ NULL,
    refunded_at TIMESTAMPTZ NULL,
    expired_at TIMESTAMPTZ NULL,
    completed_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    -- constraints
    CONSTRAINT chk_bookings_pickup_exactly_one
        CHECK ((pickup_station_id IS NOT NULL)::INT + (pickup_stop_id IS NOT NULL)::INT = 1),
    CONSTRAINT chk_bookings_dropoff_at_most_one
        CHECK ((dropoff_station_id IS NOT NULL)::INT + (dropoff_stop_id IS NOT NULL)::INT <= 1),
    CONSTRAINT chk_bookings_amounts_non_negative
        CHECK (base_fare >= 0 AND discount_amount >= 0 AND total_amount >= 0),
    CONSTRAINT chk_bookings_total_le_base
        CHECK (total_amount <= base_fare)
);

CREATE UNIQUE INDEX uq_bookings_booking_code ON bookings (booking_code);
CREATE INDEX idx_bookings_passenger_user_id_created_at
    ON bookings (passenger_user_id, created_at DESC);
CREATE INDEX idx_bookings_trip_id_status ON bookings (trip_id, status);
CREATE INDEX idx_bookings_operator_id_status ON bookings (operator_id, status);
CREATE INDEX idx_bookings_booking_group_id ON bookings (booking_group_id)
    WHERE booking_group_id IS NOT NULL;
CREATE INDEX idx_bookings_status_created_at ON bookings (status, created_at)
    WHERE status IN ('PENDING_PAYMENT', 'CONFIRMED');
CREATE INDEX idx_bookings_trip_snapshot_departure ON bookings (trip_snapshot_departure DESC);
CREATE INDEX idx_bookings_completed_report ON bookings (completed_at, operator_id)
    WHERE status = 'COMPLETED' AND completed_at IS NOT NULL;

-- Durable local Station redirect graph and trip.station.merged processed-event marker.
-- Both Station ids are logical references to Trip DB; no cross-database FK is allowed.
CREATE TABLE booking_station_redirects (
    duplicate_station_id UUID PRIMARY KEY,
    canonical_station_id UUID NOT NULL,
    source_event_id UUID NOT NULL,
    occurred_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_booking_station_redirects_not_self
        CHECK (duplicate_station_id <> canonical_station_id)
);

CREATE UNIQUE INDEX uq_booking_station_redirects_source_event
    ON booking_station_redirects (source_event_id);
CREATE INDEX idx_booking_station_redirects_canonical
    ON booking_station_redirects (canonical_station_id);

-- Rollout backfill: preserve immutable history while seeding the mutable schedule projection.
UPDATE bookings
SET trip_current_departure = trip_snapshot_departure
WHERE trip_current_departure IS NULL;

CREATE INDEX idx_bookings_trip_current_departure ON bookings (trip_current_departure DESC);

-- Append-only authoritative Booking lifecycle timeline. Application code permits INSERT/read only.
CREATE TABLE booking_status_history (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    booking_id UUID NOT NULL REFERENCES bookings (id) ON DELETE RESTRICT,
    status booking_status NOT NULL,
    occurred_at TIMESTAMPTZ NOT NULL,
    reason_code VARCHAR(100) NULL,
    actor_user_id UUID NULL, -- logical FK to identity.users; intentionally no DB FK
    source VARCHAR(100) NOT NULL
);

CREATE INDEX idx_booking_status_history_booking_occurred_id
    ON booking_status_history (booking_id, occurred_at, id);

COMMENT ON COLUMN bookings.booking_code IS
    'Format VR-yyyyMMdd-XXXXXXXX (8 chars base32 uppercase). Booking/order code for history and backward compatibility; ticket QR uses tickets.ticket_code.';
COMMENT ON COLUMN bookings.passenger_user_id IS
    'Historical field name for the Identity account that created/paid for the Booking; logical FK only.';
COMMENT ON COLUMN bookings.buyer_display_name IS
    'Nullable buyer display snapshot. Legacy rows are filled by the bounded application backfill; migrations never call Identity.';
COMMENT ON COLUMN bookings.total_amount IS
    'IMMUTABLE after INSERT. Snapshot of fare at booking time. Operator fare edits do not affect existing bookings.';
COMMENT ON COLUMN bookings.trip_snapshot_departure IS
    'IMMUTABLE historical departure captured when the Booking is created; schedule events never update it.';
COMMENT ON COLUMN bookings.trip_current_departure IS
    'Mutable current-departure projection, backfilled from trip_snapshot_departure and advanced causally by schedule events.';
COMMENT ON COLUMN bookings.refund_override IS
    'true when refund 100% regardless of cancellation policy (operator-fault scenarios).';
COMMENT ON COLUMN bookings.seat_lock_token IS
    'Original Trip seat lock token returned during checkout. Nullable for legacy rows created before Booking persisted lock metadata.';

-- -----------------------------------------------------------------------------
-- passengers (sub-entity of Booking; 1–5 per booking; operational-only)
-- -----------------------------------------------------------------------------
CREATE TABLE passengers (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    booking_id UUID NOT NULL REFERENCES bookings (id) ON DELETE CASCADE,
    seat_number VARCHAR(20) NULL,
    boarding_status passenger_boarding_status NOT NULL DEFAULT 'PENDING',
    boarded_at TIMESTAMPTZ NULL,
    boarded_at_stop_id UUID NULL,    -- logical FK to trip.stops
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX uq_passengers_booking_seat ON passengers (booking_id, seat_number);
-- Removed redundant `idx_passengers_booking_id` (covered by leading col of uq_passengers_booking_seat).
CREATE INDEX idx_passengers_boarding_status ON passengers (booking_id, boarding_status);

COMMENT ON TABLE passengers IS
    'Operational-only — no PII (no fullName/phone/idNumber). Max 5 per booking enforced by app-layer AND DB trigger (trg_passengers_max_5_per_booking).';

-- -----------------------------------------------------------------------------
-- tickets (1 per seat; proof of travel / QR token)
-- -----------------------------------------------------------------------------
CREATE TABLE tickets (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    booking_id UUID NOT NULL REFERENCES bookings (id) ON DELETE CASCADE,
    passenger_id UUID NOT NULL REFERENCES passengers (id) ON DELETE CASCADE,
    ticket_code VARCHAR(30) NOT NULL,    -- "VT-yyyyMMdd-XXXXXXXX"
    seat_number VARCHAR(20) NOT NULL,
    status ticket_status NOT NULL DEFAULT 'PENDING_PAYMENT',
    fare_amount BIGINT NOT NULL,
    discount_amount BIGINT NOT NULL DEFAULT 0,
    paid_amount BIGINT NOT NULL,
    issued_at TIMESTAMPTZ NULL,
    used_at TIMESTAMPTZ NULL,
    cancelled_at TIMESTAMPTZ NULL,
    refunded_at TIMESTAMPTZ NULL,
    expired_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_tickets_amounts_non_negative
        CHECK (fare_amount >= 0 AND discount_amount >= 0 AND paid_amount >= 0),
    CONSTRAINT chk_tickets_paid_le_fare
        CHECK (paid_amount <= fare_amount)
);

CREATE UNIQUE INDEX uq_tickets_ticket_code ON tickets (ticket_code);
CREATE UNIQUE INDEX uq_tickets_passenger_id ON tickets (passenger_id);
CREATE INDEX idx_tickets_booking_status ON tickets (booking_id, status);
CREATE INDEX idx_tickets_seat_number ON tickets (seat_number);

COMMENT ON TABLE tickets IS
    'One ticket per booked seat. Booking is the order; Ticket is the per-seat proof of travel and QR identity.';
COMMENT ON COLUMN tickets.ticket_code IS
    'Format VT-yyyyMMdd-XXXXXXXX (8 chars uppercase Crockford-style base32). New boarding QR encodes this string.';

-- -----------------------------------------------------------------------------
-- booking_pending_actions
-- -----------------------------------------------------------------------------
CREATE TABLE booking_pending_actions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    booking_id UUID NOT NULL REFERENCES bookings (id) ON DELETE CASCADE,
    reason booking_pending_action_reason NOT NULL,
    severity booking_pending_action_severity NULL,
    deadline TIMESTAMPTZ NOT NULL,
    resolved_at TIMESTAMPTZ NULL,
    resolved_action booking_pending_action_resolved NULL,
    metadata JSONB NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Partial unique: only 1 active per booking
CREATE UNIQUE INDEX uq_booking_pending_actions_active_per_booking
    ON booking_pending_actions (booking_id)
    WHERE resolved_at IS NULL;
CREATE INDEX idx_booking_pending_actions_deadline_unresolved
    ON booking_pending_actions (deadline) WHERE resolved_at IS NULL;
CREATE INDEX idx_booking_pending_actions_reason ON booking_pending_actions (reason);

COMMENT ON INDEX uq_booking_pending_actions_active_per_booking IS
    'Enforces "only 1 active pending action per booking" rule (Section 8 conventions).';

-- -----------------------------------------------------------------------------
-- booking_transfers (Vehicle Substitution — 1 record per Passenger)
-- -----------------------------------------------------------------------------
CREATE TABLE booking_transfers (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    booking_id UUID NOT NULL REFERENCES bookings (id) ON DELETE RESTRICT,
    passenger_id UUID NOT NULL REFERENCES passengers (id) ON DELETE RESTRICT,
    ticket_id UUID NULL REFERENCES tickets (id) ON DELETE RESTRICT,
    original_trip_id UUID NOT NULL,
    new_trip_id UUID NOT NULL,
    original_seat_number VARCHAR(20) NULL,
    new_seat_number VARCHAR(20) NULL,
    original_seat_type VARCHAR(30) NULL,
    new_seat_type VARCHAR(30) NULL,
    is_seat_downgrade BOOLEAN NOT NULL DEFAULT FALSE,
    confirmation_status booking_transfer_confirmation_status NOT NULL,
    confirmed_at TIMESTAMPTZ NULL,
    confirmed_by_user_id UUID NULL, -- logical FK -> identity.users.id
    transferred_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    transferred_by_user_id UUID NOT NULL,
    note TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_booking_transfers_booking_id ON booking_transfers (booking_id);
CREATE INDEX idx_booking_transfers_passenger_id ON booking_transfers (passenger_id);
CREATE INDEX idx_booking_transfers_ticket_id ON booking_transfers (ticket_id);
CREATE INDEX idx_booking_transfers_original_trip_id ON booking_transfers (original_trip_id);
CREATE INDEX idx_booking_transfers_new_trip_id ON booking_transfers (new_trip_id);
CREATE INDEX idx_booking_transfers_pending_confirm_transferred_at
    ON booking_transfers (transferred_at)
    WHERE confirmation_status = 'PENDING_CONFIRM';
CREATE UNIQUE INDEX uq_booking_transfers_passenger_trip_pair
    ON booking_transfers (passenger_id, original_trip_id, new_trip_id);

COMMENT ON COLUMN booking_transfers.confirmed_by_user_id IS
    'Nullable logical FK to identity.users.id; deliberately no cross-database constraint.';

-- -----------------------------------------------------------------------------
-- booking_stats (denormalized counter table for reporting)
-- -----------------------------------------------------------------------------
CREATE TABLE booking_stats (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id UUID NOT NULL,
    operator_name TEXT NULL,
    stat_date DATE NOT NULL,
    trip_id UUID NULL,    -- nullable: per-operator-per-day aggregates have trip_id NULL
    total_bookings INT NOT NULL DEFAULT 0,
    total_confirmed INT NOT NULL DEFAULT 0,
    total_cancelled INT NOT NULL DEFAULT 0,
    total_no_show INT NOT NULL DEFAULT 0,
    total_no_show_passengers INT NOT NULL DEFAULT 0,
    total_completed INT NOT NULL DEFAULT 0,
    total_revenue BIGINT NOT NULL DEFAULT 0,
    total_refunded BIGINT NOT NULL DEFAULT 0,
    total_seats_booked INT NOT NULL DEFAULT 0,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX uq_booking_stats_operator_date_trip
    ON booking_stats (operator_id, stat_date, COALESCE(trip_id, '00000000-0000-0000-0000-000000000000'::UUID));
CREATE INDEX idx_booking_stats_operator_date ON booking_stats (operator_id, stat_date DESC);

COMMENT ON TABLE booking_stats IS
    'UPSERT-driven counter table from event consumers. (operator_id, stat_date, trip_id) unique key.';

CREATE TABLE booking_stats_processed_events (
    event_type VARCHAR(100) NOT NULL,
    booking_id UUID NOT NULL,
    processed_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT pk_booking_stats_processed_events PRIMARY KEY (event_type, booking_id)
);

COMMENT ON TABLE booking_stats_processed_events IS
    'Durable de-dupe marker for BookingStats lifecycle event consumers. No cross-service FK.';

-- -----------------------------------------------------------------------------
-- platform_booking_stats (Day 42 exact-range earned projection)
-- -----------------------------------------------------------------------------
CREATE TABLE platform_booking_stats (
    booking_id UUID PRIMARY KEY REFERENCES bookings(id) ON DELETE CASCADE,
    operator_id UUID NOT NULL,
    completed_at TIMESTAMPTZ NOT NULL,
    booking_revenue_vnd BIGINT NOT NULL,
    projected_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT ck_platform_booking_stats_revenue_non_negative
        CHECK (booking_revenue_vnd >= 0)
);

CREATE INDEX idx_platform_booking_stats_completed_operator
    ON platform_booking_stats (completed_at, operator_id);

CREATE OR REPLACE FUNCTION sync_platform_booking_stats()
RETURNS TRIGGER AS $$
DECLARE
    source_id UUID := CASE WHEN TG_OP = 'DELETE' THEN OLD.id ELSE NEW.id END;
BEGIN
    IF TG_OP <> 'DELETE'
       AND NEW.status = 'COMPLETED'::public.booking_status
       AND NEW.completed_at IS NOT NULL THEN
        INSERT INTO platform_booking_stats (
            booking_id, operator_id, completed_at, booking_revenue_vnd, projected_at
        )
        VALUES (NEW.id, NEW.operator_id, NEW.completed_at, NEW.total_amount, now())
        ON CONFLICT (booking_id) DO UPDATE SET
            operator_id = EXCLUDED.operator_id,
            completed_at = EXCLUDED.completed_at,
            booking_revenue_vnd = EXCLUDED.booking_revenue_vnd,
            projected_at = now();
    ELSE
        DELETE FROM platform_booking_stats WHERE booking_id = source_id;
    END IF;

    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_sync_platform_booking_stats
    AFTER INSERT OR UPDATE OR DELETE ON bookings
    FOR EACH ROW EXECUTE FUNCTION sync_platform_booking_stats();

CREATE OR REPLACE FUNCTION rebuild_platform_booking_stats()
RETURNS VOID AS $$
BEGIN
    INSERT INTO platform_booking_stats (
        booking_id, operator_id, completed_at, booking_revenue_vnd, projected_at
    )
    SELECT id, operator_id, completed_at, total_amount, now()
    FROM bookings
    WHERE status = 'COMPLETED'::public.booking_status
      AND completed_at IS NOT NULL
    ON CONFLICT (booking_id) DO UPDATE SET
        operator_id = EXCLUDED.operator_id,
        completed_at = EXCLUDED.completed_at,
        booking_revenue_vnd = EXCLUDED.booking_revenue_vnd,
        projected_at = now();

    DELETE FROM platform_booking_stats projection
    WHERE NOT EXISTS (
        SELECT 1
        FROM bookings source
        WHERE source.id = projection.booking_id
          AND source.status = 'COMPLETED'::public.booking_status
          AND source.completed_at IS NOT NULL
    );
END;
$$ LANGUAGE plpgsql;

SELECT rebuild_platform_booking_stats();

-- -----------------------------------------------------------------------------
-- vouchers (System Admin + Operator self-create — platform-wide + operator-owned)
-- -----------------------------------------------------------------------------
CREATE TABLE vouchers (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    code VARCHAR(50) NOT NULL,
    name VARCHAR(120) NOT NULL,        -- human-readable label (e.g. "Summer Sale 20%", "Tết Discount 50k")
    type voucher_type NOT NULL,
    value BIGINT NOT NULL,            -- percent (1-100) for PERCENT_OFF, VND for FIXED_AMOUNT
    min_order_amount BIGINT NOT NULL DEFAULT 0,
    max_discount_amount BIGINT NULL,
    total_usage_limit INT NULL,
    per_user_limit INT NULL,
    valid_from TIMESTAMPTZ NOT NULL,
    valid_until TIMESTAMPTZ NOT NULL,
    new_user_only BOOLEAN NOT NULL DEFAULT FALSE,
    applicable_payment_methods TEXT[] NULL,     -- NULL/empty = all payment methods
    applicable_services TEXT[] NOT NULL DEFAULT ARRAY['BOOKING']::TEXT[], -- BOOKING, PARCEL
    applicable_operator_ids UUID[] NULL,    -- NULL = applies to all operators (admin VIETRIDE_FUNDED only; operator-owned forced to self)
    applicable_route_ids UUID[] NULL,       -- NULL = applies to all routes
    funding_type voucher_funding_type NOT NULL,
    owner_operator_id UUID NULL,            -- NULL = platform admin voucher; NOT NULL = operator self-created (logical FK identity.operators, tenant-scoped)
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_by_user_id UUID NOT NULL,    -- logical FK: SYSTEM_ADMIN (platform) or OPERATOR_ADMIN (operator-owned)
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    deleted_at TIMESTAMPTZ NULL,         -- soft-delete (ADR 0003)
    CONSTRAINT chk_vouchers_value_positive CHECK (value > 0),
    CONSTRAINT chk_vouchers_validity_window CHECK (valid_until > valid_from),
    CONSTRAINT chk_vouchers_min_order_non_negative CHECK (min_order_amount >= 0),
    CONSTRAINT chk_vouchers_operator_owned_funding CHECK (owner_operator_id IS NULL OR funding_type = 'OPERATOR_FUNDED'::voucher_funding_type),
    CONSTRAINT chk_vouchers_applicable_services_valid CHECK (applicable_services <@ ARRAY['BOOKING', 'PARCEL']::text[] AND cardinality(applicable_services) > 0),
    CONSTRAINT chk_vouchers_applicable_payment_methods_valid CHECK (applicable_payment_methods IS NULL OR applicable_payment_methods <@ ARRAY['WALLET', 'VNPAY']::text[])
);

CREATE UNIQUE INDEX uq_vouchers_code ON vouchers (code) WHERE deleted_at IS NULL;
CREATE INDEX idx_vouchers_active_validity
    ON vouchers (valid_until) WHERE is_active = TRUE;
CREATE INDEX idx_vouchers_owner_operator ON vouchers (owner_operator_id)
    WHERE owner_operator_id IS NOT NULL AND deleted_at IS NULL;
CREATE INDEX idx_vouchers_new_user_only ON vouchers (new_user_only);

COMMENT ON COLUMN vouchers.owner_operator_id IS
    'NULL = platform admin voucher (owner_operator_id IS NULL); NOT NULL = operator self-created voucher scoped to that operator (logical FK identity.operators). Operator-owned vouchers are always OPERATOR_FUNDED (enforced by chk_vouchers_operator_owned_funding).';
COMMENT ON COLUMN vouchers.created_by_user_id IS
    'Logical FK identity.users. SYSTEM_ADMIN for platform vouchers; OPERATOR_ADMIN for operator-owned vouchers.';
COMMENT ON COLUMN vouchers.deleted_at IS
    'Soft-delete timestamp per ADR 0003. Code can be reused after soft-delete (partial unique index uq_vouchers_code WHERE deleted_at IS NULL).';

-- -----------------------------------------------------------------------------
-- voucher_usages (DELETE row when booking CANCELLED/REFUNDED per spec)
-- -----------------------------------------------------------------------------
CREATE TABLE voucher_usages (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    voucher_id UUID NOT NULL REFERENCES vouchers (id) ON DELETE RESTRICT,
    user_id UUID NOT NULL,    -- logical FK
    booking_id UUID NULL REFERENCES bookings (id) ON DELETE CASCADE,
    reference_type VARCHAR(20) NOT NULL DEFAULT 'BOOKING', -- BOOKING or PARCEL
    reference_id UUID NOT NULL,
    booking_group_id UUID NULL,    -- for round-trip limit count
    discount_amount BIGINT NOT NULL,
    funded_by voucher_funding_type NOT NULL,    -- snapshot at apply time
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_voucher_usages_voucher_user ON voucher_usages (voucher_id, user_id);
CREATE INDEX idx_voucher_usages_voucher_group ON voucher_usages (voucher_id, booking_group_id)
    WHERE booking_group_id IS NOT NULL;
CREATE INDEX idx_voucher_usages_booking_id ON voucher_usages (booking_id)
    WHERE booking_id IS NOT NULL;
CREATE INDEX idx_voucher_usages_reference ON voucher_usages (reference_type, reference_id);

COMMENT ON COLUMN voucher_usages.funded_by IS
    'Snapshot of voucher.funding_type at apply time — used for settlement reconcile if voucher changes later.';

-- -----------------------------------------------------------------------------
-- campaigns + campaign_vouchers
-- -----------------------------------------------------------------------------
CREATE TABLE campaigns (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(120) NOT NULL,
    description TEXT NULL,
    owner_operator_id UUID NULL,
    valid_from TIMESTAMPTZ NOT NULL,
    valid_until TIMESTAMPTZ NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_by_user_id UUID NOT NULL,
    deleted_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_campaigns_validity_window CHECK (valid_until > valid_from)
);

CREATE INDEX idx_campaigns_active_validity ON campaigns (is_active, valid_until);
CREATE INDEX idx_campaigns_owner_operator ON campaigns (owner_operator_id)
    WHERE owner_operator_id IS NOT NULL AND deleted_at IS NULL;

CREATE TABLE campaign_vouchers (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    campaign_id UUID NOT NULL REFERENCES campaigns (id) ON DELETE CASCADE,
    voucher_id UUID NOT NULL REFERENCES vouchers (id) ON DELETE CASCADE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX uq_campaign_vouchers_campaign_voucher
    ON campaign_vouchers (campaign_id, voucher_id);
CREATE INDEX idx_campaign_vouchers_voucher_id ON campaign_vouchers (voucher_id);

-- -----------------------------------------------------------------------------
-- operator_voucher_consents
-- -----------------------------------------------------------------------------
CREATE TABLE operator_voucher_consents (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id UUID NOT NULL,    -- logical FK
    voucher_id UUID NOT NULL REFERENCES vouchers (id) ON DELETE CASCADE,
    status operator_voucher_consent_status NOT NULL DEFAULT 'PENDING',
    requested_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    responded_at TIMESTAMPTZ NULL,
    responded_by_user_id UUID NULL,    -- logical FK OPERATOR_ADMIN
    reject_reason TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX uq_operator_voucher_consents_operator_voucher
    ON operator_voucher_consents (operator_id, voucher_id);
CREATE INDEX idx_operator_voucher_consents_status ON operator_voucher_consents (status);
CREATE INDEX idx_operator_voucher_consents_operator_status
    ON operator_voucher_consents (operator_id, status);
-- Voucher-scoped query (e.g. Admin "consent status across operators for voucher X").
-- UNIQUE composite (operator_id, voucher_id) has leading operator_id and does NOT cover voucher_id-only filter.
CREATE INDEX idx_operator_voucher_consents_voucher_id
    ON operator_voucher_consents (voucher_id);

-- -----------------------------------------------------------------------------
-- booking_shuttle_intents
-- -----------------------------------------------------------------------------
CREATE TABLE booking_shuttle_intents (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    booking_id UUID NOT NULL REFERENCES bookings (id) ON DELETE CASCADE,
    direction VARCHAR(30) NOT NULL DEFAULT 'INBOUND_TO_STATION',
    pickup_address TEXT NOT NULL,
    pickup_latitude DECIMAL(10,7) NOT NULL,
    pickup_longitude DECIMAL(10,7) NOT NULL,
    road_distance_meters INT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    cancelled_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_booking_shuttle_intents_latitude CHECK (pickup_latitude BETWEEN -90 AND 90),
    CONSTRAINT chk_booking_shuttle_intents_longitude CHECK (pickup_longitude BETWEEN -180 AND 180),
    CONSTRAINT chk_booking_shuttle_intents_direction CHECK (direction IN ('INBOUND_TO_STATION', 'OUTBOUND_FROM_STATION')),
    CONSTRAINT chk_booking_shuttle_intents_road_distance CHECK (road_distance_meters IS NULL OR road_distance_meters >= 0)
);

CREATE UNIQUE INDEX uq_booking_shuttle_intents_booking_direction
    ON booking_shuttle_intents (booking_id, direction) WHERE is_active = TRUE;

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
BEGIN NEW.updated_at = now(); RETURN NEW; END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_bookings_updated_at BEFORE UPDATE ON bookings
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_passengers_updated_at BEFORE UPDATE ON passengers
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_tickets_updated_at BEFORE UPDATE ON tickets
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_booking_pending_actions_updated_at BEFORE UPDATE ON booking_pending_actions
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_booking_stats_updated_at BEFORE UPDATE ON booking_stats
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_vouchers_updated_at BEFORE UPDATE ON vouchers
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_operator_voucher_consents_updated_at BEFORE UPDATE ON operator_voucher_consents
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_booking_shuttle_intents_updated_at BEFORE UPDATE ON booking_shuttle_intents
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();

-- -----------------------------------------------------------------------------
-- Trigger: enforce max 5 passengers per booking (v6 Section 6.1 line 1568)
-- -----------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION trg_check_passenger_max_per_booking()
RETURNS TRIGGER AS $$
BEGIN
    IF (SELECT COUNT(*) FROM passengers WHERE booking_id = NEW.booking_id) >= 5 THEN
        RAISE EXCEPTION 'Booking % already has 5 passengers (max). v6 Section 6.1 hard limit.', NEW.booking_id
            USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_passengers_max_5_per_booking
    BEFORE INSERT ON passengers
    FOR EACH ROW EXECUTE FUNCTION trg_check_passenger_max_per_booking();

COMMENT ON FUNCTION trg_check_passenger_max_per_booking() IS
    'Enforces v6 Section 6.1 hard limit: 1 booking ≤ 5 Passenger records. App-layer also validates for better UX (returns BOOKING_MAX_SEATS_EXCEEDED before DB trip).';

-- =============================================================================
-- Hangfire schema (auto-created): seat release on VNPay timeout,
-- schedule-change auto-accept, PENDING_SEAT_ASSIGNMENT escalation.
-- =============================================================================
