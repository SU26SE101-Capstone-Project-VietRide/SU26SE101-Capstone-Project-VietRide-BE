CREATE TABLE "vietride_rag"."idempotency_operations" (
    "operation_id" UUID NOT NULL,
    "user_id" UUID NOT NULL,
    "method" VARCHAR(10) NOT NULL,
    "path" VARCHAR(500) NOT NULL,
    "fingerprint" CHAR(64) NOT NULL,
    "owner_token" UUID NOT NULL,
    "status" VARCHAR(20) NOT NULL,
    "response_status" INTEGER,
    "response_headers" JSONB,
    "response_body" TEXT,
    "created_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMPTZ(6) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "expires_at" TIMESTAMPTZ(6) NOT NULL,
    CONSTRAINT "idempotency_operations_pkey" PRIMARY KEY ("operation_id")
);

CREATE INDEX "idx_idempotency_operations_user_created"
    ON "vietride_rag"."idempotency_operations"("user_id", "created_at" DESC);
CREATE INDEX "idx_idempotency_operations_expires_at"
    ON "vietride_rag"."idempotency_operations"("expires_at");
