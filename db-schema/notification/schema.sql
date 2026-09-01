-- =============================================================================
-- VietRide :: Notification Service :: PostgreSQL 16 schema
-- Database: vietride_notification
-- Schema: vietride_notification
-- Framework: NestJS + Prisma ORM
-- =============================================================================
-- Notification Service ONLY CONSUMES events from RabbitMQ — does NOT publish.
-- => No OutboxEvent table (per v6 Section 8).
-- BullMQ (Redis) handles FCM retry with exponential backoff.
-- =============================================================================

CREATE EXTENSION IF NOT EXISTS "pgcrypto";

CREATE SCHEMA IF NOT EXISTS vietride_notification;
SET search_path TO vietride_notification, public;

-- =============================================================================
-- ENUMS
-- =============================================================================

CREATE TYPE notification_type AS ENUM (
    'BOOKING_CONFIRMED',
    'BOOKING_CREATED',
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
    'STOP_DISABLED',
    'VEHICLE_SUBSTITUTED',
    'VEHICLE_SUBSTITUTION_SEAT_SHORTAGE',
    'BOOKING_TRANSFER_ESCALATED',
    'VEHICLE_SWAPPED',
    'PARCEL_RESERVED',
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
    'PARCEL_REVIEW_APPROVED',
    'PARCEL_FINAL_PAYMENT_REQUIRED',
    'PARCEL_SETTLEMENT_RECOVERED',
    'VOUCHER_CONSENT_REQUESTED',
    'VOUCHER_CONSENT_ACCEPTED',
    'VOUCHER_CONSENT_REJECTED',
    'SUBSCRIPTION_LIMIT_EXCEEDED',
    'SUBSCRIPTION_USAGE_WARNING',
    'SUBSCRIPTION_TRIAL_EXPIRING',
    'SUBSCRIPTION_EXPIRED',
    'SUBSCRIPTION_APPROVED',
    'SUBSCRIPTION_PAYMENT_PENDING_WARN',
    'SUBSCRIPTION_PAYMENT_AUTO_REVERTED',
    'INVOICE_ISSUED',
    'DRIVER_SCHEDULE_EDITED',
    'PAYOUT_PROCESSED',
    'PAYOUT_FAILED',
    'OPERATOR_APPROVED',
    'OPERATOR_SUSPENDED',
    'OPERATOR_REGISTRATION_SUBMITTED',
    'TRIP_ASSIGNED',
    'TRIP_ASSIGNMENT_REMOVED',
    'OPERATOR_ANNOUNCEMENT',
    'SHUTTLE_ASSIGNED',
    'SHUTTLE_CANCELLED',
    'SHUTTLE_PICKED_UP',
    'SHUTTLE_DELIVERED',
    'SHUTTLE_NO_SHOW',
    'SHUTTLE_COMPLETED',
    'SHUTTLE_UNFULFILLED',
    'SHUTTLE_WARNING',
    'SHUTTLE_STARTED',
    'SHUTTLE_REASSIGNED',
    'SHUTTLE_UNASSIGNED',
    'DRIVER_STOP_DEPARTED_WITH_PENDING',
    'ROUTE_CHANGE_PROPOSAL_CREATED',
    'ROUTE_CHANGE_PROPOSAL_APPROVED',
    'ROUTE_CHANGE_PROPOSAL_REJECTED',
    'ROUTE_CHANGE_PROPOSAL_SUPERSEDED',
    'ROUTE_CHANGE_PROPOSAL_EXPIRED'
);

CREATE TYPE notification_delivery_status AS ENUM (
    'PENDING', 'SENT', 'FAILED', 'RETRYING', 'VALIDATED'
);

CREATE TYPE email_template_key AS ENUM (
    'AUTH_OTP',
    'SET_INITIAL_PASSWORD',
    'PARCEL_DELIVERY_LINK',
    'OPERATOR_SUBSCRIPTION_NOTICE',
    'INVOICE_NOTICE'
);

CREATE TYPE email_delivery_status AS ENUM (
    'PENDING', 'SENDING', 'SENT', 'FAILED', 'RETRYING'
);

CREATE TYPE device_platform AS ENUM ('IOS', 'ANDROID', 'WEB');

