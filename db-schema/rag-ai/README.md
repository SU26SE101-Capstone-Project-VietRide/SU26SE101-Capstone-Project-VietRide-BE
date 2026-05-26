# RAG AI Service — DB Schema

## Overview

NestJS service xử lý **knowledge base ingestion + LLM streaming RAG**. Ingest pipeline: upload file → System Admin approve → extract text → chunk + embed (OpenAI text-embedding-3-small 1536d) → store với pgvector. Query: embed user message → cosine similarity top-5 chunks → LLM (claude-sonnet-4-6) stream SSE.

- **Database:** `vietride_rag`
- **Framework:** NestJS + TypeORM
- **Extensions:** `pgcrypto`, **`vector` (pgvector)**
- **Background jobs:** BullMQ (NestJS, KHÔNG dùng Hangfire). Job ingest pipeline triggered by `DocumentApproved` event.
- **Hangfire schema:** KHÔNG có.

## Entity List

| Entity | Purpose | Key business fields |
|---|---|---|
| `KnowledgeDocument` | File upload metadata. | `fileUrl` Firebase Storage, `fileType` enum, `accessLevel` (PUBLIC/OPERATOR/ADMIN), `status` enum |
| `KnowledgeChunk` | Đoạn text + `embedding vector(1536)`. | composite unique `(documentId, chunkIndex)`, IVFFlat cosine index |
| `RagConversation` | 1 session chat. | `role` enum (xác định access filter), `lastMessageAt` |
| `RagMessage` | 1 turn USER/ASSISTANT. | `citedChunkIds` UUID[] audit |
| `OutboxEvent` | `DocumentApproved` event publish. | |

## Design Decisions

- **pgvector extension** required — `CREATE EXTENSION IF NOT EXISTS "vector"` ở đầu schema.sql.
- **`KnowledgeChunk.embedding vector(1536)`** — dimension matches OpenAI text-embedding-3-small. KHÔNG được đổi dimension mà không re-embed full corpus.
- **IVFFlat index `WITH (lists = 100)`** — starting point. Heuristic: `lists ≈ sqrt(rows)`. Cần `REINDEX` khi table grow đáng kể (vd > 100k rows nên tăng lists). Trade-off: HNSW có recall tốt hơn nhưng IVFFlat đủ cho scale capstone + đơn giản hơn.
- **Cosine distance operator `<=>`** dùng cho similarity search (v6 spec). Query pattern:
  ```sql
  SELECT id, document_id, content
  FROM knowledge_chunks kc
  JOIN knowledge_documents kd ON kc.document_id = kd.id
  WHERE kd.access_level = ANY(:allowedLevels) AND kd.status = 'APPROVED'
  ORDER BY embedding <=> :query_embedding
  LIMIT 5;
  ```
- **`KnowledgeChunk.document_id` CASCADE DELETE** — xóa document → xóa chunk. Phù hợp vì chunk không có ý nghĩa ngoài document.
- **`RagConversation.role` enum** — copy role của user tại lúc tạo conversation; dùng để filter `KnowledgeDocument.accessLevel`. Nếu user role đổi giữa conversation (rất hiếm), conversation cũ giữ role cũ.
- **`RagMessage.cited_chunk_ids` UUID[]** — array thay vì junction table (simpler, query ít, không cần JOIN). KHÔNG enforce FK ở DB layer.
- **`KnowledgeDocument.uploaded_by_user_id` NOT NULL** — chỉ SYSTEM_ADMIN upload trong v1; vẫn bắt buộc cho audit.

## Index Strategy

| Index | Columns | Type | Purpose |
|---|---|---|---|
| `idx_knowledge_documents_status` | `status` | B-tree | Filter APPROVED for query |
| `idx_knowledge_documents_access_status` | `(access_level, status)` | B-tree | Retrieval access filter |
| `idx_knowledge_documents_uploaded_by` | `(uploaded_by_user_id, created_at DESC)` | B-tree | Admin audit |
| `uq_knowledge_chunks_doc_index` | `(document_id, chunk_index)` | unique | Avoid duplicate chunk |
| `idx_knowledge_chunks_document_id` | `document_id` | B-tree | List chunks of document |
| `idx_knowledge_chunks_embedding` | `embedding` | **IVFFlat cosine** | Vector similarity search |
| `idx_rag_conversations_user_id_started_at` | `(user_id, started_at DESC)` | B-tree | "My RAG sessions" history |
| `idx_rag_conversations_role` | `role` | B-tree | Analytics per role |
| `idx_rag_messages_conversation_id_created_at` | `(conversation_id, created_at)` | B-tree | Message order in conversation |
| `idx_outbox_events_status_created` | partial | B-tree | Outbox poll |

## Cross-service References (Logical FK)

| Column | References | Enforcement |
|---|---|---|
| `KnowledgeDocument.uploadedByUserId/approvedByUserId`, `RagConversation.userId` | `identity.User.id` | app-layer |

## Migration Strategy

- **Tool:** TypeORM migrations.
- **Bootstrap order:** Sau Identity (logical FK).
- **pgvector setup:** `CREATE EXTENSION IF NOT EXISTS "vector"` must be installed in PostgreSQL container. Docker compose: `docker pull pgvector/pgvector:pg16` (community image bundled with extension).
- **IVFFlat retraining:** Run `REINDEX INDEX idx_knowledge_chunks_embedding;` after major data growth (10x rows since last reindex).
- **Embedding model migration (v2):** Nếu đổi model dimension (vd sang `text-embedding-3-large` 3072d) → cần ALTER TABLE drop column + re-embed full corpus. Plan: keep both columns + dual-write trong window migration.

## Open Questions

Không có. Section 6.8 + Section 8 đã spec đầy đủ.
