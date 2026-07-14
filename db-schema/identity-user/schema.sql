-- =============================================================================
-- VietRide :: Identity & User Service :: PostgreSQL 16 schema
-- Database: vietride_identity
-- Framework: .NET Core 8 + EF Core 8
-- =============================================================================
-- Source of truth: SU26SE101_VIETRIDE_technical_context_v7.md (Section 5, 8)
-- Conventions:
--   - snake_case tables + columns (EF Core naming policy maps to camelCase)
--   - UUID PK with gen_random_uuid() default
--   - TIMESTAMPTZ for timestamps (UTC storage)
--   - BIGINT for VND money
--   - Cross-service FKs are LOGICAL ONLY (no REFERENCES) -- see _global/cross-service-references.md
-- =============================================================================

CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- =============================================================================
-- ENUMS
-- =============================================================================

CREATE TYPE user_role AS ENUM (
    'PASSENGER', 'DRIVER', 'ASSISTANT',
    'OPERATOR_STAFF', 'OPERATOR_ADMIN', 'SYSTEM_ADMIN'
);

CREATE TYPE user_status AS ENUM (
    'PENDING_EMAIL_VERIFICATION',
    'PENDING_INITIAL_PASSWORD',
    'ACTIVE',
    'LOCKED',
    'DELETED'
);

CREATE TYPE operator_registration_status AS ENUM (
    'PENDING', 'APPROVED', 'REJECTED', 'SUSPENDED'
);

CREATE TYPE email_verification_purpose AS ENUM (
    'REGISTRATION', 'PASSWORD_RESET', 'SET_INITIAL_PASSWORD'
);

CREATE TYPE oauth_provider AS ENUM ('GOOGLE');

CREATE TYPE refresh_token_revoke_reason AS ENUM (
    'NORMAL_ROTATION', 'REUSE_DETECTED', 'USER_LOGOUT',
    'ADMIN_REVOKE', 'PASSWORD_RESET'
);

CREATE TYPE device_platform AS ENUM ('IOS', 'ANDROID', 'WEB');

CREATE TYPE activity_log_action AS ENUM (
    'LOGIN', 'LOGOUT', 'BOOK_TICKET', 'CANCEL_TICKET',
    'UPDATE_PROFILE', 'CHANGE_PASSWORD', 'COMPLETE_PROFILE',
    'CREATE_OPERATOR', 'APPROVE_OPERATOR', 'REJECT_OPERATOR',
    'LOCK_USER', 'UNLOCK_USER',
    'VEHICLE_SUBSTITUTION_TRIGGERED',
    'DRIVER_SCHEDULE_EDIT', 'VEHICLE_SWAP',
    'TRIP_COMPLETED_MANUAL',
    'PARCEL_UNLOAD_OVERRIDE', 'PARCEL_DELIVERY_RESEND',
    'PARCEL_MANUAL_CONFIRM',
    -- Operator Wallet & Trip Settlement (4.6)
    'TRIP_SETTLEMENT_MANUAL',
    'OPERATOR_WALLET_ADJUSTMENT',
    -- Initial password flow (Day 5)
    'SET_INITIAL_PASSWORD', 'RESEND_INITIAL_PASSWORD'
    -- v2: 'BANK_ACCOUNT_UPDATED' (removed from v1 — bank withdrawal deferred)
    -- v2: 'OPERATOR_WITHDRAWAL_REQUESTED' / 'OPERATOR_WITHDRAWAL_PROCESSED'
);

CREATE TYPE subscription_status AS ENUM (
    'PENDING_APPROVAL', 'ACTIVE', 'EXPIRED', 'CANCELLED', 'PENDING_PAYMENT'
);

CREATE TYPE subscription_payment_method AS ENUM ('VNPAY', 'WALLET');

CREATE TYPE subscription_billing_period AS ENUM ('MONTHLY', 'YEARLY');

CREATE TYPE subscription_upgrade_attempt_status AS ENUM (
    'INITIATED', 'PAYMENT_PENDING', 'SUCCEEDED', 'EXPIRED', 'FAILED'
);

-- =============================================================================
-- TABLES
-- =============================================================================