-- =============================================================================
-- TABLES
-- =============================================================================

-- -----------------------------------------------------------------------------
-- notifications (in-app history per user)
-- -----------------------------------------------------------------------------
CREATE TABLE notifications (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL,    -- logical FK identity.users
    type notification_type NOT NULL,
    title VARCHAR(255) NOT NULL,
    body TEXT NOT NULL,
    data JSONB NULL,           -- payload (bookingId, tripId, etc.) for app routing
    dedupe_key VARCHAR(200) NULL,
    read_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_notifications_user_id_created_at
    ON notifications (user_id, created_at DESC);
CREATE INDEX idx_notifications_user_id_unread
    ON notifications (user_id, created_at DESC) WHERE read_at IS NULL;
CREATE UNIQUE INDEX notifications_dedupe_key_key
    ON notifications (dedupe_key);
CREATE INDEX idx_notifications_type_created_at
    ON notifications (type, created_at DESC);

-- -----------------------------------------------------------------------------
-- notification_deliveries (FCM push attempt audit)
-- -----------------------------------------------------------------------------
CREATE TABLE notification_deliveries (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    notification_id UUID NOT NULL REFERENCES notifications (id) ON DELETE CASCADE,
    fcm_token VARCHAR(500) NOT NULL,
    platform device_platform NOT NULL,
    status notification_delivery_status NOT NULL DEFAULT 'PENDING',
    retry_count INT NOT NULL DEFAULT 0,
    last_error TEXT NULL,
    provider_message_id VARCHAR(255) NULL,
    sent_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_notification_deliveries_notification_id
    ON notification_deliveries (notification_id);
CREATE INDEX idx_notification_deliveries_status_created_at
    ON notification_deliveries (status, created_at);
CREATE UNIQUE INDEX notification_deliveries_notification_id_fcm_token_key
    ON notification_deliveries (notification_id, fcm_token);

-- -----------------------------------------------------------------------------
-- email_deliveries (transactional email attempt audit)
-- -----------------------------------------------------------------------------
CREATE TABLE email_deliveries (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    notification_id UUID NULL REFERENCES notifications (id) ON DELETE SET NULL,
    dedupe_key VARCHAR(200) NULL,
    to_email VARCHAR(320) NOT NULL,
    template_key email_template_key NOT NULL,
    subject VARCHAR(255) NOT NULL,
    sanitized_data JSONB NULL,
    status email_delivery_status NOT NULL DEFAULT 'PENDING',
    retry_count INT NOT NULL DEFAULT 0,
    last_error TEXT NULL,
    provider_message_id VARCHAR(255) NULL,
    sent_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_email_deliveries_notification_id
    ON email_deliveries (notification_id);
CREATE INDEX idx_email_deliveries_status_created_at
    ON email_deliveries (status, created_at);
CREATE INDEX idx_email_deliveries_to_email_created_at
    ON email_deliveries (to_email, created_at DESC);
CREATE UNIQUE INDEX email_deliveries_dedupe_key_key
    ON email_deliveries (dedupe_key);

-- =============================================================================
-- TRIGGERS
-- =============================================================================

CREATE TABLE processed_messages (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    consumer_name VARCHAR(200) NOT NULL,
    message_id VARCHAR(100) NOT NULL,
    routing_key VARCHAR(200) NOT NULL,
    payload_hash CHAR(64) NOT NULL,
    processed_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX uq_processed_messages_consumer_message
    ON processed_messages (consumer_name, message_id);
CREATE INDEX idx_processed_messages_processed_at
    ON processed_messages (processed_at);

CREATE OR REPLACE FUNCTION trg_set_updated_at()
RETURNS TRIGGER AS $$
BEGIN NEW.updated_at = now(); RETURN NEW; END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_notification_deliveries_updated_at
    BEFORE UPDATE ON notification_deliveries
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();

CREATE TRIGGER trg_email_deliveries_updated_at
    BEFORE UPDATE ON email_deliveries
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();

-- =============================================================================
-- NO OutboxEvent table — Notification Service only consumes, does NOT publish.
-- BullMQ `fcm-push` queue (Redis) handles retry with exponential backoff:
--   5s → 30s → 5m → DLQ.
-- =============================================================================
