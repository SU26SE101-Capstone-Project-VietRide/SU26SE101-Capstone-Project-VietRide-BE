-- =============================================================================
-- VietRide :: Payment & Wallet Service :: PostgreSQL 16 schema
-- Database: vietride_payment
-- Framework: .NET Core 8 + EF Core 8
-- =============================================================================
-- v1 model: booking/parcel revenue vào PlatformWallet holding pool, sau đó
-- settle sang ví nội bộ operator (OperatorWallet),
-- KHÔNG có bank withdrawal. Bank withdrawal là v2 feature.
-- Cycle: doanh thu hold 7 ngày sau Trip terminal → Monday weekly auto-settle.
-- Admin manual settle per-trip cũng support.
-- =============================================================================

CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- =============================================================================
-- ENUMS
-- =============================================================================

CREATE TYPE payment_reference_type AS ENUM (
    'BOOKING', 'BOOKING_GROUP', 'PARCEL', 'PARCEL_ADDITIONAL', 'TOP_UP', 'SUBSCRIPTION'
);

CREATE TYPE payment_method AS ENUM ('WALLET', 'VNPAY');

CREATE TYPE payment_status AS ENUM (
    'PENDING_REDIRECT', 'SUCCEEDED', 'FAILED', 'EXPIRED', 'REFUNDED'
);

CREATE TYPE top_up_request_status AS ENUM (
    'PENDING', 'SUCCEEDED', 'FAILED', 'EXPIRED'
);

CREATE TYPE wallet_transaction_type AS ENUM ('CREDIT', 'DEBIT');

CREATE TYPE wallet_transaction_ref AS ENUM (
    'TOP_UP', 'BOOKING_PAYMENT', 'BOOKING_REFUND',
    'PARCEL_PAYMENT', 'PARCEL_REFUND', 'PARCEL_ADDITIONAL_PAYMENT', 'MANUAL_ADJUSTMENT'
);

CREATE TYPE invoice_status AS ENUM ('DRAFT', 'ISSUED', 'CANCELLED');
CREATE TYPE invoice_pdf_generation_status AS ENUM ('PENDING', 'PROCESSING', 'FAILED', 'COMPLETED');

-- Platform clearing/holding wallet — singleton internal pool
CREATE TYPE platform_wallet_transaction_type AS ENUM ('CREDIT', 'DEBIT');

CREATE TYPE platform_wallet_transaction_ref AS ENUM (
    'BOOKING_PAYMENT_HOLD',
    'PARCEL_PAYMENT_HOLD',
    'PARCEL_ADDITIONAL_PAYMENT_HOLD',
    'BOOKING_REFUND',
    'PARCEL_REFUND',
    'TRIP_SETTLEMENT',
    'SUBSCRIPTION_PAYMENT',
    'MANUAL_ADJUSTMENT'
);

CREATE TYPE operator_ledger_entry_type AS ENUM (
    'BOOKING_REVENUE', 'PARCEL_REVENUE',
    'BOOKING_REFUND', 'PARCEL_REFUND',
    'VOUCHER_VIETRIDE_FUNDED_CREDIT', 'VOUCHER_OPERATOR_FUNDED_AUDIT',
    'ADJUSTMENT'
);

CREATE TYPE operator_ledger_reference_type AS ENUM (
    'BOOKING', 'PARCEL', 'VOUCHER_USAGE', 'MANUAL'
);

CREATE TYPE operator_ledger_adjustment_reason AS ENUM (
    'VIETRIDE_FUNDED_VOUCHER_REVERSAL',
    'GENERIC_BOOKING_REFUND_ENTITLEMENT',
    'MANUAL_WALLET_ADJUSTMENT',
    'LEGACY_UNCLASSIFIED'
);

-- Operator wallet (internal v1 wallet) — transaction log enums
CREATE TYPE operator_wallet_transaction_type AS ENUM ('CREDIT', 'DEBIT');

CREATE TYPE operator_wallet_transaction_ref AS ENUM (
    'TRIP_SETTLEMENT',
    'ADJUSTMENT',
    'SUBSCRIPTION_PAYMENT'
    -- v2 will add 'WITHDRAWAL' for bank transfer out
);

-- Trip settlement state machine
CREATE TYPE operator_trip_settlement_status AS ENUM (
    'PENDING_HOLD',    -- created at Trip terminal; waiting eligibleAt (7-day hold)
    'ELIGIBLE',        -- past eligibleAt; ready for Monday auto-settle or admin manual
    'SETTLED',         -- wallet credited; terminal
    'CANCELLED'        -- netAmount <= 0 at settle time (all refunded), or admin cancel
);

