-- =============================================================================
-- VietRide :: RAG AI Service :: PostgreSQL 16 schema
-- Database: vietride_rag
-- Schema: vietride_rag
-- Framework: NestJS + Prisma ORM
-- =============================================================================
-- pgvector extension required for embedding similarity search.
-- Storage provider: Cloudinary raw assets.
-- Chat provider: ShopAIKey OpenAI-compatible API, gemini-3.5-flash.
-- Embedding model: ShopAIKey OpenAI-compatible API, gemini-embedding-2-preview.
-- Embedding dimension: 3072 (native ShopAIKey model output).
-- =============================================================================

CREATE EXTENSION IF NOT EXISTS "pgcrypto";
CREATE EXTENSION IF NOT EXISTS "vector";

CREATE SCHEMA IF NOT EXISTS vietride_rag;
SET search_path TO vietride_rag, public;

-- =============================================================================
-- ENUMS
-- =============================================================================

CREATE TYPE knowledge_document_status AS ENUM (
    'PENDING_REVIEW',
    'APPROVED',
    'REJECTED',
    'ARCHIVED'
);

CREATE TYPE knowledge_document_access AS ENUM (
    'PUBLIC',
    'OPERATOR',
    'ADMIN'
);

CREATE TYPE knowledge_document_file_type AS ENUM (
    'PDF',
    'DOCX',
    'TXT',
    'MARKDOWN'
);

CREATE TYPE knowledge_document_category AS ENUM (
    'CUSTOMER_SUPPORT',
    'OPERATOR_POLICY',
    'PLATFORM_ADMIN'
);

CREATE TYPE knowledge_document_type AS ENUM (
    'FAQ',
    'POLICY',
    'SOP',
    'GUIDE',
    'TERMS'
);

CREATE TYPE knowledge_document_ingest_status AS ENUM (
    'PENDING',
    'PROCESSING',
    'COMPLETED',
    'FAILED'
);

CREATE TYPE rag_storage_provider AS ENUM (
    'CLOUDINARY'
);

CREATE TYPE rag_conversation_role AS ENUM (
    'PASSENGER',
    'DRIVER',
    'ASSISTANT',
    'OPERATOR_STAFF',
    'OPERATOR_ADMIN',
    'SYSTEM_ADMIN'
);

CREATE TYPE rag_message_role AS ENUM (
    'USER',
    'ASSISTANT'
);

CREATE TYPE outbox_event_status AS ENUM (
    'PENDING',
    'PUBLISHING',
    'PUBLISHED',
    'FAILED',
    'DISCARDED'
);

CREATE TYPE policy_type AS ENUM (
    'FOR_OPERATOR',
    'FOR_USER'
);

CREATE TYPE policy_audit_action AS ENUM (
    'CREATE',
    'UPDATE',
    'ACTIVATE',
    'DEACTIVATE',
    'DELETE'
);

-- =============================================================================
-- TABLES
-- =============================================================================

CREATE TABLE knowledge_documents (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    title VARCHAR(500) NOT NULL,
    description TEXT NULL,
    storage_provider rag_storage_provider NOT NULL DEFAULT 'CLOUDINARY',
    storage_path TEXT NOT NULL,
    file_name VARCHAR(255) NOT NULL,
    mime_type VARCHAR(100) NOT NULL,
    file_size BIGINT NOT NULL,
    file_type knowledge_document_file_type NOT NULL,
    access_level knowledge_document_access NOT NULL,
    category knowledge_document_category NOT NULL,
    document_type knowledge_document_type NOT NULL,
    audience_roles TEXT[] NOT NULL DEFAULT ARRAY[]::TEXT[],
    language VARCHAR(10) NOT NULL DEFAULT 'vi',
    operator_id UUID NULL,
    status knowledge_document_status NOT NULL DEFAULT 'PENDING_REVIEW',
    ingest_status knowledge_document_ingest_status NOT NULL DEFAULT 'PENDING',
    ingest_error TEXT NULL,
    ingested_at TIMESTAMPTZ NULL,
    chunk_count INT NULL,
    embedding_model VARCHAR(255) NULL,
    embedding_dimensions INT NULL,
    uploaded_by_user_id UUID NOT NULL,
    approved_by_user_id UUID NULL,
    approved_at TIMESTAMPTZ NULL,
    archived_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_knowledge_documents_file_size_positive CHECK (file_size > 0),
    CONSTRAINT chk_knowledge_documents_chunk_count_non_negative CHECK (chunk_count IS NULL OR chunk_count >= 0),
    CONSTRAINT chk_knowledge_documents_embedding_dimensions_positive CHECK (embedding_dimensions IS NULL OR embedding_dimensions > 0),
    CONSTRAINT chk_knowledge_documents_public_global CHECK (access_level <> 'PUBLIC' OR operator_id IS NULL),
    CONSTRAINT chk_knowledge_documents_taxonomy CHECK (
        (access_level = 'PUBLIC' AND category = 'CUSTOMER_SUPPORT')
        OR (access_level = 'OPERATOR' AND category = 'OPERATOR_POLICY')
        OR (access_level = 'ADMIN' AND category = 'PLATFORM_ADMIN')
    )
);

