# RAG AI Service - DB Schema

## Overview

NestJS service xử lý knowledge base ingestion và LLM streaming RAG. Ingest pipeline: upload file lên Cloudinary raw asset -> System Admin approve -> extract text -> chunk + embed bằng OpenRouter embedding model -> lưu với pgvector. Query: embed user message -> vector similarity top-k chunks -> OpenRouter chat model stream SSE.

- **Database:** `vietride_rag`
- **Schema:** `vietride_rag`
- **Framework:** NestJS + Prisma
- **Extensions:** `pgcrypto`, `vector` (pgvector)
- **Storage:** Cloudinary raw assets, DB chỉ lưu `storage_path`/metadata, không lưu signed URL dài hạn.
- **Chat model thử nghiệm:** `nvidia/nemotron-3-ultra-550b-a55b:free`
- **Embedding model thử nghiệm:** `nvidia/llama-nemotron-embed-vl-1b-v2:free`
- **Embedding dimension hiện tại:** `2048`

## Entity List

| Entity | Purpose | Key business fields |
|---|---|---|
| `KnowledgeDocument` | Metadata tài liệu upload. | `storageProvider`, `storagePath`, `fileType`, `accessLevel`, `category`, `documentType`, `audienceRoles`, `operatorId`, `status`, `ingestStatus` |
| `KnowledgeChunk` | Đoạn text đã chunk + embedding. | `embedding halfvec(2048)`, `searchVector`, `operatorId`, `documentType`, unique `(documentId, chunkIndex)` |
| `RagConversation` | 1 session chat. | `userId`, `operatorId`, `role`, `summary`, `lastMessageAt` |
| `RagMessage` | 1 turn USER/ASSISTANT. | `citedChunkIds` audit nội bộ, `tokensUsed` |
| `MessageFeedback` | Feedback cho ASSISTANT message. | `rating`, `chunkIds`, `queryRewritten`, `responseLength` |
| `OutboxEvent` | Outbox để trigger ingest/publish event. | `eventType`, `payload`, `status`, `retryCount` |
| `Policy` | Chính sách generic cho platform hoặc một operator tenant. | `operatorId`, `policyType`, `category`, `version`, `active`, `deletedAt` |
| `PolicyAuditLog` | Nhật ký bất biến cho mọi mutation Policy. | `action`, `before`, `after`, `actor`, `occurredAt` |

## Data Taxonomy

- `PUBLIC`: CSKH cho hành khách, `category = CUSTOMER_SUPPORT`, `operator_id IS NULL`.
- `OPERATOR`: policy/SOP cho nhà xe, `category = OPERATOR_POLICY`, có thể global hoặc theo `operator_id`.
- `ADMIN`: tài liệu quản trị platform, `category = PLATFORM_ADMIN`.

Rule retrieval production:

- `PASSENGER`: chỉ lấy `PUBLIC`, `CUSTOMER_SUPPORT`, `operator_id IS NULL`.
- `DRIVER`, `ASSISTANT`, `OPERATOR_STAFF`, `OPERATOR_ADMIN`: lấy `PUBLIC` và `OPERATOR`; tài liệu operator chỉ được lấy khi `operator_id IS NULL OR operator_id = caller.operatorId`.
- `SYSTEM_ADMIN`: được lấy `PUBLIC`, `OPERATOR`, `ADMIN`.

## Design Decisions

- `KnowledgeChunk.embedding halfvec(2048)` khớp kết quả probe thực tế của OpenRouter embedding model `nvidia/llama-nemotron-embed-vl-1b-v2:free`. Nếu đổi sang model dimension khác phải migration và re-embed corpus.
- `KnowledgeChunk.search_vector` là `tsvector` để chuẩn bị hybrid search phía sau feature flag, không thay thế vector search ở Phase 2.
- Tạo HNSW index `idx_knowledge_chunks_embedding_hnsw` với `halfvec_cosine_ops` để hỗ trợ vector search cho embedding 2048 chiều.
- `KnowledgeDocument.storage_path` lưu Cloudinary public_id/path. API mới tạo signed/controlled URL ngắn hạn khi cần preview.
- `KnowledgeDocument.access_level + category` có CHECK constraint để tránh nhầm CSKH hành khách với policy nhà xe.
- `KnowledgeDocument.operator_id` bắt buộc NULL với `PUBLIC`, tránh leak tài liệu passenger theo tenant sai.
- Cross-service references như `uploaded_by_user_id`, `approved_by_user_id`, `user_id`, `operator_id` là logical FK, không tạo FK cross-DB.
- `Policy.operator_id` là logical tenant key: `NULL` cho platform và UUID cho operator; không tạo FK sang Identity.
- `PolicyAuditLog` được ghi cùng transaction với Policy và trigger DB chặn UPDATE/DELETE để giữ audit bất biến.
- `RagMessage.cited_chunk_ids` dùng `UUID[]` để audit citation đơn giản, không enforce FK.
- API chat không trả `cited_chunk_ids` cho client. SSE `done` chỉ trả metadata thân thiện
  `citations[{ title, section }]`; UUID tiếp tục được giữ nội bộ cho feedback và điều tra.
- `MessageFeedback.rating` chỉ nhận `-1` hoặc `1`.

## Index Strategy

| Index | Columns | Type | Purpose |
|---|---|---|---|
| `idx_knowledge_documents_status` | `status` | B-tree | Filter APPROVED/PENDING_REVIEW |
| `idx_knowledge_documents_access_status` | `(access_level, status)` | B-tree | Retrieval access filter |
| `idx_knowledge_documents_operator_access_status` | `(operator_id, access_level, status)` | B-tree | Tenant-safe retrieval |
| `idx_knowledge_documents_category_status` | `(category, status)` | B-tree | Taxonomy filter |
| `idx_knowledge_documents_uploaded_by` | `(uploaded_by_user_id, created_at DESC)` | B-tree | Admin audit |
| `uq_knowledge_chunks_doc_index` | `(document_id, chunk_index)` | Unique | Tránh duplicate chunk |
| `idx_knowledge_chunks_document_id` | `document_id` | B-tree | List chunks của document |
| `idx_knowledge_chunks_operator_id` | `operator_id` | B-tree | Tenant filter |
| `idx_knowledge_chunks_embedding_hnsw` | `embedding` | HNSW cosine | Vector similarity search |
| `idx_knowledge_chunks_search_vector` | `search_vector` | GIN | Hybrid search/FTS |
| `idx_rag_conversations_user_id_started_at` | `(user_id, started_at DESC)` | B-tree | Conversation history |
| `idx_rag_conversations_operator_id_started_at` | `(operator_id, started_at DESC)` | B-tree | Operator audit/history |
| `idx_rag_messages_conversation_id_created_at` | `(conversation_id, created_at)` | B-tree | Message order |
| `idx_message_feedback_user_created_at` | `(user_id, created_at DESC)` | B-tree | Feedback audit |
| `idx_outbox_events_status_created` | `(status, created_at)` | B-tree | Outbox poll |

## Migration Strategy

- Production deploy dùng Prisma migration trong `apps/rag/prisma/migrations`.
- Baseline migration chỉ tạo extension/schema. Phase 2 migration tạo toàn bộ domain schema.
- Không dùng `prisma db push` để hoàn tất production change.
- Local/dev chạy từ thư mục `apps/rag`: `npx prisma migrate dev --schema=prisma/schema.prisma`.
- Staging/production chạy từ thư mục `apps/rag`: `npx prisma migrate deploy --schema=prisma/schema.prisma`.
- Nếu đổi embedding dimension, phải thêm migration riêng và re-embed toàn bộ corpus trước khi bật traffic thật.
