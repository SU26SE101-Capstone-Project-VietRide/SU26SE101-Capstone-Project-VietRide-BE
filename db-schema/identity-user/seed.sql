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

-- Bootstrap SYSTEM_ADMIN is intentionally handled by the Identity Service startup seeder.