CREATE TYPE operator_trip_settlement_method AS ENUM (
    'AUTO_WEEKLY',     -- Hangfire Monday 09:00 weekly job
    'ADMIN_MANUAL'     -- SYSTEM_ADMIN manual trigger via endpoint
);

CREATE TYPE outbox_event_status AS ENUM ('PENDING', 'PUBLISHING', 'PUBLISHED', 'FAILED');

-- =============================================================================
-- TABLES
-- =============================================================================

-- -----------------------------------------------------------------------------
-- payments
-- -----------------------------------------------------------------------------
CREATE TABLE payments (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    reference_type payment_reference_type NOT NULL,
    reference_id UUID NOT NULL,    -- bookingId / bookingGroupId / parcelId / topUpRequestId / operatorSubscriptionId
    user_id UUID NULL,             -- logical FK identity.users (passenger paying for booking/parcel/top-up)
    operator_id UUID NULL,         -- logical FK identity.operators (operator paying for subscription)
    amount BIGINT NOT NULL,
    method payment_method NOT NULL,
    status payment_status NOT NULL,
    vnpay_txn_ref VARCHAR(100) NULL,
    vnpay_response_code VARCHAR(10) NULL,
    idempotency_key VARCHAR(100) NULL,
    payment_redirect_url TEXT NULL,
    due_at TIMESTAMPTZ NULL,       -- authoritative VNPay/payment deadline; NULL legacy fallback = created_at + 15 minutes
    succeeded_at TIMESTAMPTZ NULL,
    failed_at TIMESTAMPTZ NULL,
    expired_at TIMESTAMPTZ NULL,
    refunded_at TIMESTAMPTZ NULL,
    context JSONB NOT NULL DEFAULT '{}'::jsonb,
    context_reconciliation_required BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_payments_amount_non_negative CHECK (amount >= 0)
);

CREATE UNIQUE INDEX uq_payments_vnpay_txn_ref ON payments (vnpay_txn_ref)
    WHERE vnpay_txn_ref IS NOT NULL;
CREATE UNIQUE INDEX uq_payments_idempotency_key ON payments (idempotency_key)
    WHERE idempotency_key IS NOT NULL;
CREATE INDEX idx_payments_reference ON payments (reference_type, reference_id);
CREATE INDEX idx_payments_user_id_created_at ON payments (user_id, created_at DESC)
    WHERE user_id IS NOT NULL;
CREATE INDEX idx_payments_operator_id_created_at ON payments (operator_id, created_at DESC)
    WHERE operator_id IS NOT NULL;
CREATE INDEX idx_payments_status_created_at ON payments (status, created_at)
    WHERE status IN ('PENDING_REDIRECT');
CREATE INDEX idx_payments_context_reconciliation ON payments (context_reconciliation_required, status)
    WHERE context_reconciliation_required = TRUE;
CREATE INDEX idx_payments_subscription_due_at ON payments (due_at)
    WHERE reference_type = 'SUBSCRIPTION' AND due_at IS NOT NULL;
CREATE INDEX idx_payments_subscription_succeeded_at ON payments (succeeded_at)
    WHERE reference_type = 'SUBSCRIPTION' AND status = 'SUCCEEDED';

COMMENT ON COLUMN payments.vnpay_txn_ref IS
    'VNPay vnp_TxnRef — unique per VNPay transaction. NULL for WALLET method.';
COMMENT ON COLUMN payments.idempotency_key IS
    'From Idempotency-Key header. UNIQUE prevents double-charge on retry.';
COMMENT ON COLUMN payments.due_at IS
    'Authoritative inclusive expiry deadline for payment sessions. NULL legacy rows use created_at + 15 minutes.';

-- -----------------------------------------------------------------------------
-- top_up_requests (PASSENGER wallet top-up via VNPay)
-- -----------------------------------------------------------------------------
CREATE TABLE top_up_requests (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL,    -- logical FK
    amount BIGINT NOT NULL,
    status top_up_request_status NOT NULL DEFAULT 'PENDING',
    vnpay_txn_ref VARCHAR(100) NOT NULL,
    vnpay_response_code VARCHAR(10) NULL,
    payment_redirect_url TEXT NULL,
    succeeded_at TIMESTAMPTZ NULL,
    expired_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_top_up_requests_amount_min CHECK (amount >= 10000)
);

CREATE UNIQUE INDEX uq_top_up_requests_vnpay_txn_ref ON top_up_requests (vnpay_txn_ref);
CREATE INDEX idx_top_up_requests_user_id_created_at ON top_up_requests (user_id, created_at DESC);
CREATE INDEX idx_top_up_requests_status_created_at ON top_up_requests (status, created_at)
    WHERE status = 'PENDING';

