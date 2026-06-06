-- =============================================================================
-- VietRide :: RAG AI Service :: PostgreSQL 16 schema
-- Database: vietride_rag
-- Schema: vietride_rag
-- Framework: NestJS + Prisma ORM
-- =============================================================================
-- pgvector extension required for embedding similarity search.
-- Embedding model: OpenAI text-embedding-3-small (1536 dimensions).
-- =============================================================================

CREATE EXTENSION IF NOT EXISTS "pgcrypto";
CREATE EXTENSION IF NOT EXISTS "vector";

CREATE SCHEMA IF NOT EXISTS vietride_rag;
SET search_path TO vietride_rag, public;

-- =============================================================================
-- ENUMS
-- =============================================================================

CREATE TYPE knowledge_document_status AS ENUM (
    'PENDING_REVIEW', 'APPROVED', 'REJECTED', 'ARCHIVED'
);

CREATE TYPE knowledge_document_access AS ENUM ('PUBLIC', 'OPERATOR', 'ADMIN');

CREATE TYPE knowledge_document_file_type AS ENUM ('PDF', 'DOCX', 'TXT', 'MARKDOWN');

CREATE TYPE rag_conversation_role AS ENUM (
    'PASSENGER', 'DRIVER', 'ASSISTANT',
    'OPERATOR_STAFF', 'OPERATOR_ADMIN', 'SYSTEM_ADMIN'
);

CREATE TYPE rag_message_role AS ENUM ('USER', 'ASSISTANT');

CREATE TYPE outbox_event_status AS ENUM ('PENDING', 'PUBLISHING', 'PUBLISHED', 'FAILED');

-- =============================================================================
-- TABLES
-- =============================================================================

-- -----------------------------------------------------------------------------
-- knowledge_documents
-- -----------------------------------------------------------------------------
CREATE TABLE knowledge_documents (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    title VARCHAR(500) NOT NULL,
    description TEXT NULL,
    file_url TEXT NOT NULL,                -- Firebase Storage signed URL or path
    file_type knowledge_document_file_type NOT NULL,
    access_level knowledge_document_access NOT NULL,
    status knowledge_document_status NOT NULL DEFAULT 'PENDING_REVIEW',
    uploaded_by_user_id UUID NOT NULL,    -- logical FK
    approved_by_user_id UUID NULL,         -- logical FK SYSTEM_ADMIN
    approved_at TIMESTAMPTZ NULL,
    archived_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_knowledge_documents_status ON knowledge_documents (status);
CREATE INDEX idx_knowledge_documents_access_status
    ON knowledge_documents (access_level, status);
CREATE INDEX idx_knowledge_documents_uploaded_by
    ON knowledge_documents (uploaded_by_user_id, created_at DESC);

-- -----------------------------------------------------------------------------
-- knowledge_chunks (embedded text chunks with pgvector)
-- -----------------------------------------------------------------------------
CREATE TABLE knowledge_chunks (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id UUID NOT NULL REFERENCES knowledge_documents (id) ON DELETE CASCADE,
    chunk_index INT NOT NULL,
    content TEXT NOT NULL,
    token_count INT NOT NULL,
    embedding vector(1536) NOT NULL,    -- OpenAI text-embedding-3-small
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT chk_knowledge_chunks_chunk_index_non_negative CHECK (chunk_index >= 0),
    CONSTRAINT chk_knowledge_chunks_token_count_positive CHECK (token_count > 0)
);

CREATE UNIQUE INDEX uq_knowledge_chunks_doc_index
    ON knowledge_chunks (document_id, chunk_index);
CREATE INDEX idx_knowledge_chunks_document_id ON knowledge_chunks (document_id);

-- IVFFlat index for vector similarity search (cosine distance)
-- lists=100 is a reasonable starting point; tune based on row count
-- (rule of thumb: lists ≈ sqrt(rows)). REINDEX may be needed as table grows.
CREATE INDEX idx_knowledge_chunks_embedding
    ON knowledge_chunks
    USING ivfflat (embedding vector_cosine_ops)
    WITH (lists = 100);

COMMENT ON COLUMN knowledge_chunks.embedding IS
    'vector(1536) — OpenAI text-embedding-3-small. Use <=> operator for cosine distance.';
COMMENT ON INDEX idx_knowledge_chunks_embedding IS
    'IVFFlat cosine. Adjust lists parameter as table grows (sqrt(rows) heuristic).';

-- -----------------------------------------------------------------------------
-- rag_conversations
-- -----------------------------------------------------------------------------
CREATE TABLE rag_conversations (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL,    -- logical FK
    role rag_conversation_role NOT NULL,
    started_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    last_message_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_rag_conversations_user_id_started_at
    ON rag_conversations (user_id, started_at DESC);
CREATE INDEX idx_rag_conversations_role ON rag_conversations (role);

-- -----------------------------------------------------------------------------
-- rag_messages
-- -----------------------------------------------------------------------------
CREATE TABLE rag_messages (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    conversation_id UUID NOT NULL REFERENCES rag_conversations (id) ON DELETE CASCADE,
    role rag_message_role NOT NULL,
    content TEXT NOT NULL,
    cited_chunk_ids UUID[] NULL,    -- audit trail for ASSISTANT messages
    tokens_used INT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_rag_messages_conversation_id_created_at
    ON rag_messages (conversation_id, created_at);

COMMENT ON COLUMN rag_messages.cited_chunk_ids IS
    'UUID[] of knowledge_chunks used to generate ASSISTANT response. NULL for USER messages.';

-- -----------------------------------------------------------------------------
-- outbox_events (DocumentApproved → trigger ingest pipeline)
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

-- =============================================================================
-- TRIGGERS
-- =============================================================================

CREATE OR REPLACE FUNCTION trg_set_updated_at()
RETURNS TRIGGER AS $$
BEGIN NEW.updated_at = now(); RETURN NEW; END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_knowledge_documents_updated_at
    BEFORE UPDATE ON knowledge_documents
    FOR EACH ROW EXECUTE FUNCTION trg_set_updated_at();
