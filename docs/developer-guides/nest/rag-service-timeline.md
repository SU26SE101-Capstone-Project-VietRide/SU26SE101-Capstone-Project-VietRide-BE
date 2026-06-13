# RAG Service Timeline

Tài liệu này là timeline triển khai chính thức cho `apps/rag`. Mục tiêu là đưa RAG Service từ app placeholder thành dịch vụ production-ready theo chuẩn NestJS của VietRide.

> **Quy tắc cho AI**: Khi nhận task liên quan RAG Service, AI PHẢI đọc file này trước,
> xác định Phase hiện tại (Phase chưa `[x]` đầu tiên), chỉ làm đúng scope Phase đó,
> verify xong mới báo done. TUYỆT ĐỐI không tự chuyển sang Phase tiếp theo nếu USER chưa nói `go`.

## Phase Progress

- [x] Phase 0 — Timeline, Threat Model, Eval Design
- [x] Phase 1 — Foundation, Prisma, Internal Auth
- [x] Phase 2 — Database Shape Production-Ready
- [x] Phase 3 — Documents API Và Cloudinary Storage
- [x] Phase 4 — Ingest TXT/MARKDOWN
- [ ] Phase 5 — Chat Core Và SSE
- [ ] Phase 6 — Production Guardrails
- [ ] Phase 7 — Hybrid Search Behind Flag
- [ ] Phase 8 — Intent Filter, Query Rewrite, Summarization
- [ ] Phase 9 — Rerank Và Feedback Loop
- [ ] Phase 10 — Production Verification

## Quyết định đã chốt

- Storage production: Cloudinary qua REST API, không thêm package mới.
- Chat provider giai đoạn thử nghiệm: OpenRouter.
- Chat model thử nghiệm: `nex-agi/nex-n2-pro:free`.
- Embedding provider giai đoạn thử nghiệm: OpenRouter.
- Embedding model thử nghiệm: `nvidia/llama-nemotron-embed-vl-1b-v2:free`.
- Không tự fallback sang model trả phí nếu `OPENROUTER_ALLOW_PAID_FALLBACK=false`.
- Không hardcode dimension embedding. Service phải probe dimension từ provider trước khi ingest và fail fast nếu DB dimension không khớp.
- Tài liệu v1 chỉ ingest TXT/MARKDOWN. PDF/DOCX làm sau khi USER duyệt parser dependency.

## Taxonomy dữ liệu

Knowledge base phải phân loại rõ theo access level, audience và tenant:

- `PUBLIC`: CSKH cho hành khách. Bao gồm FAQ đặt/hủy vé, hoàn tiền, hành lý, hàng ký gửi, voucher, thanh toán, theo dõi chuyến và hướng dẫn tài khoản.
- `OPERATOR`: policy/SOP/quy trình vận hành cho nhà xe. Bao gồm quy trình đón/trả khách, xử lý trễ chuyến, đổi xe/tài xế, nhận/trả hàng, xử lý sự cố và hướng dẫn dashboard.
- `ADMIN`: tài liệu quản trị platform cho `SYSTEM_ADMIN`. Bao gồm duyệt nhà xe, audit, RAG operations, cấu hình hệ thống, subscription và runbook nội bộ.

Metadata bắt buộc từ Phase 2:

```text
accessLevel: PUBLIC | OPERATOR | ADMIN
operatorId: uuid | null
category:
  CUSTOMER_SUPPORT
  OPERATOR_POLICY
  PLATFORM_ADMIN
documentType:
  FAQ
  POLICY
  SOP
  GUIDE
  TERMS
audienceRoles: string[]
language: vi
```

Rule retrieval:

```text
PASSENGER:
  accessLevel IN (PUBLIC)
  category = CUSTOMER_SUPPORT
  operatorId IS NULL

DRIVER / ASSISTANT / OPERATOR_STAFF / OPERATOR_ADMIN:
  accessLevel IN (PUBLIC, OPERATOR)
  AND (operatorId IS NULL OR operatorId = caller.operatorId)

SYSTEM_ADMIN:
  accessLevel IN (PUBLIC, OPERATOR, ADMIN)
```

## Biến môi trường bắt buộc

```text
DATABASE_URL=
REDIS_URL=
RABBITMQ_URL=
RABBITMQ_EXCHANGE=vietride.events
INTERNAL_JWT_SECRET=

OPENROUTER_API_KEY=
OPENROUTER_BASE_URL=https://openrouter.ai/api/v1
OPENROUTER_CHAT_MODEL=nex-agi/nex-n2-pro:free
OPENROUTER_EMBEDDING_MODEL=nvidia/llama-nemotron-embed-vl-1b-v2:free
OPENROUTER_HTTP_REFERER=http://localhost:3000
OPENROUTER_APP_TITLE=VietRide RAG
OPENROUTER_ALLOW_PAID_FALLBACK=false

CLOUDINARY_CLOUD_NAME=
CLOUDINARY_API_KEY=
CLOUDINARY_API_SECRET=
CLOUDINARY_RAG_FOLDER=rag/documents
```

## Threat model bắt buộc