COMMENT ON COLUMN top_up_requests.amount IS
    'Min 10,000 VND enforced via CHECK constraint.';

-- -----------------------------------------------------------------------------
-- wallets (PASSENGER wallet — 1-1 with User; natural PK user_id)
--   Mirrors operator_wallets pattern: user_id is the PK (natural key, logical
--   cross-service FK to identity.users). No synthetic id — eliminates the
--   redundant UNIQUE(user_id) index and enables idempotent UPSERT on bootstrap.
-- -----------------------------------------------------------------------------
CREATE TABLE wallets (
    user_id UUID PRIMARY KEY,    -- logical FK identity.users (1-1)
    balance BIGINT NOT NULL DEFAULT 0,
    currency VARCHAR(3) NOT NULL DEFAULT 'VND',
    row_version INT NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_wallets_balance_non_negative CHECK (balance >= 0)
);

COMMENT ON TABLE wallets IS
    'Passenger wallet (1-1 with identity.users). user_id is natural PK — same pattern as operator_wallets. Bootstrap via identity.user.created event (UPSERT idempotent).';
COMMENT ON COLUMN wallets.balance IS
    'BIGINT VND, NEVER negative. Enforced by CHECK + optimistic lock via row_version.';
COMMENT ON COLUMN wallets.row_version IS
    'Optimistic concurrency token. Incremented on every UPDATE.';

-- -----------------------------------------------------------------------------
-- wallet_transactions (passenger wallet immutable ledger)
--   Mirrors operator_wallet_transactions: references wallet by user_id
--   (logical FK to wallets.user_id and identity.users.id). NO hard FK to
--   wallets — match the OperatorWallet pattern, consistency enforced app-layer
--   inside same DB transaction with UPDATE wallets.
-- -----------------------------------------------------------------------------
CREATE TABLE wallet_transactions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL,    -- logical FK wallets.user_id (= identity.users.id)
    type wallet_transaction_type NOT NULL,
    amount BIGINT NOT NULL,           -- always positive; type determines sign
    balance_before BIGINT NOT NULL,
    balance_after BIGINT NOT NULL,
    reference_type wallet_transaction_ref NOT NULL,
    reference_id UUID NULL,            -- bookingId / parcelId / topUpRequestId
    note TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_wallet_transactions_amount_positive CHECK (amount > 0),
    CONSTRAINT chk_wallet_transactions_balance_non_negative
        CHECK (balance_before >= 0 AND balance_after >= 0)
);

CREATE INDEX idx_wallet_transactions_user_id_created_at
    ON wallet_transactions (user_id, created_at DESC);
CREATE INDEX idx_wallet_transactions_reference
    ON wallet_transactions (reference_type, reference_id) WHERE reference_id IS NOT NULL;

COMMENT ON TABLE wallet_transactions IS
    'Immutable wallet ledger for passenger Wallet. INSERT atomic with UPDATE wallets via optimistic lock. Mirrors operator_wallet_transactions pattern (no hard FK, match by user_id).';

-- -----------------------------------------------------------------------------
-- invoices (VietRide → Operator B2B SaaS billing)
-- -----------------------------------------------------------------------------
CREATE TABLE invoices (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    invoice_number VARCHAR(50) NOT NULL,    -- "VR-INV-yyyyMM-XXXXXX"
    operator_id UUID NOT NULL,                -- logical FK
    operator_subscription_id UUID NOT NULL,   -- logical FK identity.operator_subscriptions
    payment_id UUID NOT NULL REFERENCES payments (id) ON DELETE RESTRICT,
    amount BIGINT NOT NULL,
    period_from TIMESTAMPTZ NOT NULL,
    period_to TIMESTAMPTZ NOT NULL,
    status invoice_status NOT NULL DEFAULT 'DRAFT',
    issued_at TIMESTAMPTZ NULL,
    pdf_url TEXT NULL,
    storage_object_path TEXT NULL,
    pdf_generation_status invoice_pdf_generation_status NOT NULL DEFAULT 'PENDING',
    pdf_generation_attempts INT NOT NULL DEFAULT 0,
    pdf_generation_started_at TIMESTAMPTZ NULL,
    pdf_generation_next_retry_at TIMESTAMPTZ NULL,
    pdf_generation_last_error TEXT NULL,
    metadata JSONB NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_invoices_amount_non_negative CHECK (amount >= 0),
    CONSTRAINT chk_invoices_period_order CHECK (period_to > period_from),
    CONSTRAINT chk_invoices_pdf_attempts CHECK (pdf_generation_attempts >= 0 AND pdf_generation_attempts <= 5),
    CONSTRAINT chk_invoices_issued_consistency CHECK (
        status <> 'ISSUED'
        OR (issued_at IS NOT NULL AND pdf_url IS NOT NULL AND storage_object_path IS NOT NULL
            AND pdf_generation_status = 'COMPLETED')
    )
);