CREATE INDEX idx_knowledge_documents_status
    ON knowledge_documents (status);
CREATE INDEX idx_knowledge_documents_access_status
    ON knowledge_documents (access_level, status);
CREATE INDEX idx_knowledge_documents_operator_access_status
    ON knowledge_documents (operator_id, access_level, status);
CREATE INDEX idx_knowledge_documents_category_status
    ON knowledge_documents (category, status);
CREATE INDEX idx_knowledge_documents_audience_roles
    ON knowledge_documents USING GIN (audience_roles);
CREATE INDEX idx_knowledge_documents_uploaded_by
    ON knowledge_documents (uploaded_by_user_id, created_at DESC);

CREATE TABLE knowledge_chunks (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id UUID NOT NULL REFERENCES knowledge_documents (id) ON DELETE CASCADE,
    operator_id UUID NULL,
    document_title VARCHAR(500) NOT NULL,
    section_header VARCHAR(500) NULL,
    document_type knowledge_document_type NOT NULL,
    chunk_index INT NOT NULL,
    content TEXT NOT NULL,
    token_count INT NOT NULL,
    embedding halfvec(3072) NOT NULL,
    search_vector TSVECTOR NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_knowledge_chunks_chunk_index_non_negative CHECK (chunk_index >= 0),
    CONSTRAINT chk_knowledge_chunks_token_count_positive CHECK (token_count > 0)
);

CREATE UNIQUE INDEX uq_knowledge_chunks_doc_index
    ON knowledge_chunks (document_id, chunk_index);
CREATE INDEX idx_knowledge_chunks_document_id
    ON knowledge_chunks (document_id);
CREATE INDEX idx_knowledge_chunks_operator_id
    ON knowledge_chunks (operator_id);
CREATE INDEX idx_knowledge_chunks_embedding_hnsw
    ON knowledge_chunks
    USING hnsw (embedding halfvec_cosine_ops);
CREATE INDEX idx_knowledge_chunks_search_vector
    ON knowledge_chunks
    USING GIN (search_vector);

