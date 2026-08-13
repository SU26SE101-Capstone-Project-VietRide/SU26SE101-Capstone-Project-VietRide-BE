DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM vietride_rag.knowledge_chunks LIMIT 1) THEN
        RAISE EXCEPTION
            'Cannot change knowledge_chunks.embedding to halfvec(3072): delete and re-ingest all knowledge documents first';
    END IF;
END
$$;

DROP INDEX IF EXISTS vietride_rag.idx_knowledge_chunks_embedding_hnsw;

ALTER TABLE vietride_rag.knowledge_chunks
    ALTER COLUMN embedding TYPE halfvec(3072)
    USING embedding::halfvec(3072);

CREATE INDEX idx_knowledge_chunks_embedding_hnsw
    ON vietride_rag.knowledge_chunks
    USING hnsw (embedding halfvec_cosine_ops);

COMMENT ON COLUMN vietride_rag.knowledge_chunks.embedding IS
    'halfvec(3072) for ShopAIKey gemini-embedding-2-preview native output; HNSW cosine index enabled.';