CREATE UNIQUE INDEX uq_invoices_invoice_number ON invoices (invoice_number);
CREATE INDEX idx_invoices_operator_id_created_at ON invoices (operator_id, created_at DESC);
CREATE INDEX idx_invoices_status ON invoices (status);
CREATE UNIQUE INDEX uq_invoices_payment_id ON invoices (payment_id);
CREATE INDEX idx_invoices_pdf_retry ON invoices (pdf_generation_status, pdf_generation_next_retry_at)
    WHERE pdf_generation_status IN ('PENDING', 'FAILED', 'PROCESSING');

CREATE TABLE invoice_number_counters (
    period_key CHAR(6) PRIMARY KEY,
    last_value BIGINT NOT NULL,
    CONSTRAINT chk_invoice_number_counters_range CHECK (last_value >= 0 AND last_value <= 999999)
);

-- -----------------------------------------------------------------------------
-- platform_wallets (VIETRIDE singleton clearing/holding pool)
-- -----------------------------------------------------------------------------
CREATE TABLE platform_wallets (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    balance BIGINT NOT NULL DEFAULT 0,
    currency VARCHAR(3) NOT NULL DEFAULT 'VND',
    row_version INT NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_platform_wallets_balance_non_negative CHECK (balance >= 0)
);

CREATE UNIQUE INDEX uq_platform_wallets_singleton ON platform_wallets ((TRUE));

COMMENT ON TABLE platform_wallets IS
    'Singleton internal clearing/holding pool for VietRide. Not a bank account; used for booking/parcel hold, refund, subscription revenue, and settlement transfer.';
COMMENT ON COLUMN platform_wallets.balance IS
    'BIGINT VND, NEVER negative. Debit on refund/settlement, credit on booking/parcel payment hold and subscription payment.';
COMMENT ON COLUMN platform_wallets.row_version IS
    'Optimistic concurrency token. Incremented on every UPDATE.';

-- -----------------------------------------------------------------------------
-- platform_wallet_transactions (immutable PlatformWallet ledger)
-- -----------------------------------------------------------------------------
CREATE TABLE platform_wallet_transactions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    type platform_wallet_transaction_type NOT NULL,
    amount BIGINT NOT NULL,
    balance_before BIGINT NOT NULL,
    balance_after BIGINT NOT NULL,
    reference_type platform_wallet_transaction_ref NOT NULL,
    reference_id UUID NULL,    -- bookingId / parcelId / settlementId / paymentId / adjustmentId
    note TEXT NULL,
    actor_type VARCHAR(16) NOT NULL DEFAULT 'SYSTEM',
    actor_user_id UUID NULL,    -- logical FK identity.users; no cross-database FK
    actor_display_name VARCHAR(200) NULL,
    actor_email VARCHAR(320) NULL,
    actor_role VARCHAR(50) NULL,
    actor_snapshot_resolved BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_platform_wallet_transactions_amount_positive CHECK (amount > 0),
    CONSTRAINT chk_platform_wallet_transactions_balance_non_negative
        CHECK (balance_before >= 0 AND balance_after >= 0),
    CONSTRAINT chk_platform_wallet_transactions_actor_type
        CHECK (actor_type IN ('USER', 'SYSTEM'))
);

CREATE INDEX idx_platform_wallet_transactions_created_at
    ON platform_wallet_transactions (created_at DESC);
CREATE INDEX idx_platform_wallet_transactions_reference
    ON platform_wallet_transactions (reference_type, reference_id)
    WHERE reference_id IS NOT NULL;
CREATE UNIQUE INDEX uq_platform_wallet_transactions_subscription
    ON platform_wallet_transactions (type, reference_type, reference_id)
    WHERE reference_type = 'SUBSCRIPTION_PAYMENT';
CREATE INDEX idx_platform_wallet_transactions_actor_user_id
    ON platform_wallet_transactions (actor_user_id)
    WHERE actor_user_id IS NOT NULL;

COMMENT ON TABLE platform_wallet_transactions IS
    'Immutable ledger for PlatformWallet. INSERT atomic with UPDATE platform_wallets via optimistic lock.';