CREATE TABLE rag_conversations (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL,
    operator_id UUID NULL,
    role rag_conversation_role NOT NULL,
    summary TEXT NULL,
    summary_updated_at TIMESTAMPTZ NULL,
    summary_from_message_id UUID NULL,
    started_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    last_message_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_rag_conversations_user_id_started_at
    ON rag_conversations (user_id, started_at DESC);
CREATE INDEX idx_rag_conversations_operator_id_started_at
    ON rag_conversations (operator_id, started_at DESC);
CREATE INDEX idx_rag_conversations_role
    ON rag_conversations (role);

CREATE TABLE rag_messages (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    conversation_id UUID NOT NULL REFERENCES rag_conversations (id) ON DELETE CASCADE,
    role rag_message_role NOT NULL,
    content TEXT NOT NULL,
    cited_chunk_ids UUID[] NOT NULL DEFAULT ARRAY[]::UUID[],
    tokens_used INT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_rag_messages_conversation_id_created_at
    ON rag_messages (conversation_id, created_at);

CREATE TABLE message_feedback (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    message_id UUID NOT NULL REFERENCES rag_messages (id) ON DELETE CASCADE,
    conversation_id UUID NOT NULL REFERENCES rag_conversations (id) ON DELETE CASCADE,
    user_id UUID NOT NULL,
    rating INT NOT NULL,
    query_rewritten TEXT NULL,
    chunk_ids UUID[] NOT NULL DEFAULT ARRAY[]::UUID[],
    response_length INT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_message_feedback_message_id UNIQUE (message_id),
    CONSTRAINT chk_message_feedback_rating CHECK (rating IN (-1, 1)),
    CONSTRAINT chk_message_feedback_response_length_non_negative CHECK (response_length >= 0)
);

CREATE INDEX idx_message_feedback_conversation_created_at
    ON message_feedback (conversation_id, created_at);
CREATE INDEX idx_message_feedback_user_created_at
    ON message_feedback (user_id, created_at DESC);

CREATE TABLE idempotency_operations (
    operation_id UUID PRIMARY KEY,
    user_id UUID NOT NULL,
    method VARCHAR(10) NOT NULL,
    path VARCHAR(500) NOT NULL,
    fingerprint CHAR(64) NOT NULL,
    owner_token UUID NOT NULL,
    status VARCHAR(20) NOT NULL,
    response_status INTEGER NULL,
    response_headers JSONB NULL,
    response_body TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    expires_at TIMESTAMPTZ NOT NULL
);

CREATE INDEX idx_idempotency_operations_user_created
    ON idempotency_operations (user_id, created_at DESC);
CREATE INDEX idx_idempotency_operations_expires_at
    ON idempotency_operations (expires_at);

CREATE TABLE policies (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    operator_id UUID NULL,
    title TEXT NOT NULL,
    description TEXT NOT NULL,
    content TEXT NOT NULL,
    policy_type policy_type NOT NULL,
    category TEXT NOT NULL,
    version INTEGER NOT NULL DEFAULT 1,
    active BOOLEAN NOT NULL DEFAULT TRUE,
    created_by_user_id UUID NOT NULL,
    created_by_display_name TEXT NOT NULL,
    created_by_email TEXT NOT NULL,
    row_version INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    deleted_at TIMESTAMPTZ NULL,
    CONSTRAINT chk_policies_version_positive CHECK (version > 0),
    CONSTRAINT chk_policies_row_version_non_negative CHECK (row_version >= 0)
);

CREATE INDEX idx_policies_tenant_deleted_updated
    ON policies (operator_id, deleted_at, updated_at DESC);
CREATE INDEX idx_policies_tenant_type_active
    ON policies (operator_id, policy_type, active);

CREATE TABLE policy_audit_logs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    policy_id UUID NOT NULL REFERENCES policies (id) ON DELETE RESTRICT ON UPDATE CASCADE,
    action policy_audit_action NOT NULL,
    before JSONB NULL,
    after JSONB NULL,
    actor JSONB NOT NULL,
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_policy_audit_logs_policy_occurred
    ON policy_audit_logs (policy_id, occurred_at);

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
    ON outbox_events (status, created_at);

CREATE TABLE runtime_configs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    key VARCHAR(120) NOT NULL UNIQUE,
    value JSONB NOT NULL,
    value_type VARCHAR(30) NOT NULL,
    editable_group VARCHAR(30) NOT NULL,
    risk_level VARCHAR(20) NOT NULL,
    requires_restart BOOLEAN NOT NULL DEFAULT FALSE,
    description TEXT NULL,
    updated_by_user_id UUID NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_runtime_configs_value_type CHECK (value_type IN ('string', 'number', 'bool', 'template', 'string_list')),
    CONSTRAINT chk_runtime_configs_editable_group CHECK (editable_group IN ('admin', 'ai_ops', 'readonly')),
    CONSTRAINT chk_runtime_configs_risk_level CHECK (risk_level IN ('low', 'medium', 'high'))
);

CREATE TABLE runtime_config_history (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    key VARCHAR(120) NOT NULL,
    old_value JSONB NULL,
    new_value JSONB NOT NULL,
    changed_by_user_id UUID NULL,
    reason TEXT NULL,
    changed_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_runtime_config_history_key_changed_at
    ON runtime_config_history (key, changed_at DESC);

-- =============================================================================
-- TRIGGERS
-- =============================================================================

CREATE OR REPLACE FUNCTION trg_set_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = now();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_knowledge_documents_updated_at
    BEFORE UPDATE ON knowledge_documents
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();

CREATE TRIGGER trg_message_feedback_updated_at
    BEFORE UPDATE ON message_feedback
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();

CREATE TRIGGER trg_runtime_configs_updated_at
    BEFORE UPDATE ON runtime_configs
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();

CREATE TRIGGER trg_policies_updated_at
    BEFORE UPDATE ON policies
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();

CREATE OR REPLACE FUNCTION reject_policy_audit_mutation()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'policy_audit_logs is immutable' USING ERRCODE = '55000';
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_policy_audit_logs_immutable
    BEFORE UPDATE OR DELETE ON policy_audit_logs
    FOR EACH ROW EXECUTE FUNCTION reject_policy_audit_mutation();

COMMENT ON COLUMN knowledge_documents.storage_path IS
    'Cloudinary public_id/path for raw document asset. Do not persist long-lived signed URLs.';
COMMENT ON COLUMN knowledge_chunks.embedding IS
    'halfvec(3072) for ShopAIKey gemini-embedding-2-preview native output; HNSW cosine index enabled.';
COMMENT ON COLUMN knowledge_chunks.search_vector IS
    'PostgreSQL full-text search vector for optional hybrid search.';
COMMENT ON COLUMN rag_messages.cited_chunk_ids IS
    'UUID[] of knowledge_chunks used to generate ASSISTANT response. Empty array for USER messages.';
