CREATE TYPE "vietride_tracking"."trip_share_grant_revoke_reason" AS ENUM (
    'USER_REVOKED',
    'TRIP_TERMINATED',
    'EXPIRED',
    'CREATION_ROLLBACK'
);

CREATE TABLE "vietride_tracking"."trip_share_grants" (
    "id" UUID NOT NULL DEFAULT gen_random_uuid(),
    "trip_id" UUID NOT NULL,
    "created_by_user_id" UUID NOT NULL,
    "token_hash" CHAR(64) NOT NULL,
    "token_version" SMALLINT NOT NULL DEFAULT 1,
    "expires_at" TIMESTAMPTZ(6) NOT NULL,
    "revoked_at" TIMESTAMPTZ(6),
    "revoke_reason" "vietride_tracking"."trip_share_grant_revoke_reason",
    "created_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "trip_share_grants_pkey" PRIMARY KEY ("id"),
    CONSTRAINT "chk_trip_share_grants_expires_after_created"
        CHECK ("expires_at" > "created_at"),
    CONSTRAINT "chk_trip_share_grants_token_hash"
        CHECK ("token_hash" ~ '^[0-9a-f]{64}$'),
    CONSTRAINT "chk_trip_share_grants_token_version_positive"
        CHECK ("token_version" > 0)
);

CREATE UNIQUE INDEX "uq_trip_share_grants_token_hash"
    ON "vietride_tracking"."trip_share_grants"("token_hash");

CREATE UNIQUE INDEX "uq_trip_share_grants_active_owner_trip"
    ON "vietride_tracking"."trip_share_grants"("trip_id", "created_by_user_id")
    WHERE "revoked_at" IS NULL;

CREATE INDEX "idx_trip_share_grants_active_expires_at"
    ON "vietride_tracking"."trip_share_grants"("expires_at")
    WHERE "revoked_at" IS NULL;