-- -----------------------------------------------------------------------------
-- deleted_financial_actor_markers (durable privacy fence for manual actors)
-- -----------------------------------------------------------------------------
CREATE TABLE deleted_financial_actor_markers (
    user_id UUID PRIMARY KEY,    -- logical FK identity.users; no cross-database FK
    deleted_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

COMMENT ON TABLE deleted_financial_actor_markers IS
    'Durable tombstones serialized by advisory lock with manual financial writes; prevents deleted-user PII from being restored by a racing write/backfill.';

-- -----------------------------------------------------------------------------
-- operator_wallets (OPERATOR internal wallet — 1-1 with Operator)
--   Replaces the old `operator_balances` table from previous design.
--   Credited only via TripSettlement settle event (CREDIT) or admin ADJUSTMENT.
--   No bank withdrawal in v1 (deferred to v2).
-- -----------------------------------------------------------------------------
CREATE TABLE operator_wallets (
    operator_id UUID PRIMARY KEY,    -- logical FK identity.operators (1-1)
    balance BIGINT NOT NULL DEFAULT 0,
    currency VARCHAR(3) NOT NULL DEFAULT 'VND',
    row_version INT NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_operator_wallets_balance_non_negative CHECK (balance >= 0)
);

COMMENT ON TABLE operator_wallets IS
    'Internal v1 wallet for operators. Replaces former operator_balances aggregate.';
COMMENT ON COLUMN operator_wallets.balance IS
    'BIGINT VND, NEVER negative. Credit from TripSettlement/admin; debit from subscription/admin. v2 adds WITHDRAWAL.';
COMMENT ON COLUMN operator_wallets.row_version IS
    'Optimistic concurrency token. Use row_version check pattern on UPDATE.';

-- -----------------------------------------------------------------------------
-- operator_wallet_transactions (immutable wallet ledger; mirrors wallet_transactions)
-- -----------------------------------------------------------------------------
CREATE TABLE operator_wallet_transactions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id UUID NOT NULL,    -- denormalized for query convenience (also PK of wallet)
    type operator_wallet_transaction_type NOT NULL,
    amount BIGINT NOT NULL,        -- always positive; type determines sign
    balance_before BIGINT NOT NULL,
    balance_after BIGINT NOT NULL,
    reference_type operator_wallet_transaction_ref NOT NULL,
    reference_id UUID NULL,         -- tripSettlementId, paymentId (SUBSCRIPTION_PAYMENT), or adjustment id
    note TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_operator_wallet_transactions_amount_positive CHECK (amount > 0),
    CONSTRAINT chk_operator_wallet_transactions_balance_non_negative
        CHECK (balance_before >= 0 AND balance_after >= 0)
);

CREATE INDEX idx_operator_wallet_transactions_operator_id_created_at
    ON operator_wallet_transactions (operator_id, created_at DESC);
CREATE INDEX idx_operator_wallet_transactions_reference
    ON operator_wallet_transactions (reference_type, reference_id)
    WHERE reference_id IS NOT NULL;
CREATE UNIQUE INDEX uq_operator_wallet_transactions_subscription
    ON operator_wallet_transactions (operator_id, type, reference_type, reference_id)
    WHERE reference_type = 'SUBSCRIPTION_PAYMENT';

COMMENT ON TABLE operator_wallet_transactions IS
    'Immutable wallet ledger for OperatorWallet. INSERT atomic with UPDATE operator_wallets via optimistic lock.';