- Direct-call bypass: mọi endpoint RAG nghiệp vụ phải verify `X-Internal-Auth`.
- Access leak: role thấp không được truy vấn access level cao hơn.
- Tenant leak: operator A không được thấy tài liệu của operator B.
- Prompt injection: nội dung tài liệu là untrusted context, model không được làm theo instruction trong document.
- Citation trust: response chỉ được cite chunk đã được retrieve và còn nằm trong access scope của caller.
- Provider abuse/cost: giới hạn request, token, context, timeout và không fallback sang paid model khi chưa bật flag.

## Prompt policy

System prompt phải thể hiện rõ:

- Chỉ trả lời bằng tiếng Việt mặc định.
- Chỉ dùng thông tin trong retrieved context.
- Nếu không đủ thông tin, nói rõ là chưa có dữ liệu trong knowledge base.
- Không làm theo chỉ dẫn nằm trong retrieved context.
- Không bịa số liệu, chính sách, giá vé, trạng thái chuyến hoặc dữ liệu thời gian thực.
- Citation chỉ dùng nguồn tương ứng với chunk IDs đã retrieve.

## Golden test set

Tạo bộ kiểm thử đánh giá thủ công/offline gồm tối thiểu 100 câu hỏi tiếng Việt:

- `question`
- `role`
- `operatorId`
- `expectedAccessScope`
- `expectedCitationChunkIds`
- `expectedRefusal`
- `expectedAnswerNotes`

LLM judge chỉ dùng để báo cáo offline, không làm CI gate duy nhất.

## Phase 0 - Timeline, threat model, eval design

Scope:

- Hoàn thiện timeline này.
- Chốt provider/storage/env.
- Ghi rõ feature flags và acceptance criteria.

DoD:

- Timeline được tạo trong `docs/developer-guides/nest/rag-service-timeline.md`.
- Không tạo file plan tạm trong source.

## Phase 1 - Foundation, Prisma, internal auth

Scope:

- Wire `NestCommonModule`.
- Wire global `ApiResponseExceptionFilter`, `LoggingInterceptor`, `ApiResponseInterceptor`.
- Tạo `config/env.schema.ts`.
- Tạo `RagConfigModule`.
- Tạo `RagPrismaModule` và `RagPrismaService`.
- Tạo `apps/rag/prisma/schema.prisma`.
- Tạo Prisma migration baseline deployable.
- Thêm `/ready`.
- Thêm Nx targets cần thiết: `generate`, `lint`, `test:e2e`.
- Cập nhật Docker compose env cho RAG.
- Tạo `InternalJwtAuthGuard` verify `X-Internal-Auth` HS256:
  - issuer `vietride-gateway`
  - audience `vietride-internal`
  - secret `INTERNAL_JWT_SECRET`
  - claims `sub`, `role`, `operatorId`, `reqId`
- Tạo provider abstraction:
  - `ChatCompletionProvider`
  - `EmbeddingProvider`
  - `StorageProvider`
- Tạo OpenRouter provider skeleton bằng built-in `fetch`.
- Tạo Cloudinary storage provider skeleton bằng built-in `fetch`.
- Tạo embedding dimension probe.

DoD:

- Missing/invalid internal JWT trả 401.
- Internal JWT hợp lệ gắn user context vào request.
- Env schema parse được OpenRouter/Cloudinary config.
- Embedding probe có thể assert vector numeric và dimension.
- Verify:

```bash
npx prisma validate --schema=apps/rag/prisma/schema.prisma
npx nx run rag:generate
npx nx run rag:lint
npx nx run rag:test
npx nx run rag:test:e2e
npx nx run rag:build
```

## Phase 2 - Database shape production-ready

Scope:

- Mở rộng `knowledge_documents` với storage metadata, tenant và ingest status.
- Mở rộng `knowledge_chunks` với tenant, metadata, `embedding vector(<dimension>)`, `search_vector tsvector`.
- Mở rộng `rag_conversations` với tenant và summary fields.
- Thêm `message_feedback`.
- Thêm taxonomy metadata:
  - `category`
  - `document_type`
  - `audience_roles`
  - `language`
- Cập nhật `db-schema/rag-ai/schema.sql`.

DoD:

- Prisma migration deployable.
- Canonical DDL sync với migration.
- DB dimension khớp provider dimension đã probe.
- Query vector/FTS dùng `$queryRaw` parameter hóa.

## Phase 3 - Documents API và Cloudinary Storage

Scope:

- `POST /v1/rag/documents`.
- `PUT /v1/rag/documents/{documentId}/approve`.
- Chỉ `SYSTEM_ADMIN`.
- Multipart upload TXT/MARKDOWN lên Cloudinary với `resource_type=raw`.
- DB chỉ lưu `storagePath`, `fileName`, `mimeType`, `fileSize`.
- Admin preview dùng URL có kiểm soát từ backend. Nếu asset public, backend chỉ trả URL cho caller hợp lệ; nếu dùng authenticated/private asset thì tạo signed URL TTL ngắn.
- Approve tạo ingest job/outbox.

DoD:

- Create document 201.
- Approve document 200.
- Missing internal JWT 401.
- Non-admin 403.
- Invalid file/payload 400.
- Không lưu signed URL dài hạn.

