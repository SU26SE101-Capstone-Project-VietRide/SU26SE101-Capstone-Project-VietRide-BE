-- =============================================================================
-- VietRide :: Identity & User Service :: Seed data
-- =============================================================================
-- This file contains ONLY system-required seed data — no sample/test data.
-- Run after schema.sql on empty DB. Idempotent via ON CONFLICT.
-- =============================================================================

-- -----------------------------------------------------------------------------
-- 1) Default SubscriptionPlan "Starter (Free Trial)"
--    Fixed UUID for deterministic EF Core seed migration cross-environment.
-- -----------------------------------------------------------------------------
INSERT INTO subscription_plans (
    id, name, description,
    price_per_month, price_per_year,
    max_vehicles, max_drivers, max_assistants, max_operator_users,
    max_routes, max_trips_per_month,
    enable_parcel, enable_shuttle, enable_rag,
    is_active
) VALUES (
    '00000000-0000-0000-0000-000000000001'::UUID,
    'Starter (Free Trial)',
    'Free 30-day trial auto-assigned on operator registration.',
    0, 0,
    3, 5, 5, 3,
    5, 100,
    FALSE, FALSE, TRUE,
    TRUE
)
ON CONFLICT (id) DO NOTHING;

-- -----------------------------------------------------------------------------
-- 2) Bootstrap SYSTEM_ADMIN user
--    Idempotent: only inserts if no SYSTEM_ADMIN exists yet (5.1.1 spec).
--    SECURITY: replace password_hash AFTER first deploy via:
--      UPDATE users SET password_hash = '<bcrypt cost 12 of new password>'
--      WHERE email = 'admin@vietride.local';
--    Production: load email + password from env vars
--      (SYSTEM_ADMIN_BOOTSTRAP_EMAIL, SYSTEM_ADMIN_BOOTSTRAP_PASSWORD).
-- -----------------------------------------------------------------------------
INSERT INTO users (
    id, email, phone, password_hash, display_name, role, status
)
SELECT
    '00000000-0000-0000-0000-000000000010'::UUID,
    'admin@vietride.local',
    NULL,
    '$2b$12$PLACEHOLDER_REPLACE_AFTER_DEPLOY_BCRYPT_HASH_HERE_xxxxxxxxxxxxxx',
    'System Administrator',
    'SYSTEM_ADMIN',
    'ACTIVE'
WHERE NOT EXISTS (
    SELECT 1 FROM users WHERE role = 'SYSTEM_ADMIN'
);
