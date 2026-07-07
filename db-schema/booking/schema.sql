-- =============================================================================
-- VietRide :: Booking Service :: PostgreSQL 16 schema
-- Database: vietride_booking
-- Framework: .NET Core 8 + EF Core 8
-- =============================================================================

CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- =============================================================================
-- ENUMS
-- =============================================================================

CREATE TYPE booking_status AS ENUM (
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
    -- trip context (logical FKs)
    trip_id UUID NOT NULL,
    operator_id UUID NOT NULL,
    seat_lock_token UUID NULL,
    -- pickup/dropoff: 4 columns mutually exclusive
    pickup_station_id UUID NULL,
    pickup_stop_id UUID NULL,
    dropoff_station_id UUID NULL,
    dropoff_stop_id UUID NULL,
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

COMMENT ON COLUMN bookings.booking_code IS
    'Format VR-yyyyMMdd-XXXXXXXX (8 chars base32 uppercase). Booking/order code for history and backward compatibility; ticket QR uses tickets.ticket_code.';
COMMENT ON COLUMN bookings.total_amount IS
    'IMMUTABLE after INSERT. Snapshot of fare at booking time. Operator fare edits do not affect existing bookings.';
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
    seat_number VARCHAR(20) NOT NULL,
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
    original_seat_number VARCHAR(20) NOT NULL,
    new_seat_number VARCHAR(20) NULL,
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
    CONSTRAINT chk_vouchers_operator_owned_funding CHECK (owner_operator_id IS NULL OR funding_type = 'OPERATOR_FUNDED'::voucher_funding_type)
);

CREATE UNIQUE INDEX uq_vouchers_code ON vouchers (code) WHERE deleted_at IS NULL;
CREATE INDEX idx_vouchers_active_validity
    ON vouchers (valid_until) WHERE is_active = TRUE;
CREATE INDEX idx_vouchers_owner_operator ON vouchers (owner_operator_id)
    WHERE owner_operator_id IS NOT NULL AND deleted_at IS NULL;

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
    booking_id UUID NOT NULL REFERENCES bookings (id) ON DELETE CASCADE,
    booking_group_id UUID NULL,    -- for round-trip limit count
    discount_amount BIGINT NOT NULL,
    funded_by voucher_funding_type NOT NULL,    -- snapshot at apply time
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_voucher_usages_voucher_user ON voucher_usages (voucher_id, user_id);
CREATE INDEX idx_voucher_usages_voucher_group ON voucher_usages (voucher_id, booking_group_id)
    WHERE booking_group_id IS NOT NULL;
CREATE INDEX idx_voucher_usages_booking_id ON voucher_usages (booking_id);

COMMENT ON COLUMN voucher_usages.funded_by IS
    'Snapshot of voucher.funding_type at apply time — used for settlement reconcile if voucher changes later.';

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