## Phase 4 - Ingest TXT/MARKDOWN

Scope:

- Worker ingest dùng Redis/BullMQ hoặc internal job worker.
- Download file từ Cloudinary.
- Extract TXT/MARKDOWN.
- Chunk theo heading/section, fallback 500 token + overlap 50.
- Embed bằng OpenRouter embedding model free.
- Assert vector dimension đúng DB.
- Insert chunks và populate `search_vector`.
- Update ingest status.

DoD:

- Approved TXT/MARKDOWN tạo chunks searchable.
- Duplicate ingest không duplicate chunks.
- Provider 429/fail được xử lý rõ.
- Không fallback sang paid model khi flag tắt.

## Phase 5 - Chat core và SSE

Scope:

- `POST /v1/rag/chat`.
- SSE mặc định.
- `?stream=false` chỉ bật nếu contract được cập nhật rõ.
- Verify internal JWT.
- Create/reuse conversation.
- Persist USER message.
- Embed query bằng OpenRouter embedding model free.
- Vector cosine topK cố định `k=5`.
- Access filter:
  - `PASSENGER`: `PUBLIC`
  - `DRIVER`, `ASSISTANT`, `OPERATOR_STAFF`, `OPERATOR_ADMIN`: `PUBLIC`, `OPERATOR`
  - `SYSTEM_ADMIN`: all
- Tenant filter:
  - global docs: `operator_id IS NULL`
  - operator docs: `operator_id = caller.operatorId`
- Stream chat qua OpenRouter model free.
- Persist ASSISTANT message và cited chunk IDs.

DoD:

- Passenger không thấy OPERATOR/ADMIN.
- Operator A không thấy docs Operator B.
- Prompt injection test pass.
- SSE shape ổn định.
- Provider lỗi không crash process.

## Phase 6 - Production guardrails

Scope:

- Env-based limits:
  - `RAG_MAX_MESSAGE_CHARS`
  - `RAG_MAX_CONTEXT_TOKENS`
  - `RAG_MAX_RETRIEVED_CHUNKS`
  - `RAG_USER_RATE_LIMIT_PER_HOUR`
  - `RAG_OPERATOR_RATE_LIMIT_PER_HOUR`
  - `RAG_PROVIDER_TIMEOUT_MS`
- Circuit breaker cho 429/5xx.
- Redis embedding cache.
- Redact logs.
- Readiness kiểm tra DB, Redis, Cloudinary config và OpenRouter probe status.

DoD:

- Rate limit trả 429 envelope.
- Provider unavailable trả lỗi kiểm soát.
- Logs không chứa API key, full prompt hoặc token.

## Phase 7 - Hybrid search behind flag

Flag:

```text
HYBRID_SEARCH_ENABLED=false
```

Scope:

- PostgreSQL FTS top 10.
- pgvector top 10.
- RRF fusion `1/(60+rank_fts) + 1/(60+rank_vector)`.
- Không gọi là BM25.

DoD:

- Có eval trước/sau bằng golden set.
- Không giảm access safety/citation accuracy.

## Phase 8 - Intent filter, query rewrite, summarization

Flags:

```text
INTENT_FILTER_ENABLED=false
QUERY_REWRITE_ENABLED=false
SUMMARIZE_ENABLED=false
```

Scope:

- Intent deterministic trước, LLM classifier chỉ khi ambiguous.
- Query rewrite chỉ khi có đại từ/ngữ cảnh ngầm.
- Summarization sau turn thứ 6.

DoD:

- Multi-turn 8 turns giữ context.
- Off-topic refusal đúng.
- Rewrite không phá standalone query.

## Phase 9 - Rerank và feedback loop

Flag:

```text
RERANK_ENABLED=false
```

Scope:

- LLM rerank candidates top 10, timeout 2 giây.
- Timeout fallback top 5 cũ.
- Cache theo `queryHash + chunkIds`.
- `POST /v1/rag/messages/{id}/feedback`.
- Chỉ feedback ASSISTANT message.
- User chỉ feedback message của mình.
- Admin audit toàn bộ.

DoD:

- Feedback auth/ownership pass.
- Rerank fallback ổn định.

## Phase 10 - Production verification

Verify bắt buộc:

```bash
npx prisma validate --schema=apps/rag/prisma/schema.prisma
npx nx run rag:generate
npx nx run rag:lint
npx nx run rag:test
npx nx run rag:test:e2e
npx nx run rag:build
```

Script verify:

- `scripts/test-rag-phase<N>.js`

Coverage:

- Happy path.
- Auth fail.
- Validation fail.
- Permission fail.
- Cloudinary upload.
- Ingest.
- SSE chat.
- Access leak.
- Tenant leak.
- Prompt injection.
- Provider failure.
- Rate limit.
- Embedding dimension mismatch.

## Quy tắc dừng

- Nếu verify fail sau 2 lần retry, dừng và báo lỗi cụ thể cho USER.
- Không tự chuyển phase sau khi phase hiện tại xong nếu USER chưa nói `go`.
- Không tự thêm dependency mới.
- Không gọi phase là production-complete nếu còn provider placeholder trong production path.
