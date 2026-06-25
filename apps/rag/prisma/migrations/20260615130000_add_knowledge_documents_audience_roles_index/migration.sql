-- Add GIN index on audience_roles for array contains queries in chat retrieval
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_knowledge_documents_audience_roles
    ON vietride_rag.knowledge_documents USING GIN (audience_roles);
