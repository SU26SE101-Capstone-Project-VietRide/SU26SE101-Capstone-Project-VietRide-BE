# RAG AI - ERD Drawing Guide

## Statistics

- **Total tables:** 6
- **Total intra-service FK:** 4
- **Hub tables:** `KnowledgeDocument` (1 inbound), `RagConversation` (2 inbound), `RagMessage` (1 inbound)
- **Leaf tables:** `KnowledgeChunk`, `MessageFeedback`, `OutboxEvent`

## Recommended Layout Zones

| Zone | Tables | Vị trí gợi ý |
|---|---|---|
| Knowledge base (left) | `KnowledgeDocument`, `KnowledgeChunk` | trái |
| Conversation (right) | `RagConversation`, `RagMessage`, `MessageFeedback` | phải |
| Reliability (bottom) | `OutboxEvent` | dưới |

## Drawing Order

### Phase 1 - Intra-Service

| # | From (FK) | To (PK) | Cardinality | Note |
|---|---|---|---|---|
| 1 | `KnowledgeChunk.documentId` | `KnowledgeDocument.id` | N:1 | CASCADE; HNSW halfvec cosine index |
| 2 | `RagMessage.conversationId` | `RagConversation.id` | N:1 | CASCADE |
| 3 | `MessageFeedback.messageId` | `RagMessage.id` | 1:1 | CASCADE; unique feedback per message |
| 4 | `MessageFeedback.conversationId` | `RagConversation.id` | N:1 | CASCADE; audit/query by conversation |

### Phase 2 - Cross-Service Logical FK (không vẽ)

- `KnowledgeDocument.uploadedByUserId/approvedByUserId` -> `identity.User.id`
- `KnowledgeDocument.operatorId`, `KnowledgeChunk.operatorId`, `RagConversation.operatorId` -> Operator service aggregate
- `RagConversation.userId`, `MessageFeedback.userId` -> `identity.User.id`
- `RagMessage.citedChunkIds[]`, `MessageFeedback.chunkIds[]` -> `KnowledgeChunk.id` dạng logical array, không vẽ line FK.

Xem `_global/cross-service-references.md`.

## Drawing Tips

1. Tách 3 cluster rõ: Knowledge base, Conversation/Feedback, Reliability.
2. Note `KnowledgeChunk.embedding` là `halfvec(2048)` cho pgvector và ShopAIKey model `gemini-embedding-2-preview` qua API tương thích OpenAI.
3. Note `KnowledgeDocument.storagePath` là Cloudinary public_id/path, không phải URL storage cũ.
4. `RagMessage.citedChunkIds` và `MessageFeedback.chunkIds` là UUID[] logical reference, không vẽ line FK.

## Validation Checklist

- [ ] 4 line cho 4 intra-service FK.
- [ ] KnowledgeChunk hiển thị column `embedding halfvec(2048)` rõ.
- [ ] Note pgvector extension required.
- [ ] Note Cloudinary storage path, không ghi URL storage cũ.
- [ ] RagMessage.citedChunkIds và MessageFeedback.chunkIds note logical array reference.