-- -----------------------------------------------------------------------------
-- operator_ledger_entries (per-event AUDIT log — drops balance_before/after)
-- -----------------------------------------------------------------------------
CREATE TABLE operator_ledger_entries (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id UUID NOT NULL,                -- logical FK
    trip_id UUID NULL,                         -- logical FK trip.trips — NULL only for ADJUSTMENT/MANUAL
    entry_type operator_ledger_entry_type NOT NULL,
    adjustment_reason operator_ledger_adjustment_reason NULL,
    amount BIGINT NOT NULL,                    -- signed: positive=credit, negative=debit, 0=audit-only
    reference_type operator_ledger_reference_type NOT NULL,
    reference_id UUID NOT NULL,
    reference_code VARCHAR(64) NULL,             -- bookingCode / parcelCode; nullable for legacy rows
    source_event_id UUID NOT NULL,
    occurred_at TIMESTAMPTZ NULL,                -- payment/refund business-event time; NULL for legacy rows
    operator_funded_voucher_amount BIGINT NULL,  -- explicit audit metadata; never parsed from note
    note TEXT NULL,
    actor_type VARCHAR(16) NOT NULL DEFAULT 'SYSTEM',
    actor_user_id UUID NULL,
    actor_display_name VARCHAR(200) NULL,
    actor_email VARCHAR(320) NULL,
    actor_role VARCHAR(50) NULL,
    actor_snapshot_resolved BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_operator_ledger_entries_amount_direction CHECK (
        (entry_type IN ('BOOKING_REFUND', 'PARCEL_REFUND') AND amount < 0)
        OR (entry_type = 'VOUCHER_OPERATOR_FUNDED_AUDIT' AND amount = 0)
        OR entry_type = 'ADJUSTMENT'
        OR (entry_type NOT IN ('BOOKING_REFUND', 'PARCEL_REFUND', 'VOUCHER_OPERATOR_FUNDED_AUDIT', 'ADJUSTMENT') AND amount > 0)
    ),
    CONSTRAINT chk_operator_ledger_entries_trip_required CHECK (entry_type = 'ADJUSTMENT' OR trip_id IS NOT NULL),
    CONSTRAINT chk_operator_ledger_entries_adjustment_reason_presence CHECK (
        (entry_type = 'ADJUSTMENT' AND adjustment_reason IS NOT NULL)
        OR (entry_type <> 'ADJUSTMENT' AND adjustment_reason IS NULL)
    ),
    CONSTRAINT chk_operator_ledger_entries_adjustment_reason_semantics CHECK (
        (adjustment_reason = 'VIETRIDE_FUNDED_VOUCHER_REVERSAL'
            AND amount < 0 AND reference_type IN ('BOOKING', 'PARCEL'))
        OR (adjustment_reason = 'GENERIC_BOOKING_REFUND_ENTITLEMENT'
            AND amount = 0 AND reference_type = 'BOOKING')
        OR (adjustment_reason = 'MANUAL_WALLET_ADJUSTMENT'
            AND amount <> 0 AND reference_type = 'MANUAL')
        OR adjustment_reason = 'LEGACY_UNCLASSIFIED'
        OR adjustment_reason IS NULL
    ),
    CONSTRAINT chk_operator_ledger_entries_operator_funded_voucher_amount CHECK (
        operator_funded_voucher_amount IS NULL
        OR (entry_type = 'VOUCHER_OPERATOR_FUNDED_AUDIT'
            AND operator_funded_voucher_amount > 0)
    ),
    CONSTRAINT chk_operator_ledger_entries_actor_type CHECK (actor_type IN ('USER', 'SYSTEM'))
);

CREATE INDEX idx_operator_ledger_entries_operator_id_created_at
    ON operator_ledger_entries (operator_id, created_at DESC);
CREATE INDEX idx_operator_ledger_entries_operator_trip
    ON operator_ledger_entries (operator_id, trip_id)
    WHERE trip_id IS NOT NULL;
CREATE INDEX idx_operator_ledger_entries_reference
    ON operator_ledger_entries (reference_type, reference_id);
CREATE INDEX idx_operator_ledger_entries_entry_type
    ON operator_ledger_entries (operator_id, entry_type);
CREATE INDEX idx_operator_ledger_entries_operator_reference_code
    ON operator_ledger_entries (operator_id, reference_code)
    WHERE reference_code IS NOT NULL;
CREATE INDEX idx_operator_ledger_entries_operator_occurred_at
    ON operator_ledger_entries (operator_id, occurred_at DESC)
    WHERE occurred_at IS NOT NULL;
CREATE UNIQUE INDEX uq_operator_ledger_entries_source
    ON operator_ledger_entries (source_event_id, entry_type, reference_id);
CREATE INDEX idx_operator_ledger_entries_actor_user_id
    ON operator_ledger_entries (actor_user_id) WHERE actor_user_id IS NOT NULL;
CREATE INDEX idx_operator_ledger_entries_canonical_revenue
    ON operator_ledger_entries (created_at, operator_id, reference_type)
    WHERE entry_type IN (
        'BOOKING_REVENUE', 'BOOKING_REFUND', 'PARCEL_REVENUE', 'PARCEL_REFUND',
        'VOUCHER_VIETRIDE_FUNDED_CREDIT'
    ) OR (entry_type = 'ADJUSTMENT'
        AND adjustment_reason = 'VIETRIDE_FUNDED_VOUCHER_REVERSAL');

COMMENT ON TABLE operator_ledger_entries IS
    'Audit-only log per-booking/per-parcel event. NOT the wallet balance source — see operator_wallets + operator_trip_settlements.';
COMMENT ON COLUMN operator_ledger_entries.trip_id IS
    'Link to Trip for aggregation in OperatorTripSettlement netAmount computation. NULL for ADJUSTMENT/MANUAL entries.';
COMMENT ON COLUMN operator_ledger_entries.amount IS
    'Signed BIGINT. Revenue entries positive; refund entries negative; VOUCHER_OPERATOR_FUNDED_AUDIT entries 0 (audit-only).';
COMMENT ON COLUMN operator_ledger_entries.reference_code IS
    'Nullable Booking/Parcel reconciliation code. Legacy rows remain NULL and are exposed as PARTIAL.';
