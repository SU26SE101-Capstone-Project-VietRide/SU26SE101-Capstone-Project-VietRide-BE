-- =============================================================================
-- VietRide :: Payment & Wallet Service :: Seed data
-- =============================================================================
-- PlatformWallet singleton row. Fixed UUID keeps app config/simple tests deterministic.
INSERT INTO platform_wallets (id, balance, currency)
VALUES ('00000000-0000-0000-0000-000000000001', 0, 'VND')
ON CONFLICT (id) DO NOTHING;
-- =============================================================================
