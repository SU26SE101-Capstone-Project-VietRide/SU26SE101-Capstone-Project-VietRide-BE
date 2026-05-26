-- =============================================================================
-- VietRide :: Trip-Route-Vehicle Service :: Seed data
-- =============================================================================
-- Only system-required seed. 3 platform VehicleType records (system-defined).
-- Operator can add custom types but cannot delete these 3.
-- Idempotent via ON CONFLICT.
-- =============================================================================

INSERT INTO vehicle_types (
    id, code, display_name,
    estimated_passenger_luggage_kg_per_seat, default_seat_count,
    is_system_defined, is_active
) VALUES
(
    '00000000-0000-0000-0000-000000000101'::UUID,
    'STANDARD_BUS',
    'Xe ghế ngồi tiêu chuẩn',
    10, 45,
    TRUE, TRUE
),
(
    '00000000-0000-0000-0000-000000000102'::UUID,
    'LIMOUSINE',
    'Limousine',
    15, 9,
    TRUE, TRUE
),
(
    '00000000-0000-0000-0000-000000000103'::UUID,
    'SLEEPER_BUS',
    'Xe giường nằm',
    20, 40,
    TRUE, TRUE
)
ON CONFLICT (id) DO NOTHING;