COMMENT ON COLUMN operator_ledger_entries.occurred_at IS
    'Payment/refund business-event timestamp. Readers fall back to created_at for legacy rows and mark metadata PARTIAL.';
COMMENT ON COLUMN operator_ledger_entries.operator_funded_voucher_amount IS
    'Explicit positive operator-funded voucher metadata on VOUCHER_OPERATOR_FUNDED_AUDIT; amount remains 0 and never changes settlement net.';

-- -----------------------------------------------------------------------------
-- operator_trip_settlements (per-Trip settlement marker)
--   Lifecycle: PENDING_HOLD --(eligibleAt reached)--> ELIGIBLE --(weekly or admin)--> SETTLED
--                                                                    |
--                                                                    +--> CANCELLED (netAmount <= 0)
-- -----------------------------------------------------------------------------
CREATE TABLE operator_trip_settlements (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id UUID NOT NULL,    -- logical FK
    trip_id UUID NOT NULL,         -- logical FK trip.trips
    net_amount BIGINT NOT NULL DEFAULT 0,         -- computed at settle time (sum ledger entries for this trip)
    trip_terminal_at TIMESTAMPTZ NOT NULL,        -- = Trip.completedAt or Trip.disruptedAt
    eligible_at TIMESTAMPTZ NOT NULL,             -- = trip_terminal_at + 7 days
    status operator_trip_settlement_status NOT NULL DEFAULT 'PENDING_HOLD',
    settlement_method operator_trip_settlement_method NULL,    -- set when settled
    settled_at TIMESTAMPTZ NULL,
    settled_by_user_id UUID NULL,                  -- SYSTEM_ADMIN if ADMIN_MANUAL; NULL if AUTO_WEEKLY
    operator_snapshot_resolved BOOLEAN NOT NULL DEFAULT FALSE,
    operator_name VARCHAR(200) NULL,
    operator_logo_url VARCHAR(2048) NULL,
    operator_contact_phone VARCHAR(32) NULL,
    settled_by_snapshot_resolved BOOLEAN NOT NULL DEFAULT FALSE,
    settled_by_display_name VARCHAR(200) NULL,
    settled_by_email VARCHAR(320) NULL,
    settled_by_role VARCHAR(50) NULL,
    wallet_transaction_id UUID NULL REFERENCES operator_wallet_transactions (id) ON DELETE SET NULL,
    settlement_failure_count INT NOT NULL DEFAULT 0,
    last_settlement_failure_at TIMESTAMPTZ NULL,
    active_failure_code VARCHAR(100) NULL,
    failure_resolved_at TIMESTAMPTZ NULL,
    row_version INT NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_operator_trip_settlements_eligible_after_terminal
        CHECK (eligible_at >= trip_terminal_at),
    CONSTRAINT chk_operator_trip_settlements_settled_consistency
        CHECK (
            (status IN ('PENDING_HOLD', 'ELIGIBLE')
             AND settled_at IS NULL AND settlement_method IS NULL AND wallet_transaction_id IS NULL)
            OR
            (status IN ('SETTLED', 'CANCELLED')
             AND settled_at IS NOT NULL AND settlement_method IS NOT NULL)
        ),
    CONSTRAINT chk_operator_trip_settlements_failure_consistency CHECK (
        active_failure_code IS NULL
        OR (status = 'ELIGIBLE' AND settlement_failure_count > 0 AND last_settlement_failure_at IS NOT NULL)
    )
);

CREATE UNIQUE INDEX uq_operator_trip_settlements_operator_trip
    ON operator_trip_settlements (operator_id, trip_id);
CREATE INDEX idx_operator_trip_settlements_status_eligible
    ON operator_trip_settlements (status, eligible_at)
    WHERE status IN ('PENDING_HOLD', 'ELIGIBLE');
CREATE INDEX idx_operator_trip_settlements_operator_status
    ON operator_trip_settlements (operator_id, status);
CREATE INDEX idx_operator_trip_settlements_trip_id
    ON operator_trip_settlements (trip_id);
CREATE INDEX idx_operator_trip_settlements_wallet_transaction_id
    ON operator_trip_settlements (wallet_transaction_id) WHERE wallet_transaction_id IS NOT NULL;
CREATE INDEX idx_operator_trip_settlements_settled_by_user_id
    ON operator_trip_settlements (settled_by_user_id) WHERE settled_by_user_id IS NOT NULL;
CREATE INDEX idx_operator_trip_settlements_stuck
    ON operator_trip_settlements (status, active_failure_code, last_settlement_failure_at)
    WHERE status = 'ELIGIBLE' AND active_failure_code IS NOT NULL;
CREATE INDEX idx_operator_trip_settlements_settled_at
    ON operator_trip_settlements (settled_at) WHERE status = 'SETTLED';