-- -----------------------------------------------------------------------------
-- operators
-- -----------------------------------------------------------------------------
CREATE TABLE operators (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(255) NOT NULL,
    business_registration_number VARCHAR(50) NOT NULL,
    tax_code VARCHAR(50) NOT NULL,
    contact_email VARCHAR(255) NOT NULL,
    contact_phone VARCHAR(20) NOT NULL,
    logo_url TEXT NULL,
    address_street VARCHAR(255) NULL,
    address_ward VARCHAR(100) NULL,
    address_district VARCHAR(100) NULL,
    address_province VARCHAR(100) NULL,
    representative_name VARCHAR(255) NULL,
    representative_phone VARCHAR(20) NULL,
    registration_status operator_registration_status NOT NULL DEFAULT 'PENDING',
    approved_at TIMESTAMPTZ NULL,
    approved_by_user_id UUID NULL,
    rejected_at TIMESTAMPTZ NULL,
    rejected_by_user_id UUID NULL,
    reject_reason TEXT NULL,
    suspended_at TIMESTAMPTZ NULL,
    suspend_reason TEXT NULL,
    -- Cancellation policy (configurable per operator) — JSONB array
    -- Format: [{ "hoursBeforeDeparture": int, "feePercent": int }] sorted ASC
    cancellation_policy JSONB NULL,
    -- Parcel no-show policy — default {noShowFeePercent: 0, additionalPaymentTimeoutMinutes: 30}
    parcel_no_show_policy JSONB NULL,
    -- Luggage policy — default {defaultLuggageKgPerSeat: 10}
    luggage_policy JSONB NULL,
    -- Bank account info (required before first payout)
    bank_account_name VARCHAR(100) NULL,
    bank_account_number VARCHAR(20) NULL,
    bank_name VARCHAR(200) NULL,
    -- Soft delete
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    deleted_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX uq_operators_business_reg_number
    ON operators (business_registration_number)
    WHERE deleted_at IS NULL;
CREATE UNIQUE INDEX uq_operators_tax_code
    ON operators (tax_code)
    WHERE deleted_at IS NULL;
CREATE INDEX idx_operators_registration_status ON operators (registration_status);
CREATE INDEX idx_operators_is_active ON operators (is_active);

COMMENT ON COLUMN operators.cancellation_policy IS
    'JSONB array of {hoursBeforeDeparture, feePercent}; sorted ascending. NULL = no policy configured.';
COMMENT ON COLUMN operators.parcel_no_show_policy IS
    'JSONB {noShowFeePercent, additionalPaymentTimeoutMinutes}. NULL defaults to {0, 30}.';
COMMENT ON COLUMN operators.luggage_policy IS
    'JSONB {defaultLuggageKgPerSeat}. NULL defaults to {10}.';

-- -----------------------------------------------------------------------------
-- users
-- -----------------------------------------------------------------------------
CREATE TABLE users (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    email VARCHAR(255) NOT NULL,
    phone VARCHAR(20) NULL,         -- E.164 VN format e.g. +84901234567
    password_hash VARCHAR(255) NULL, -- nullable for Google-only accounts
    display_name VARCHAR(255) NOT NULL,
    avatar_url TEXT NULL,
    role user_role NOT NULL,
    status user_status NOT NULL DEFAULT 'PENDING_EMAIL_VERIFICATION',
    operator_id UUID NULL REFERENCES operators (id) ON DELETE RESTRICT,
    -- Account lockout tracking
    failed_login_attempts INT NOT NULL DEFAULT 0,
    last_failed_login_at TIMESTAMPTZ NULL,
    last_login_at TIMESTAMPTZ NULL,
    -- Audit
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    deleted_at TIMESTAMPTZ NULL,
    -- Constraints
    CONSTRAINT chk_users_phone_format
        CHECK (phone IS NULL OR phone ~ '^\+84[0-9]{9,10}$'),
    CONSTRAINT chk_users_operator_role
        CHECK (
            (role IN ('DRIVER', 'ASSISTANT', 'OPERATOR_STAFF', 'OPERATOR_ADMIN')
             AND operator_id IS NOT NULL)
            OR
            (role IN ('PASSENGER', 'SYSTEM_ADMIN') AND operator_id IS NULL)
        )
);

-- Partial unique: email unique among non-deleted users
CREATE UNIQUE INDEX uq_users_email
    ON users (LOWER(email))
    WHERE deleted_at IS NULL;
-- Partial unique: phone unique among non-deleted, role != SYSTEM_ADMIN (SYSTEM_ADMIN phone optional)
CREATE UNIQUE INDEX uq_users_phone
    ON users (phone)
    WHERE deleted_at IS NULL AND phone IS NOT NULL;
CREATE INDEX idx_users_operator_id ON users (operator_id) WHERE operator_id IS NOT NULL;
CREATE INDEX idx_users_role_status ON users (role, status);

COMMENT ON COLUMN users.phone IS
    'E.164 VN format. REQUIRED for all roles except SYSTEM_ADMIN (and Google OAuth users before complete-profile). UNIQUE across all roles.';
COMMENT ON COLUMN users.password_hash IS
    'bcrypt cost 12. NULL for Google-only accounts.';

-- -----------------------------------------------------------------------------
-- oauth_identities
-- -----------------------------------------------------------------------------
CREATE TABLE oauth_identities (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    provider oauth_provider NOT NULL,
    provider_subject VARCHAR(255) NOT NULL,  -- google sub
    provider_email VARCHAR(255) NULL,
    linked_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX uq_oauth_identities_provider_subject
    ON oauth_identities (provider, provider_subject);
CREATE UNIQUE INDEX uq_oauth_identities_user_provider
    ON oauth_identities (user_id, provider);
CREATE INDEX idx_oauth_identities_user_id ON oauth_identities (user_id);

-- -----------------------------------------------------------------------------
-- refresh_tokens
-- -----------------------------------------------------------------------------
CREATE TABLE refresh_tokens (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    token_hash VARCHAR(255) NOT NULL,
    family_id UUID NOT NULL,
    parent_token_id UUID NULL REFERENCES refresh_tokens (id) ON DELETE SET NULL,
    issued_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    expires_at TIMESTAMPTZ NOT NULL,
    revoked_at TIMESTAMPTZ NULL,
    revoked_reason refresh_token_revoke_reason NULL,
    user_agent VARCHAR(500) NULL,
    ip_address VARCHAR(45) NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX uq_refresh_tokens_token_hash ON refresh_tokens (token_hash);
CREATE INDEX idx_refresh_tokens_user_id ON refresh_tokens (user_id);
CREATE INDEX idx_refresh_tokens_family_id ON refresh_tokens (family_id);
CREATE INDEX idx_refresh_tokens_expires_at ON refresh_tokens (expires_at) WHERE revoked_at IS NULL;
-- Family-chain query (rotation history / reuse detection walks parent_token_id self-FK).
CREATE INDEX idx_refresh_tokens_parent_token_id
    ON refresh_tokens (parent_token_id) WHERE parent_token_id IS NOT NULL;

COMMENT ON COLUMN refresh_tokens.family_id IS
    'Family of rotated tokens. Reuse detection: revoked token used again → revoke entire family.';
COMMENT ON COLUMN refresh_tokens.parent_token_id IS
    'Self-FK to the token this one replaced. NULL for first token in family.';

-- -----------------------------------------------------------------------------
-- email_verification_tokens
-- -----------------------------------------------------------------------------
CREATE TABLE email_verification_tokens (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    purpose email_verification_purpose NOT NULL,
    code VARCHAR(255) NOT NULL,    -- numeric OTP for REGISTRATION/PASSWORD_RESET; UUID for SET_INITIAL_PASSWORD
    expires_at TIMESTAMPTZ NOT NULL,
    failed_attempts INT NOT NULL DEFAULT 0,
    used_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX uq_email_verification_tokens_code_purpose
    ON email_verification_tokens (code, purpose);
CREATE INDEX idx_email_verification_tokens_user_purpose
    ON email_verification_tokens (user_id, purpose) WHERE used_at IS NULL;
CREATE INDEX idx_email_verification_tokens_expires_at
    ON email_verification_tokens (expires_at) WHERE used_at IS NULL;

COMMENT ON COLUMN email_verification_tokens.failed_attempts IS
    'Brute-force counter. Token invalidated after 5 failed attempts.';

-- -----------------------------------------------------------------------------
-- user_devices (FCM tokens — see Section 8 FCM Token Lifecycle)
-- -----------------------------------------------------------------------------
CREATE TABLE user_devices (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    fcm_token VARCHAR(500) NOT NULL,
    platform device_platform NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    last_active_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX uq_user_devices_user_fcm_token ON user_devices (user_id, fcm_token);
CREATE INDEX idx_user_devices_fcm_token ON user_devices (fcm_token) WHERE is_active = TRUE;
CREATE INDEX idx_user_devices_user_active ON user_devices (user_id) WHERE is_active = TRUE;
CREATE INDEX idx_user_devices_last_active_at ON user_devices (last_active_at) WHERE is_active = TRUE;

-- -----------------------------------------------------------------------------
-- activity_logs (audit)
-- -----------------------------------------------------------------------------
CREATE TABLE activity_logs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users (id) ON DELETE RESTRICT,
    action activity_log_action NOT NULL,
    metadata JSONB NULL,
    ip_address VARCHAR(45) NULL,
    user_agent VARCHAR(500) NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_activity_logs_user_id_created_at
    ON activity_logs (user_id, created_at DESC);
CREATE INDEX idx_activity_logs_action_created_at
    ON activity_logs (action, created_at DESC);

-- -----------------------------------------------------------------------------
-- subscription_plans (SaaS plans defined by SYSTEM_ADMIN)
-- -----------------------------------------------------------------------------
CREATE TABLE subscription_plans (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(100) NOT NULL,
    description TEXT NULL,
    price_per_month BIGINT NOT NULL DEFAULT 0,
    price_per_year BIGINT NOT NULL DEFAULT 0,
    -- Resource limits
    max_vehicles INT NOT NULL DEFAULT 0,
    max_drivers INT NOT NULL DEFAULT 0,
    max_assistants INT NOT NULL DEFAULT 0,
    max_operator_users INT NOT NULL DEFAULT 0,
    max_routes INT NOT NULL DEFAULT 0,
    max_trips_per_month INT NOT NULL DEFAULT 0,
    -- Module flags
    enable_parcel BOOLEAN NOT NULL DEFAULT FALSE,
    enable_shuttle BOOLEAN NOT NULL DEFAULT FALSE,
    enable_rag BOOLEAN NOT NULL DEFAULT FALSE,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_subscription_plans_price_per_month_non_negative CHECK (price_per_month >= 0),
    CONSTRAINT chk_subscription_plans_price_per_year_non_negative CHECK (price_per_year >= 0)
);

CREATE INDEX idx_subscription_plans_is_active ON subscription_plans (is_active);

-- -----------------------------------------------------------------------------
-- operator_subscriptions (1-1 with operators, current plan + usage counters)
-- -----------------------------------------------------------------------------
CREATE TABLE operator_subscriptions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id UUID NOT NULL UNIQUE REFERENCES operators (id) ON DELETE RESTRICT,
    plan_id UUID NOT NULL REFERENCES subscription_plans (id) ON DELETE RESTRICT,
    previous_active_plan_id UUID NULL REFERENCES subscription_plans (id) ON DELETE SET NULL,
    status subscription_status NOT NULL DEFAULT 'PENDING_APPROVAL',
    started_at TIMESTAMPTZ NULL,
    expires_at TIMESTAMPTZ NULL,
    payment_method subscription_payment_method NULL,
    billing_period subscription_billing_period NULL,
    -- Usage counters (current period)
    current_vehicles INT NOT NULL DEFAULT 0,
    current_drivers INT NOT NULL DEFAULT 0,
    current_assistants INT NOT NULL DEFAULT 0,
    current_operator_users INT NOT NULL DEFAULT 0,
    current_routes INT NOT NULL DEFAULT 0,
    current_trips_this_month INT NOT NULL DEFAULT 0,
    last_reset_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    -- Notification flags
    warn_sent_at TIMESTAMPTZ NULL,
    trial_expiring_warn_sent_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_operator_subscriptions_status ON operator_subscriptions (status);
CREATE INDEX idx_operator_subscriptions_expires_at ON operator_subscriptions (expires_at)
    WHERE status = 'ACTIVE';
CREATE INDEX idx_operator_subscriptions_plan_id ON operator_subscriptions (plan_id);
CREATE INDEX idx_operator_subscriptions_previous_active_plan_id
    ON operator_subscriptions (previous_active_plan_id) WHERE previous_active_plan_id IS NOT NULL;

COMMENT ON COLUMN operator_subscriptions.previous_active_plan_id IS
    'Plan ACTIVE before PENDING_PAYMENT; used by revert flow if payment times out after 7 days.';
COMMENT ON COLUMN operator_subscriptions.current_trips_this_month IS
    'Reset to 0 monthly by Hangfire (day 1, 00:01). Skipped for Trip.source = VEHICLE_SUBSTITUTION.';

-- -----------------------------------------------------------------------------
-- subscription_upgrade_attempts (Day 37 payment saga; Payment ID is logical)
-- -----------------------------------------------------------------------------
CREATE TABLE subscription_upgrade_attempts (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    subscription_id UUID NOT NULL REFERENCES operator_subscriptions (id) ON DELETE RESTRICT,
    operator_id UUID NOT NULL REFERENCES operators (id) ON DELETE RESTRICT,
    target_plan_id UUID NOT NULL REFERENCES subscription_plans (id) ON DELETE RESTRICT,
    billing_period subscription_billing_period NOT NULL,
    amount BIGINT NOT NULL CHECK (amount >= 0),
    status subscription_upgrade_attempt_status NOT NULL,
    payment_id UUID NULL,
    idempotency_key VARCHAR(100) NOT NULL,
    due_at TIMESTAMPTZ NOT NULL,
    warn_sent_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_subscription_upgrade_attempts_idempotency_key UNIQUE (idempotency_key)
);

CREATE UNIQUE INDEX uq_subscription_upgrade_attempts_payment_id
    ON subscription_upgrade_attempts (payment_id) WHERE payment_id IS NOT NULL;
CREATE UNIQUE INDEX uq_subscription_upgrade_attempts_active_subscription
    ON subscription_upgrade_attempts (subscription_id)
    WHERE status IN ('INITIATED', 'PAYMENT_PENDING');
CREATE INDEX idx_subscription_upgrade_attempts_status_due_at
    ON subscription_upgrade_attempts (status, due_at);

-- -----------------------------------------------------------------------------
-- subscription_quota_allocations (durable cross-service quota reservation)
-- -----------------------------------------------------------------------------
CREATE TABLE subscription_quota_allocations (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id UUID NOT NULL,
    subscription_id UUID NOT NULL REFERENCES operator_subscriptions (id) ON DELETE RESTRICT,
    resource VARCHAR(32) NOT NULL,
    resource_id UUID NOT NULL,
    period_key VARCHAR(7) NULL,
    released_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_subscription_quota_allocations_resource UNIQUE (operator_id, resource, resource_id)
);
CREATE INDEX idx_subscription_quota_allocations_subscription_resource
    ON subscription_quota_allocations (subscription_id, resource, released_at);

-- Durable marker for replay-safe OperatorWallet bootstrap events.
CREATE TABLE operator_wallet_backfill_markers (
    operator_id UUID PRIMARY KEY,
    event_id UUID NOT NULL UNIQUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- =============================================================================
-- TRIGGERS — auto-update updated_at on UPDATE
-- =============================================================================

CREATE OR REPLACE FUNCTION trg_set_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = now();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_operators_updated_at BEFORE UPDATE ON operators
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_users_updated_at BEFORE UPDATE ON users
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_oauth_identities_updated_at BEFORE UPDATE ON oauth_identities
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_refresh_tokens_updated_at BEFORE UPDATE ON refresh_tokens
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_user_devices_updated_at BEFORE UPDATE ON user_devices
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_subscription_plans_updated_at BEFORE UPDATE ON subscription_plans
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_operator_subscriptions_updated_at BEFORE UPDATE ON operator_subscriptions
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_subscription_upgrade_attempts_updated_at BEFORE UPDATE ON subscription_upgrade_attempts
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_subscription_quota_allocations_updated_at BEFORE UPDATE ON subscription_quota_allocations
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_operator_wallet_backfill_markers_updated_at BEFORE UPDATE ON operator_wallet_backfill_markers
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();

-- =============================================================================
-- Hangfire (.NET) schema lives in this same DB under schema `hangfire`.
-- Created automatically by Hangfire.PostgreSql at app startup.
-- DO NOT define hangfire.* tables here.
-- =============================================================================
