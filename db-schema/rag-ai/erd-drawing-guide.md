# RAG AI — ERD Drawing Guide

## Statistics
- **Total tables:** 5
- **Total intra-service FK:** 2
- **Hub tables:** `KnowledgeDocument` (1 inbound), `RagConversation` (1 inbound)
- **Leaf tables:** `OutboxEvent`

## Recommended Layout Zones

| Zone | Tables | Vị trí gợi ý |
|---|---|---|
| Knowledge base (left) | `KnowledgeDocument`, `KnowledgeChunk` | trái |
| Conversation (right) | `RagConversation`, `RagMessage` | phải |
| Reliability (bottom) | `OutboxEvent` | dưới |

## Drawing Order

### Phase 1 — Intra-service

| # | From (FK) | To (PK) | Cardinality | Note |
|---|---|---|---|---|
| 1 | `KnowledgeChunk.documentId` | `KnowledgeDocument.id` | N:1 | CASCADE; ivfflat embedding index |
| 2 | `RagMessage.conversationId` | `RagConversation.id` | N:1 | CASCADE |

### Phase 2 — Cross-Service Logical FK (KHÔNG vẽ)

- `KnowledgeDocument.uploadedByUserId/approvedByUserId` → `identity.User.id`
- `RagConversation.userId` → `identity.User.id`
- `RagMessage.citedChunkIds[]` → `KnowledgeChunk.id` (array, intra-service nhưng polymorphic — KHÔNG vẽ line, note trong drawio)

Xem `_global/cross-service-references.md`.

## Drawing Tips

1. **2 cluster riêng biệt** — Knowledge base (Document → Chunk) bên trái, Conversation (Conversation → Message) bên phải. Không có FK cross 2 cluster (chỉ logical array reference).
2. Note `KnowledgeChunk.embedding` là `vector(1536)` cho pgvector — đặc biệt cần annotation.
3. `RagMessage.cited_chunk_ids` là UUID[] array — không vẽ line, note "logical reference" trong drawio.

## Validation Checklist

- [ ] 2 line cho 2 intra-service FK
- [ ] KnowledgeChunk hiển thị column `embedding vector(1536)` rõ
- [ ] Note pgvector extension required
- [ ] RagMessage.citedChunkIds note polymorphic array reference