COMMENT ON TABLE operator_trip_settlements IS
    'Per-Trip settlement marker. 1 record per Trip per Operator. Drives 7-day hold + Monday auto-settle + admin manual.';
COMMENT ON COLUMN operator_trip_settlements.net_amount IS
    'Recomputed at settle time = SUM(operator_ledger_entries.amount WHERE operator_id=X AND trip_id=Y AND entry_type IN revenue/refund/voucher_vietride_funded_credit). VOUCHER_OPERATOR_FUNDED_AUDIT has amount=0, no impact.';
COMMENT ON COLUMN operator_trip_settlements.eligible_at IS
    'trip_terminal_at + interval 7 days. Admin manual settle can bypass this for early settle.';
COMMENT ON COLUMN operator_trip_settlements.row_version IS
    'Optimistic lock for status transition. Pattern: UPDATE ... WHERE id=:id AND status=:expected AND row_version=:original.';

-- -----------------------------------------------------------------------------
-- refund_failure_logs (Hangfire retry tracking; max 5 retries then admin alert)
-- -----------------------------------------------------------------------------
CREATE TABLE refund_failure_logs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    booking_id UUID NULL,             -- logical FK
    parcel_id UUID NULL,              -- logical FK
    trigger_event_type VARCHAR(100) NOT NULL,
    failure_reason TEXT NOT NULL,
    user_id UUID NULL,                -- logical FK; retry payload
    amount BIGINT NULL,               -- retry payload, VND; zero only for BOOKING_REFUND_PAYMENT
    reference_type VARCHAR(50) NULL,  -- BOOKING_REFUND / PARCEL_REFUND / BOOKING_REFUND_PAYMENT
    reference_id UUID NULL,           -- bookingId / parcelId; paymentId for BOOKING_REFUND_PAYMENT
    retry_count INT NOT NULL DEFAULT 0,
    last_attempt_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    resolved_at TIMESTAMPTZ NULL,
    resolved_by_user_id UUID NULL,    -- logical FK SYSTEM_ADMIN manual resolve
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_refund_failure_logs_target_exists
        CHECK (booking_id IS NOT NULL OR parcel_id IS NOT NULL)
);

CREATE INDEX idx_refund_failure_logs_unresolved
    ON refund_failure_logs (last_attempt_at) WHERE resolved_at IS NULL;
CREATE INDEX idx_refund_failure_logs_booking_id ON refund_failure_logs (booking_id)
    WHERE booking_id IS NOT NULL;
CREATE INDEX idx_refund_failure_logs_parcel_id ON refund_failure_logs (parcel_id)
    WHERE parcel_id IS NOT NULL;
CREATE INDEX idx_refund_failure_logs_resolved_by_user_id
    ON refund_failure_logs (resolved_by_user_id) WHERE resolved_by_user_id IS NOT NULL;
CREATE INDEX idx_refund_failure_logs_reference
    ON refund_failure_logs (reference_type, reference_id)
    WHERE reference_type IS NOT NULL AND reference_id IS NOT NULL;

-- -----------------------------------------------------------------------------
-- processed_integration_events (durable consumer idempotency)
-- -----------------------------------------------------------------------------
CREATE TABLE processed_integration_events (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    consumer VARCHAR(150) NOT NULL,
    event_id UUID NOT NULL,
    payload_hash CHAR(64) NULL,
    processed_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX uq_processed_integration_events_consumer_event
    ON processed_integration_events (consumer, event_id);

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

CREATE TRIGGER trg_payments_updated_at BEFORE UPDATE ON payments
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_top_up_requests_updated_at BEFORE UPDATE ON top_up_requests
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_wallets_updated_at BEFORE UPDATE ON wallets
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_invoices_updated_at BEFORE UPDATE ON invoices
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_platform_wallets_updated_at BEFORE UPDATE ON platform_wallets
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_operator_wallets_updated_at BEFORE UPDATE ON operator_wallets
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
CREATE TRIGGER trg_operator_trip_settlements_updated_at BEFORE UPDATE ON operator_trip_settlements
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();

-- =============================================================================
-- Hangfire schema (auto-created): VNPay PENDING_REDIRECT EXPIRED 15m,
-- TopUpRequest EXPIRED 15m,
-- Trip settlement eligibility flag (daily 02:00) — PENDING_HOLD → ELIGIBLE,
-- Trip settlement weekly auto-settle (Monday 09:00) — ELIGIBLE → SETTLED
-- with PlatformWallet DEBIT + OperatorWallet CREDIT,
-- Subscription trial expire (daily 00:30), Subscription PENDING_PAYMENT
-- warn/revert, Invoice PDF retry, RefundFailureLog retry.
-- =============================================================================
