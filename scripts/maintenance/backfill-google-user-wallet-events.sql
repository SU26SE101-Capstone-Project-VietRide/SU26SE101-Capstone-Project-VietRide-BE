-- Backfill passenger wallet-bootstrap events for Google OAuth accounts.
--
-- Run this script only against the vietride_identity database, after the
-- Payment consumer queue `payment.wallet-bootstrap` is ready on the
-- `vietride.events` exchange. The script writes Identity Outbox rows only;
-- it never writes directly to the Payment database.

-- Preflight: count and list recovery events that have not been enqueued yet.
WITH google_passengers AS (
    SELECT DISTINCT
        users.id AS user_id,
        users.role::text AS role,
        users.email,
        users.created_at,
        md5('vietride.google-wallet-backfill.v1:' || users.id::text)::uuid AS event_id
    FROM vietride_identity.users AS users
    INNER JOIN vietride_identity.oauth_identities AS oauth
        ON oauth.user_id = users.id
       AND oauth.provider::text = 'GOOGLE'
    WHERE users.deleted_at IS NULL
      AND users.role::text = 'PASSENGER'
)
SELECT COUNT(*) AS pending_google_wallet_recovery_events
FROM google_passengers AS candidates
WHERE NOT EXISTS (
    SELECT 1
    FROM vietride_identity.outbox_events AS outbox
    WHERE outbox.id = candidates.event_id
);

WITH google_passengers AS (
    SELECT DISTINCT
        users.id AS user_id,
        users.email,
        md5('vietride.google-wallet-backfill.v1:' || users.id::text)::uuid AS event_id
    FROM vietride_identity.users AS users
    INNER JOIN vietride_identity.oauth_identities AS oauth
        ON oauth.user_id = users.id
       AND oauth.provider::text = 'GOOGLE'
    WHERE users.deleted_at IS NULL
      AND users.role::text = 'PASSENGER'
)
SELECT
    candidates.user_id,
    candidates.email,
    candidates.event_id
FROM google_passengers AS candidates
WHERE NOT EXISTS (
    SELECT 1
    FROM vietride_identity.outbox_events AS outbox
    WHERE outbox.id = candidates.event_id
)
ORDER BY candidates.user_id;

BEGIN;

SELECT pg_advisory_xact_lock(
    hashtextextended('vietride.identity.google-wallet-backfill.v1', 0));

WITH google_passengers AS (
    SELECT DISTINCT
        users.id AS user_id,
        users.role::text AS role,
        users.email,
        users.created_at,
        md5('vietride.google-wallet-backfill.v1:' || users.id::text)::uuid AS event_id
    FROM vietride_identity.users AS users
    INNER JOIN vietride_identity.oauth_identities AS oauth
        ON oauth.user_id = users.id
       AND oauth.provider::text = 'GOOGLE'
    WHERE users.deleted_at IS NULL
      AND users.role::text = 'PASSENGER'
)
INSERT INTO vietride_identity.outbox_events (
    id,
    event_type,
    payload,
    status,
    retry_count,
    last_error,
    created_at,
    published_at)
SELECT
    candidates.event_id,
    'identity.user.created',
    jsonb_build_object(
        'eventId', candidates.event_id,
        'userId', candidates.user_id,
        'role', candidates.role,
        'email', candidates.email,
        'createdAt', to_char(
            candidates.created_at AT TIME ZONE 'UTC',
            'YYYY-MM-DD"T"HH24:MI:SS.US"Z"')),
    'PENDING',
    0,
    NULL,
    now(),
    NULL
FROM google_passengers AS candidates
ON CONFLICT (id) DO NOTHING
RETURNING
    id AS enqueued_event_id,
    payload ->> 'userId' AS user_id;

COMMIT;

-- Postflight: expected value is 0. Re-running the script must keep it at 0.
WITH google_passengers AS (
    SELECT DISTINCT
        users.id AS user_id,
        md5('vietride.google-wallet-backfill.v1:' || users.id::text)::uuid AS event_id
    FROM vietride_identity.users AS users
    INNER JOIN vietride_identity.oauth_identities AS oauth
        ON oauth.user_id = users.id
       AND oauth.provider::text = 'GOOGLE'
    WHERE users.deleted_at IS NULL
      AND users.role::text = 'PASSENGER'
)
SELECT COUNT(*) AS remaining_google_wallet_recovery_events
FROM google_passengers AS candidates
WHERE NOT EXISTS (
    SELECT 1
    FROM vietride_identity.outbox_events AS outbox
    WHERE outbox.id = candidates.event_id
);
