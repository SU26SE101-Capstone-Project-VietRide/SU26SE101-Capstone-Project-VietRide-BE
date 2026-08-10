# VietRide — Backend Source of Truth

> **Phiên bản:** 1.63.2
> **Trạng thái:** ACTIVE — sealed for capstone v1
> **Cập nhật lần cuối:** 2026-08-10
> **Capstone:** SU26SE101 — SU26
> **Owner doc:** Senior Backend Architect (rotate khi handover)

---

## 0. About — Đọc trước khi dùng

### 0.1 Mục đích

Đây là **master document** cho mọi coding agent / developer làm việc trên backend VietRide. Doc này KHÔNG lặp lại business rules — thay vào đó nó:

1. **Map** mọi tài liệu backend hiện có và chỉ rõ "khi cần X thì đọc file nào".
2. **Định nghĩa** các convention backend chưa có nơi cố định: project structure, layer pattern, error envelope, response shape, naming, DI, testing.
3. **Tổng hợp** các bảng tra cứu nhanh (service matrix, error code registry, event registry, status machines, background jobs).
4. **Versioning + changelog** — là tài liệu sống, mỗi thay đổi convention backend đều phải append changelog ở Section 13.

### 0.2 Nguồn dữ liệu (Source-of-truth hierarchy)

Khi conflict, ưu tiên theo thứ tự sau:

| # | File | Nội dung canonical | Khi nào reference |
|---|---|---|---|
| 1 | `SU26SE101_VIETRIDE_technical_context_v7.md` | **Business rules, flows, decisions, entity requirements, status machines, enum values** | Luôn là source-of-truth cuối cùng cho mọi câu hỏi business / domain. |
| 2 | `VietRide_API_Contract_v1.md` | **Controller/DTO contract chi tiết** (request/response shape per endpoint) | Khi scaffold controller, DTO, FE call. |
| 3 | `db-schema/<service>/schema.sql` + `db-schema/<service>/README.md` | **DDL + entity rationale per service** | Khi sinh entity class, migration, repository, hoặc cần biết column type / constraint cụ thể. |
| 4 | `db-schema/_global/cross-service-references.md` | **Danh sách logical FK cross-service** (enforce qua HTTP/event, KHÔNG hard FK DB) | Khi thiết kế inter-service call, snapshot field, event consume. |
| 5 | `db-schema/_global/README.md` + `ERD_DRAWING_MASTER.md` + `erd-all-relations-drawing-order.md` | **DB conventions toàn hệ thống** (naming, datatype, soft delete, audit columns) + ERD master | Khi cần overview DB hoặc tra naming convention. |
| 6 | **Doc này** (`BACKEND_SOURCE_OF_TRUTH.md`) | **Backend implementation layer** — project structure, code conventions, error/event/job registry, env config | Khi cần "code layout", "convention", "registry" — tức là KHÔNG phải business hay DDL. |

> **Quy tắc xung đột:** nếu doc này nói khác technical_context_v7 — **doc này SAI**, fix doc này và bump version.

### 0.3 Đối tượng đọc

| Role | Đọc tối thiểu |
|---|---|
| Backend coding agent (scaffold service) | Section 0–3, 5–11 (skip business detail) + open file ở Section 4 khi cần entity |
| Frontend / mobile agent | Section 2, 5, 6 + `VietRide_API_Contract_v1.md` + technical_context Section 4 (Client Apps) |
| QA / test agent | Section 8 (status machines), 9 (cross-cutting), 11 (job registry) + technical_context Section 6 (flows) |
| DBA / migration agent | Section 4 + `db-schema/_global/README.md` + per-service `schema.sql` |
| New developer | Đọc theo thứ tự Section 0 → 13 |

### 0.4 Quy tắc cập nhật doc

1. **Mỗi thay đổi convention** (đổi error code, đổi naming rule, thêm event mới, v.v.) → bắt buộc append Section 13 changelog với date + commit hash.
2. **Bump version** theo SemVer: PATCH cho typo/clarification, MINOR cho thêm section/registry entry, MAJOR cho breaking convention change.
3. **Không paste DDL** vào doc này — chỉ reference path tới `db-schema/<service>/schema.sql`.
4. **Không paste full API contract** — chỉ reference `VietRide_API_Contract_v1.md`.
5. **Khi flag gap** (rule mơ hồ trong technical_context): mở section `TBD / Open Questions` ở cuối doc thay vì tự suy diễn.

---

## 1. Service Map

### 1.1 Tổng quan kiến trúc

```
[Passenger App RN]  [Driver App RN]  [Operator Web Next]  [Admin Web Next]
        │                  │                  │                   │
        └──────────────────┴──────────────────┴───────────────────┘
                                    │
                            ┌───────▼────────┐
                            │  API Gateway   │  NestJS — JWT validate + Internal JWT sign + rate limit
                            └───────┬────────┘
              ┌─────────────────────┼─────────────────────┐
              │                     │                     │
       .NET Core 8 Services    NestJS Services        RabbitMQ
       ────────────────────    ────────────────       (broker)
       Identity & User         Tracking (Socket.IO)
       Booking                 Notification (FCM)
       Trip-Route-Vehicle      RAG AI (LLM SSE)
       Payment & Wallet
       Parcel

       Shared infra: PostgreSQL 16 (8 DBs) + PgBouncer + Redis 7 + Firebase + Nginx
```

### 1.2 Service ↔ Framework ↔ Database matrix

| # | Service | Framework | Database name | Hangfire | Prisma | Trách nhiệm chính |
|---|---|---|---|---|---|---|
| 0 | **API Gateway** | NestJS | — (stateless) | — | — | JWT validate (RS256 JWKS), Internal JWT sign (HS256 120s), reverse proxy, rate limit, phone-completion gate |
| 1 | **Identity & User** | .NET 8 + EF Core 8 | `vietride_identity` | ✓ | — | Auth (OAuth/email/OTP), RBAC, User/Operator profile, refresh token rotation, SubscriptionPlan + OperatorSubscription, ActivityLog, UserDevice (FCM token) |
| 2 | **Trip-Route-Vehicle** | .NET 8 + EF Core 8 | `vietride_trip` | ✓ | — | Station/Stop/Route/RouteStop, Vehicle + VehicleType, Trip + TripSeat + TripStop + TripStopFare, operator holiday fare surcharge, DriverSchedule + Hangfire generate, AlternativeRoute, RouteChangeProposal snapshots, ShuttleTrip, Incident |
| 3 | **Booking** | .NET 8 + EF Core 8 | `vietride_booking` | ✓ | — | Booking order + per-seat Ticket + Passenger boarding record + BookingTransfer, BookingPendingAction, Voucher + VoucherUsage + OperatorVoucherConsent, BookingStats, seat lock TTL (Redis) |
| 4 | **Payment & Wallet** | .NET 8 + EF Core 8 | `vietride_payment` | ✓ | — | Payment (BOOKING/PARCEL/TOP_UP/SUBSCRIPTION), Wallet + WalletTransaction (passenger), PlatformWallet + OperatorWallet + OperatorLedgerEntry + OperatorTripSettlement, Invoice + PDF, VNPay integration, RefundFailureLog |
| 5 | **Parcel** | .NET 8 + EF Core 8 | `vietride_parcel` | ✓ | — | Parcel lifecycle, ParcelRouteFare, hashed delivery-token history, transfer/return flows, ParcelStats |
| 6 | **Tracking** | NestJS + Prisma | `vietride_tracking` | — | ✓ | Socket.IO GPS streaming (`/tracking`), ETA caching (Redis 60s), off-route detection, batch-write `GpsTrail` từ Redis buffer mỗi 5 phút |
| 7 | **Notification** | NestJS + Prisma | `vietride_notification` | — | ✓ | Consume RabbitMQ events → enqueue BullMQ → FCM push + in-app `Notification` history + `NotificationDelivery` retry log + Email via SendGrid (OTP + parcel link) |
| 8 | **RAG AI** | NestJS + Prisma | `vietride_rag` | — | ✓ | KnowledgeDocument ingest, KnowledgeChunk + pgvector embed, RagConversation + RagMessage, LLM SSE streaming |

### 1.3 Container & port map (local dev)

| Container | Image base | Internal port | Expose qua Nginx | Notes |
|---|---|---|---|---|
| `gateway` | NestJS | 3000 | `/v1/*` | Public entrypoint |
| `identity` | .NET 8 | 5001 | `/internal/v1/users/*`, `/internal/v1/operators/*` (via JWKS public endpoint) | JWKS public at `/v1/.well-known/jwks.json` |
| `trip` | .NET 8 | 5002 | internal only | |
| `booking` | .NET 8 | 5003 | internal only | |
| `payment` | .NET 8 | 5004 | internal + `/v1/payments/vnpay-ipn` (IP whitelist) | VNPay IPN bypass Internal JWT, verify HMAC-SHA512 |
| `parcel` | .NET 8 | 5005 | internal + delivery link `https://app.vietride.app/parcels/delivery/confirm?token=…` |
| `tracking` | NestJS | 3001 | `/tracking/socket.io` (WSS upgrade) | Direct client connection (User Access Token RS256) |
| `notification` | NestJS | 3002 | internal only | Consume RabbitMQ |
| `rag` | NestJS | 3003 | `/v1/rag/*` | SSE streaming |
| `postgres` | postgres:16 | 5432 | — | 8 logical DBs |
| `pgbouncer` | edoburu/pgbouncer | 6432 | — | Transaction pool mode |
| `redis` | redis:7 | 6379 | — | Cache + seat lock + BullMQ + Redis idem |
| `rabbitmq` | rabbitmq:3-management | 5672 / 15672 | — | Mgmt UI 15672 |

> Port assignment có thể đổi nhưng phải sync với `docker-compose.yml` + ENV `<SERVICE>_BASE_URL`.

### 1.4 Scope boundaries — must remember

- **8 business services + 1 Gateway = 9 containers.** Gateway KHÔNG phải business service.
- **Wallet KHÔNG phải service riêng** — module trong Payment & Wallet Service. Mọi reference "Wallet Service" trong doc đều ám chỉ module này.
- **DriverSchedule, Vehicle, Search → tất cả thuộc Trip-Route-Vehicle.**
- **MediatR chỉ in-process** trong .NET service — KHÔNG dùng để inter-service.
- **Cross-DB FK BỊ CẤM ở DB layer** — chỉ logical FK (xem `db-schema/_global/cross-service-references.md`).
- **MediatR pin v11.x** (v12+ commercial license). NuGet: `<PackageReference Include="MediatR" Version="11.*" />`.

---

## 2. Tech Stack Pinned Versions

> Mọi version dưới đây là **pinned**. Update qua PR có review.

### 2.1 .NET services

| Package | Version | Lý do pin |
|---|---|---|
| .NET SDK | 8.0.x (LTS) | LTS đến 2026-11 |
| Entity Framework Core | 8.x | Cùng major với SDK |
| Npgsql.EntityFrameworkCore.PostgreSQL | 8.x | Cùng major EF Core |
| MediatR | **11.x** (MIT) | v12+ commercial license, KHÔNG upgrade |
| FluentValidation | 11.x | |
| FluentValidation.AspNetCore | 11.x | |
| Hangfire.AspNetCore | latest stable, centrally pinned | Approved .NET Hangfire host/dashboard integration; no other Booking scheduler package |
| Hangfire.PostgreSql | latest stable | Storage trong cùng DB service, schema `hangfire` |
| Polly | 8.x | Circuit breaker + retry cho external HTTP |
| Serilog.AspNetCore | latest | Structured logging console + file |
| BCrypt.Net-Next | latest | bcrypt cost 12 |
| Mapster | 7.x | Optional, dùng cho DTO mapping (chọn vì source-gen compile-time, không reflection runtime như AutoMapper, license MIT). Có thể skip dùng tay. |
| Microsoft.IdentityModel.Tokens | latest | Sign/verify JWT |
| FirebaseAdmin | **3.6.0** | Identity-only Firebase Custom Token minting and refresh-session revocation |

### 2.2 NestJS services

| Package | Version | Lý do |
|---|---|---|
| Node.js | 20 LTS | |
| NestJS | 11.x | `package.json` là source-of-truth cho version chính xác |
| Prisma | 6.x | `package.json` là source-of-truth cho version chính xác |
| pg | 8.x | |
| socket.io | 4.x | Tracking |
| @nestjs/microservices (RabbitMQ) | 11.x | Consumer pattern |
| amqplib | 0.10.x | Underlying AMQP client |
| bullmq | 5.x | Redis-backed queue |
| ioredis | 5.x | |
| firebase-admin | 12.x | FCM push (Notification) |
| @sendgrid/mail | 8.x | Email provider (Notification) |
| Provider SDK riêng | Không dùng | RAG gọi OpenRouter chat/embedding bằng built-in `fetch`; Cloudinary raw asset bằng REST API |
| pgvector | 0.2.x | RAG only |
| http-proxy-middleware | 3.x | Gateway proxy |
| jose | 5.x | Gateway JWT sign/verify |
| zod | 3.x | DTO validation runtime |

### 2.3 Infrastructure pinned

| Component | Version | Notes |
|---|---|---|
| PostgreSQL | 16 | + pgvector extension cho `vietride_rag` |
| PgBouncer | 1.21+ | Transaction pool mode |
| Redis | 7.x | |
| RabbitMQ | 3.13+ | management plugin enabled |
| Nginx | 1.25+ | Reverse proxy, SSL termination, WebSocket upgrade |
| Docker Compose | v2 | |

---

## 3. Project / Solution Structure

> **Monorepo manager: Nx.** NestJS services được generate bằng `nx generate @nx/nest:app`. .NET services dùng `@nx-dotnet/core` plugin để Nx tracking (build/test/lint qua Nx executor), nhưng bản thân mỗi .NET service vẫn là 1 solution `.sln` độc lập bên trong folder app — Nx không thay thế MSBuild.

> ### ⚠️ Quan trọng cho mọi agent đọc Section 3 — File structure chỉ là VÍ DỤ
>
> Mọi sơ đồ thư mục + danh sách file trong Section 3 (3.1 monorepo, 3.2 .NET layout, 3.3 NestJS layout, 3.4 Gateway, 3.6 libs) chỉ là **ví dụ minh họa** để agent hiểu **cấu trúc + convention naming + dependency direction**. **KHÔNG phải danh sách file đầy đủ hoặc cố định.**
>
> **Agent ĐƯỢC PHÉP:**
> - **Tạo thêm file/folder mới** khi domain hoặc use case thực sự cần (vd thêm `BookingRefundService.cs`, thêm `<feature>/orchestrators/<x>.orchestrator.ts`, thêm folder `Sagas/` nếu có long-running flow, thêm `Specifications/` cho EF Core Specification pattern, …).
> - **Bỏ file/folder không cần** khi service không có use case đó (vd service không publish event thì KHÔNG cần `Outbox/` folder; Gateway KHÔNG cần `Domain/`; Notification KHÔNG cần `Outbox/`; service không gọi external SaaS thì KHÔNG cần `ExternalClients/`).
> - **Rename theo domain thực tế** — ví dụ trong doc là `<Aggregate>` placeholder, agent thay bằng tên Aggregate thật của service (`Booking`, `Trip`, `Parcel`, ...).
> - **Tạo nhiều file/class nhỏ hơn** trong cùng folder khi có nhiều use case / entity / event trong cùng aggregate.
> - **Gom file lại** khi cùng concern và KHÔNG vi phạm SRP (xem 3.2.3 balance philosophy).
>
> **Agent KHÔNG được phép:**
> - **Đổi convention naming** (Section 3.5) — `<Verb><Aggregate>Command`, `I<Aggregate>Repository`, `<Aggregate>Service`, snake_case DB, camelCase JSON, … cố định.
> - **Phá dependency direction** (Section 3.2.2) — Domain không ref Application/Infrastructure; Application không ref Infrastructure.
> - **Bỏ qua anti-pattern** ở 3.2.3 (god class, controller chứa business logic, repository chứa business rule, …).
> - **Đẩy domain nghiệp vụ vào `libs/`** (Section 3.6) — domain entity LUÔN sống trong `apps/<service>/Domain/`.
> - **Đẩy infrastructure cụ thể của 1 service vào `libs/`** — VnPayClient/SendGridEmailClient/DbContext của service LUÔN sống trong `apps/<service>/Infrastructure/`.
>
> **Nguyên tắc khi quyết định tạo file mới:**
> 1. **Có use case thực tế cần?** Có → tạo. Không → đừng tạo "vì biết đâu cần".
> 2. **Đặt vào folder nào theo Section 3.5 naming + 3.6 libs philosophy?**
> 3. **File có ≤1 trách nhiệm rõ ràng?** (KHÔNG yêu cầu cứng số dòng — xem 3.2.3 balance).
> 4. **Đặt tên theo convention?** (Section 3.5).
> 5. **Có ai consume file này?** Nếu không (dead code, premature) → bỏ.

### 3.1 Monorepo top-level layout (Nx)

> ⚠️ **Reminder:** Sơ đồ dưới đây là ví dụ minh họa cấu trúc + folder semantics. Tên file con bên trong (`docker-compose.yml`, `init.sql`, `nginx.conf`, ...) là gợi ý — agent có thể tạo thêm hoặc đặt khác miễn giữ folder semantics chính xác.

```
vietride/                                                                    (workspace root)
├── .artifacts/                          CI artifacts (test reports, coverage, build logs) — gitignored
├── .claude/                             Claude Code agent config (settings.json, hooks)
├── .github/                             GitHub Actions workflows
├── .nx/                                 Nx local cache — gitignored
├── .vscode/                             Editor config (recommended extensions, launch.json)
├── apps/                                ⭐ Tất cả services (NestJS + .NET) — xem 3.2
│   ├── gateway/                         NestJS (Nx project)
│   ├── identity/                        .NET solution (Nx project via @nx-dotnet)
│   ├── trip/                            .NET solution
│   ├── booking/                         .NET solution
│   ├── payment/                         .NET solution
│   ├── parcel/                          .NET solution
│   ├── tracking/                        NestJS
│   ├── notification/                    NestJS
│   └── rag/                             NestJS
├── libs/                                ⭐ Building blocks reusable — xem 3.6 cho philosophy
│   ├── shared/                          (TS — NestJS apps consume)
│   │   ├── contracts/                   DTO types, error code enum, event payload types (FE + BE share qua TS path alias)
│   │   ├── nest-common/                 JwtAuthGuard, InternalJwtGuard, RolesGuard, ProblemJsonExceptionFilter, RequestContextMiddleware, ZodValidationPipe, @CurrentUser/@Roles decorators
│   │   ├── nest-rabbitmq/               RabbitMQ connection factory, producer abstraction, consumer base class, routing-key constants, Outbox publisher base
│   │   ├── nest-persistence/            Prisma naming strategy (snake_case), base entity (id/createdAt/updatedAt/rowVersion), soft-delete subscriber
│   │   ├── nest-redis/                  IoRedis module factory + idempotency middleware helper
│   │   └── nest-config/                 Zod env schema base + ConfigModule factory + validated env type
│   └── dotnet/                          (C# — .NET apps reference qua ProjectReference từ libs)
│       ├── VietRide.Shared.Kernel/                 ⭐ Domain primitives — referenced bởi Domain layer
│       │   ├── ValueObjects/Money.cs               BIGINT VND wrapper, to-the-đồng (FromDecimal rounds nearest đồng)
│       │   ├── ValueObjects/PhoneNumber.cs         E.164 VN validation
│       │   ├── Primitives/BaseEntity.cs            Id, CreatedAt, UpdatedAt, RowVersion + IAuditable/ISoftDeletable/IActivatable markers
│       │   ├── Primitives/Result.cs                Result<T> + Error type cho functional error handling
│       │   ├── Abstractions/IClock.cs              `DateTime.UtcNow` wrapper for testability
│       │   └── Exceptions/DomainException.cs       Base domain exception
│       ├── VietRide.Shared.Application/            ⭐ Application primitives — referenced bởi Application layer
│       │   ├── Repositories/IRepository.cs         `IRepository<TEntity, TId>` generic base contract (GetByIdAsync, AddAsync, Update, Remove, Query)
│       │   ├── Repositories/IReadRepository.cs     Read-only variant (KHÔNG có Add/Update/Remove — cho Query Handler)
│       │   ├── UnitOfWork/IUnitOfWork.cs           Optional — wrap `SaveChangesAsync` + transaction begin/commit
│       │   ├── Services/IApplicationService.cs     Marker interface (DI scanning convention)
│       │   ├── Behaviors/ValidationBehavior.cs     MediatR pipeline — generic
│       │   ├── Behaviors/LoggingBehavior.cs        MediatR pipeline — generic
│       │   ├── Behaviors/TransactionBehavior.cs    MediatR pipeline — wrap BeginTransaction/Commit
│       │   ├── Pagination/PagedResult.cs           `{ items, page, pageSize, totalItems, totalPages, hasNextPage, hasPreviousPage }` — see §5.7
│       │   ├── Exceptions/                         ValidationException, NotFoundException, ConflictException, ForbiddenException
│       │   └── Mapping/MappingExtensions.cs        Manual mapping helpers (KHÔNG bắt buộc Mapster)
│       ├── VietRide.Shared.Persistence/            ⭐ EF Core helpers — referenced bởi Infrastructure layer
│       │   ├── EfRepository.cs                     `EfRepository<TEntity, TId>` generic impl của IRepository
│       │   ├── EfUnitOfWork.cs                     Generic impl của IUnitOfWork
│       │   ├── Interceptors/AuditingInterceptor.cs       Set CreatedAt/UpdatedAt cho IAuditable
│       │   ├── Interceptors/SoftDeleteInterceptor.cs     Set DeletedAt + global query filter (DeletedAt == null) cho ISoftDeletable
│       │   ├── Interceptors/OutboxInterceptor.cs         INSERT outbox_events cùng SaveChanges transaction
│       │   ├── Conventions/SnakeCaseNamingConvention.cs  Map PascalCase property → snake_case column
│       │   └── Outbox/OutboxEvent.cs                     Base OutboxEvent entity (mỗi service có DbSet riêng)
│       ├── VietRide.Shared.Messaging/              ⭐ RabbitMQ + event publishing — referenced bởi Application + Infrastructure
│       │   ├── Abstractions/IEventPublisher.cs     Outbox-aware publish API (used by Application)
│       │   ├── Abstractions/IIntegrationEvent.cs   Marker interface
│       │   ├── Constants/RoutingKeys.cs            Centralized event routing key constants
│       │   ├── RabbitMq/RabbitMqConnectionFactory.cs
│       │   ├── RabbitMq/OutboxEventPublisher.cs    Concrete impl — read outbox + publish
│       │   └── HostedServices/OutboxPublisherHostedService.cs  Generic — service inject + register
│       ├── VietRide.Shared.Http/                   ⭐ Inter-service HTTP — referenced bởi Infrastructure
│       │   ├── Handlers/InternalJwtPropagationHandler.cs   DelegatingHandler — gen + inject X-Internal-Auth header
│       │   ├── Handlers/RequestIdPropagationHandler.cs     Propagate X-Request-Id
│       │   ├── Polly/PollyPolicyBuilder.cs         Retry + circuit breaker + timeout policy factory
│       │   └── BaseServiceClient.cs                Optional base class cho TripServiceClient/IdentityServiceClient/...
│       └── VietRide.Shared.Web/                    ⭐ ASP.NET Core integration — referenced bởi Api
│           ├── Authentication/InternalJwtAuthenticationHandler.cs  Verify X-Internal-Auth HS256
│           ├── Authentication/JwksAuthenticationExtensions.cs      Verify User Access Token RS256 via JWKS
│           ├── Filters/ApiResponseExceptionFilter.cs               Global exception → ApiResponse error envelope
│           ├── Filters/ApiResponseResultFilter.cs                  Success-wrap → ApiResponse envelope
│           ├── Middleware/RequestLoggingMiddleware.cs              Structured log per request
│           ├── Middleware/IdempotencyMiddleware.cs                 Redis-based Idempotency-Key handling
│           ├── Health/HealthCheckBuilderExtensions.cs              /health + /ready setup
│           └── Swagger/SwaggerSetupExtensions.cs                   OpenAPI generation defaults
├── infra/                               ⭐ Infrastructure config & manifests
│   ├── docker/
│   │   ├── docker-compose.yml           Production-like local stack
│   │   ├── docker-compose.override.yml  Local dev overrides (volumes, ports)
│   │   └── postgres/init.sql            CREATE DATABASE statements (8 logical DBs)
│   ├── nginx/
│   │   ├── nginx.conf
│   │   └── conf.d/*.conf
│   ├── pgbouncer/
│   │   └── pgbouncer.ini
│   ├── rabbitmq/
│   │   └── definitions.json             Pre-declared exchanges + queues + bindings
│   └── k8s/                             (optional, v2)
├── db-schema/                           ⭐ DB DDL + ERD per service (existing — không đổi)
│   ├── _global/
│   ├── identity-user/
│   ├── trip-route-vehicle/
│   ├── booking/
│   ├── payment-wallet/
│   ├── parcel/
│   ├── tracking/
│   ├── notification/
│   └── rag-ai/
├── docs/                                ⭐ ALL developer & generated documentation (single folder — đã gom DOC/ uppercase vào đây v1.3.3)
│   ├── adr/                             Architecture Decision Records
│   ├── runbooks/                        On-call / deployment runbooks
│   ├── api/                             API docs + auto-generated OpenAPI
│   │   └── openapi/                     OpenAPI 3 spec auto-generated per service (tool output, KHÔNG sửa tay)
│   └── deliverables/                    Capstone submission artifacts (compiled diagrams, demo recording, etc.)
├── scripts/                             Utility scripts (one-off, ops)
│   ├── seed-dev-data.ts                 Seed dev DB
│   ├── reset-local.sh                   Tear down + rebuild local stack
│   └── gen-jwt-secret.sh                Generate INTERNAL_JWT_SECRET
├── tests/                               ⭐ ALL test projects ngoài per-app unit tests
│   ├── e2e/                             Cross-service Playwright/Supertest e2e (đi xuyên ≥2 services)
│   ├── load/                            k6/Artillery load test scripts
│   ├── gateway-e2e/                     Per-app HTTP e2e cho Gateway (Jest + axios, Nx generator default — di chuyển từ `apps/<svc>-e2e/` về đây v1.3.3)
│   ├── tracking-e2e/                    Per-app HTTP e2e cho Tracking
│   ├── notification-e2e/                Per-app HTTP e2e cho Notification
│   └── rag-e2e/                         Per-app HTTP e2e cho RAG
├── dist/                                Build output — gitignored
├── node_modules/                        gitignored
├── BACKEND_SOURCE_OF_TRUTH.md          (this doc)
├── SU26SE101_VIETRIDE_technical_context_v7.md
├── Docs/                                ⚠️ Legacy folder — chứa `Docs/API/VietRide_API_Contract_v1.md` + `Docs/Architecture/`. Sẽ migrate dần sang `docs/api/` lowercase ở v1.x cleanup. KHÔNG tạo file mới ở đây.
├── AGENTS.md                            Hướng dẫn cho coding agent (tools convention)
├── CLAUDE.md                            Hướng dẫn riêng cho Claude Code
├── README.md                            Root README — quick start, link tới doc khác
├── .env                                 gitignored
├── .env.example                         Template ENV vars (check in)
├── .gitignore
├── .prettierignore
├── .prettierrc
├── eslint.config.mjs                    Flat ESLint config cho toàn workspace
├── jest.config.ts                       Jest workspace config (Nx)
├── jest.preset.js                       Jest preset shared bởi tất cả apps
├── nx.json                              Nx workspace config (target defaults, plugins)
├── package.json                         Root npm package — chứa Nx + plugin deps
├── package-lock.json
├── tsconfig.base.json                   Base TS config — path aliases cho libs
└── global.json                          ⭐ Pin .NET SDK version (`{ "sdk": { "version": "8.0.x" } }`)
```

**Key Nx files:**

- `nx.json` — workspace config + target defaults (`build`, `test`, `lint`, `serve`).
- `tsconfig.base.json` — path aliases trỏ tới `libs/shared/*` để app import được TS lib.
- `global.json` — pin .NET SDK ở root để CI + local dùng cùng version.

**Folder semantics quick-ref:**

| Folder | Chứa gì | Khi nào tạo file mới ở đây |
|---|---|---|
| `apps/` | 1 folder = 1 deployable service | Khi scaffold service mới (`nx g @nx/nest:app` hoặc tạo .NET solution + add Nx project) |
| `libs/shared/<x>/` | TS code reuse giữa NestJS services | Khi có code lặp ≥2 NestJS apps (Guard, Filter, DTO type, event contract) |
| `libs/dotnet/<x>/` | C# code reuse giữa .NET services | Khi có code lặp ≥2 .NET apps (Money struct, Outbox publisher, JWT handler) |
| `infra/` | Config infrastructure (Docker, Nginx, PgBouncer, RabbitMQ) | Khi thêm service mới vào compose, hoặc thay config |
| `db-schema/` | DDL canonical | Khi thêm/sửa entity. Source-of-truth cho schema. |
| `docs/` | **ALL** developer + generated docs — ADR, runbook, dev guide (markdown), `docs/api/openapi/` cho generated OpenAPI JSON, `docs/deliverables/` cho capstone artifacts | Khi cần document quyết định, on-call, dev setup, hoặc xuất artifact build. (v1.3.3: gom uppercase `DOC/` về đây cho gọn) |
| `scripts/` | One-off / ops scripts | Khi cần script reusable |
| `tests/` | E2E + load test xuyên qua nhiều services | Per-app tests vẫn ở `apps/<x>/`; chỉ test xuyên service ở đây |

### 3.2 .NET service layout (Clean Architecture + CQRS + Repository + Service)

> **Nguyên tắc (đọc kỹ trước khi scaffold):**
> 1. **Clean Architecture + DDD lite + CQRS.** Pipeline: Controller → MediatR.Send(Command/Query) → Handler → `I<Aggregate>Service` (orchestration) / `I<Aggregate>Repository` (data) → Domain entity method.
> 2. **`IRepository<TEntity, TId>` generic base** ở `libs/dotnet/VietRide.Shared.Application` + **per-aggregate `I<Aggregate>Repository`** trong service (extend generic, thêm query domain-specific). Impl: `EfRepository<T,TId>` base ở `libs/dotnet/VietRide.Shared.Persistence` + per-service `<Aggregate>Repository : EfRepository<>, I<Aggregate>Repository` ở Infrastructure.
> 3. **`I<Aggregate>Service` per-aggregate** ở Application layer — chứa business logic / orchestration **tái dùng** giữa nhiều Handler (vd `IBookingService.CalculateCancellationFee()`, `ITripService.GenerateTripSeats()`). Impl `<Aggregate>Service` cùng Application layer (NOT Infrastructure). Handler inject Service khi cần shared logic; nếu logic chỉ dùng 1 lần → viết thẳng trong Handler.
> 4. **MediatR vẫn là entry point từ Controller** (per technical_context_v7 quyết định CQRS). Controller KHÔNG được gọi thẳng `IBookingService` — phải qua `MediatR.Send(command)` để hưởng pipeline behaviors (validation, logging, transaction). Service chỉ được Handler / Service khác gọi.
> 5. **External integration interfaces** (`IVnPayClient`, `ISendGridEmailClient`, `IFcmPushClient`, `IFirebaseStorageClient`) đặt tại Application/Abstractions; impl concrete tại Infrastructure/ExternalClients. Inter-service HTTP cũng theo pattern này: `ITripServiceClient` (Application) → `TripServiceClient` (Infrastructure/Http).
> 6. **SOLID — đặc biệt SRP:** 1 file = 1 class/interface = 1 trách nhiệm. Mỗi Command/Query/Handler/Validator/DTO/Entity/Repository/Service là **file `.cs` riêng**. KHÔNG được gộp 5 handler vào 1 file. KHÔNG có `BookingService` 800 dòng làm tất cả — chia method theo use case, mỗi method ≤ 80 dòng.
> 7. **ISP:** `I<Aggregate>Repository` KHÔNG được trở thành god interface 30 method. Nếu repo nhiều method → tách `I<Aggregate>ReadRepository` (query) vs `I<Aggregate>WriteRepository` (mutation). `I<Aggregate>Service` cũng vậy — nếu vượt ~10 method → tách theo concern (`IBookingLifecycleService`, `IBookingPricingService`, ...).
> 8. **OCP:** thêm use case mới → tạo Handler MỚI, KHÔNG sửa Handler cũ. Thêm query mới cho repo → thêm method mới, KHÔNG modify signature method cũ.
> 9. **DIP:** Application chỉ depend abstraction (`IRepository`, `IService`, external clients). Application **KHÔNG được** reference Infrastructure project. Infrastructure implement các interface đó.

#### 3.2.1 Solution + folder layout

> ⚠️ **Reminder:** Cây thư mục + file dưới đây là **ví dụ minh họa convention** — KHÔNG phải danh sách bắt buộc. Agent tự quyết định file cần tạo dựa trên use case thực tế của service. Xem callout đầu Section 3 cho rules chi tiết.

Mỗi .NET service là 1 solution **độc lập** với 4 project + 2 test project, đặt dưới `apps/<service>/`:

```
apps/<service>/                                    (Nx project root)
├── project.json                                   Nx target definitions (build = dotnet build, test = dotnet test)
├── VietRide.<Service>.sln
├── Directory.Build.props                          Shared MSBuild props (TargetFramework, Nullable, TreatWarningsAsErrors)
├── src/
│   ├── VietRide.<Service>.Api/                    ASP.NET Core host (entry + HTTP boundary)
│   │   ├── Controllers/<Aggregate>Controller.cs   Thin — chỉ MediatR.Send + map response
│   │   ├── Middleware/                            InternalJwtAuthHandler, RequestLoggingMiddleware
│   │   ├── HostedServices/OutboxPublisherHostedService.cs   IHostedService — KHÔNG dùng Hangfire
│   │   ├── HangfireJobs/<Job>.cs                  1 file = 1 job class (business scheduled jobs)
│   │   ├── DependencyInjection/                   ServiceCollection extensions (AddApi, AddSwagger, AddHangfireSetup)
│   │   ├── appsettings.json
│   │   ├── appsettings.Development.json
│   │   ├── Program.cs                             Composition root only
│   │   └── VietRide.<Service>.Api.csproj
│   │
│   ├── VietRide.<Service>.Application/            Use cases (CQRS) + Service interfaces + Repository interfaces
│   │   ├── Abstractions/
│   │   │   ├── Repositories/                      ⭐ Per-aggregate repository interfaces (1 file/interface)
│   │   │   │   ├── IBookingRepository.cs          extends IRepository<Booking, Guid> + booking-specific queries
│   │   │   │   ├── IPassengerRepository.cs        extends IRepository<Passenger, Guid>
│   │   │   │   ├── IVoucherRepository.cs          extends IRepository<Voucher, Guid>
│   │   │   │   └── IBookingPendingActionRepository.cs
│   │   │   ├── Services/                          ⭐ Per-aggregate application service interfaces
│   │   │   │   ├── IBookingService.cs             Orchestration + business logic reusable cross-handler
│   │   │   │   ├── IBookingPricingService.cs      Tách theo concern nếu IBookingService vượt ~10 method
│   │   │   │   └── IVoucherService.cs
│   │   │   ├── ExternalClients/                   External integration interfaces (impl ở Infrastructure)
│   │   │   │   ├── IVnPayClient.cs                (Payment service only)
│   │   │   │   ├── ISendGridEmailClient.cs        (Notification only)
│   │   │   │   ├── IFcmPushClient.cs              (Notification only)
│   │   │   │   ├── IFirebaseStorageClient.cs
│   │   │   │   └── IGoogleDirectionsClient.cs     (Tracking only)
│   │   │   └── ServiceClients/                    Inter-service HTTP client interfaces
│   │   │       ├── ITripServiceClient.cs
│   │   │       ├── IIdentityServiceClient.cs
│   │   │       ├── IPaymentServiceClient.cs
│   │   │       └── IParcelServiceClient.cs
│   │   ├── Services/                              ⭐ Application service IMPLEMENTATIONS (NOT Infrastructure)
│   │   │   ├── BookingService.cs                  implements IBookingService — orchestration + shared business logic
│   │   │   ├── BookingPricingService.cs
│   │   │   └── VoucherService.cs                  ⚠️ Services KHÔNG inject DbContext trực tiếp — qua Repository
│   │   ├── Features/                              ⭐ CQRS — folder per Aggregate, sub-folder per use case
│   │   │   └── <Aggregate>/
│   │   │       ├── Create<Aggregate>/
│   │   │       │   ├── Create<Aggregate>Command.cs           IRequest<Result<XyzDto>>
│   │   │       │   ├── Create<Aggregate>CommandHandler.cs    inject IRepository + IService khi cần
│   │   │       │   ├── Create<Aggregate>CommandValidator.cs  FluentValidation
│   │   │       │   └── <Aggregate>Dto.cs                     Response DTO
│   │   │       ├── Get<Aggregate>ById/
│   │   │       │   ├── Get<Aggregate>ByIdQuery.cs
│   │   │       │   └── Get<Aggregate>ByIdQueryHandler.cs     Read — inject I<Aggregate>ReadRepository or IRepository
│   │   │       └── List<Aggregate>/
│   │   │           ├── List<Aggregate>Query.cs
│   │   │           └── List<Aggregate>QueryHandler.cs
│   │   ├── EventHandlers/                         Inbound RabbitMQ event handlers (1 file/class per event)
│   │   │   └── <EventName>Handler.cs
│   │   └── VietRide.<Service>.Application.csproj  (ref: Domain + libs/dotnet/VietRide.Shared.Application + VietRide.Shared.Kernel + VietRide.Shared.Messaging)
│   │
│   ├── VietRide.<Service>.Domain/                 Entities, value objects, domain events, enums — POCO
│   │   ├── Entities/<Aggregate>.cs                1 file = 1 entity. Business invariants enforced trong method (vd `booking.Confirm()`, `trip.StartBoarding()`)
│   │   ├── ValueObjects/                          1 file = 1 VO (Money, BookingCode, PhoneNumber)
│   │   ├── Enums/                                 1 file = 1 enum (BookingStatus, TripStatus, ParcelStatus, …)
│   │   ├── Events/                                Domain events (raise trong entity method) — 1 file/event
│   │   ├── Exceptions/DomainException.cs          Base + specific subclasses
│   │   └── VietRide.<Service>.Domain.csproj       ⚠️ ZERO external ref. Không depend EF Core, không depend MediatR.
│   │
│   └── VietRide.<Service>.Infrastructure/         EF Core + Repository impl + concrete external clients + outbox
│       ├── Persistence/
│       │   ├── ApplicationDbContext.cs            EF Core DbContext (DbSet per entity)
│       │   ├── Configurations/<Entity>Configuration.cs   1 file/entity (IEntityTypeConfiguration<T>)
│       │   ├── Repositories/                      ⭐ Per-aggregate repository implementations (1 file/class)
│       │   │   ├── BookingRepository.cs           : EfRepository<Booking, Guid>, IBookingRepository
│       │   │   ├── PassengerRepository.cs         : EfRepository<Passenger, Guid>, IPassengerRepository
│       │   │   ├── VoucherRepository.cs
│       │   │   └── BookingPendingActionRepository.cs
│       │   ├── Queries/                           Optional — IQueryable<T> extension methods reusable cross-repository
│       │   │   └── BookingQueries.cs              static class WhereActiveForOperator(), WithPassengers(), etc.
│       │   └── Migrations/                        EF Core migrations
│       ├── ExternalClients/                       Concrete impl of Application/Abstractions/ExternalClients/*
│       │   ├── VnPayClient.cs                     : IVnPayClient
│       │   ├── SendGridEmailClient.cs             : ISendGridEmailClient
│       │   ├── FcmPushClient.cs                   : IFcmPushClient
│       │   ├── FirebaseStorageClient.cs           : IFirebaseStorageClient
│       │   └── GoogleDirectionsClient.cs          : IGoogleDirectionsClient
│       ├── Http/                                  Inter-service typed HttpClient impl
│       │   ├── TripServiceClient.cs               : ITripServiceClient — uses Polly + InternalJwtPropagationHandler
│       │   ├── IdentityServiceClient.cs
│       │   ├── PaymentServiceClient.cs
│       │   └── ParcelServiceClient.cs
│       ├── Messaging/
│       │   ├── RabbitMqEventPublisher.cs          Concrete impl của IEventPublisher từ libs
│       │   └── RabbitMqConsumer.cs                Subscribe queue, dispatch tới handler
│       ├── DependencyInjection/InfrastructureServiceCollectionExtensions.cs   ⭐ AddInfrastructure() wire repository + service + external client
│       └── VietRide.<Service>.Infrastructure.csproj (ref: Domain + Application + libs/dotnet/VietRide.Shared.Persistence + VietRide.Shared.Http)
│
└── tests/
    ├── VietRide.<Service>.UnitTests/              xUnit + FluentAssertions + NSubstitute
    │   ├── Domain/                                Entity invariant tests (no EF, pure POCO)
    │   ├── Application/                           Handler tests — mock external interfaces, use in-memory DbContext hoặc SQLite
    │   └── VietRide.<Service>.UnitTests.csproj
    └── VietRide.<Service>.IntegrationTests/       WebApplicationFactory + Testcontainers PostgreSQL
        ├── Api/                                   Endpoint tests qua HTTP
        ├── Fixtures/                              PostgresFixture, RedisFixture
        └── VietRide.<Service>.IntegrationTests.csproj
```

#### 3.2.2 Project reference rules (CI enforced)

```
Domain          → (no refs at all — pure POCO)
Application     → Domain
Infrastructure  → Domain, Application
Api             → Application, Infrastructure (composition root only — wire DI ở Program.cs)
```

**Enforce qua `NetArchTest` trong UnitTests:**

```csharp
[Fact]
public void Domain_should_not_reference_Application_or_Infrastructure()
{
    var result = Types.InAssembly(typeof(Booking).Assembly)
        .Should().NotHaveDependencyOnAny("VietRide.<Service>.Application", "VietRide.<Service>.Infrastructure")
        .GetResult();
    result.IsSuccessful.Should().BeTrue();
}
```

Test fail → CI fail.

#### 3.2.3 OOP & SOLID rules — balance, không cực đoan

> **Triết lý:** SOLID + SRP để code dễ đọc, dễ test, dễ thay đổi — **KHÔNG phải để fragment file thành mảnh vụn**. Code chia nhỏ quá → mất context, đọc 1 use case phải nhảy qua 8 file, trade off ngược lại. Numbers dưới đây là **guideline rough cho review thảo luận**, KHÔNG phải hard limit CI enforce. Dùng judgment: nếu class lớn nhưng **một mạch một concern** thì OK, nếu class trung bình nhưng **trộn 3 concern** thì vẫn cần tách.
>
> **Nguyên tắc balance:**
> - **Tránh god class** (mix nhiều concern không liên quan) — nhưng cũng **tránh anemic fragmentation** (10 class mỗi cái 5 dòng chỉ để "đẹp SOLID").
> - **Pragmatic threshold để review thảo luận** (không phải fail PR auto):
>   - Handler / Service method ~80–150 dòng: bình thường nếu là một flow rõ ràng. > 200 dòng → review xem có thể tách helper trên entity không.
>   - Service class ~10–20 method là OK. > 20–25 method với concerns khác nhau → cân nhắc tách. Service 5 method gom đúng 1 concern là **good**, đừng split thêm.
>   - Repository ~10–15 method là bình thường (CRUD generic + 5–10 custom query). Tách Read/Write CHỈ khi vượt ~20 method HOẶC khi Query Handler cần immutable read contract rõ ràng.
>   - File C# ~200–400 dòng OK. > 500 dòng → review xem có god class không.
> - **Khi nghi ngờ → ưu tiên gom (less files), tách sau khi thực sự có pain point.** Premature split = premature abstraction.

> **Checklist anti-pattern dưới đây là cờ ĐỎ rõ ràng** (god class, swallow exception, controller chứa business logic, …) — review CHẶN PR. Số liệu kèm theo chỉ là **mốc tham khảo** để mở thảo luận, không auto-reject.

| ❌ Anti-pattern | ✅ Yêu cầu | Lý do |
|---|---|---|
| God service `BookingService` 800–1500 dòng trộn lifecycle + pricing + reporting + integration | Tách khi class trộn **concern không liên quan**. Vd `BookingService` (lifecycle: create/cancel/refund) + `BookingPricingService` (fare/discount calc). KHÔNG cần tách nếu chỉ 10 method cùng concern. | SRP — 1 lý do thay đổi/class, KHÔNG phải 1 method/class. |
| God repository `IBookingRepository` 30+ method trộn read + write + reporting | Tách CHỈ khi vượt ~20 method HOẶC khi cần explicit `IReadRepository` contract cho Query Handler. ~10–15 method là OK. | ISP khi cần thiết, không phải mặc định. |
| Handler 300–500 dòng làm nhiều việc trong 1 use case | Trim xuống logic của use case đó. Đẩy shared logic sang Service / domain method. ~80–150 dòng cho complex flow là OK. > 200 dòng → review. | SRP. Handler đọc top-to-bottom phải nắm flow ngay — không nhảy nhiều quá. |
| Service gọi `DbContext` trực tiếp | Service inject `I<Aggregate>Repository`, không tự query EF Core | DIP. Service nằm ở Application — KHÔNG depend Infrastructure. |
| Repository chứa business logic (`if booking.Status == X then ...`) | Repository CHỈ là data gateway — query + persistence. Business logic ở Domain entity hoặc Service. | SRP. Repository không phải nơi của domain rule. |
| Domain entity depend EF Core (`[Required]`, `[Key]`, navigation virtual) | Domain entity **không có data annotation** ngoài C# language feature. EF mapping tách ra `IEntityTypeConfiguration<T>` ở Infrastructure. | Domain layer pure POCO, không bị lock vào ORM |
| `IMapper` / `Mapper.Map<>` toàn project mặc định | Manual mapping `BookingDto.From(Booking entity)` static factory hoặc extension method. **Mapster OPTIONAL** (chọn thay AutoMapper vì source-gen compile-time, MIT, perf tốt hơn ~3x); nếu dùng phải có `TypeAdapterConfig` per Aggregate, đăng ký qua `services.AddMapster()`. | Manual mapping rõ ràng, debuggable, KHÔNG bắt buộc. AutoMapper KHÔNG dùng — đã chuyển sang Mapster (xem Section 2.1 + changelog 1.3.3). |
| `Helpers`, `Utils`, `Common`, `Misc` class chứa 20+ static method khác nhau | Tách theo concern: `MoneyFormatter`, `DateRangeValidator`, `JwtClaimReader`, … | Naming cụ thể, SRP |
| God entity `Booking` 50 properties + 30 method | Chia entity + sub-entity (`Passenger`, `BookingPendingAction`) + value object (`Money`, `BookingCode`); method nhóm theo lifecycle (`Confirm()`, `Cancel()`, `Refund()`) | DDD aggregate boundary |
| Controller chứa business logic (`if status == ... else ...`) | Controller **chỉ** map HTTP → Command/Query → `MediatR.Send` → map response. Tối đa 10 dòng/action. | Controller là HTTP adapter, KHÔNG sống của domain logic |
| Controller gọi thẳng `IBookingService` (bypass MediatR) | Controller → `MediatR.Send` → Handler → Service. KHÔNG bypass. | Pipeline behaviors (validation, logging, transaction) chỉ chạy qua MediatR. |
| Service inject Service inject Service chain dài (5+ tầng) | Tối đa 2 tầng service: Handler → Service → Repository. Cross-aggregate orchestration cần phối hợp 3+ service → tách ra Saga / Process Manager. | Tránh "service trong service" indirection nightmare. |
| `throw new Exception("...")` generic | `throw new BookingNotCancellableException(bookingId)` custom class kèm errorCode | Catch-able theo type, error code map được |
| `try { … } catch { return null; }` swallow | Để exception propagate, exception filter map sang `ApiResponse` error envelope | Fail loud, Sentry capture |
| `static List<>` hoặc `static Dictionary<>` mutable in-process state | Redis cho shared state hoặc DI singleton có lock proper | Scale ngang vỡ |
| Nullable reference type tắt (`<Nullable>disable</Nullable>`) | **Bật `<Nullable>enable</Nullable>` ở `Directory.Build.props`** + treat nullable warnings as errors | Bắt nullref compile time |
| Async method không `CancellationToken` | Mọi async method (Handler, Service, Repository, HTTP call) **phải nhận `CancellationToken ct`** từ caller | Cancel propagation |
| Tạo `I<Aggregate>Service` nhưng impl chỉ wrap repository 1-1 (passthrough) | Nếu Service không thêm logic ngoài delegate sang Repository → bỏ Service, Handler gọi Repository trực tiếp | YAGNI. Đừng tạo abstraction trống. |

#### 3.2.4 Repository pattern — chi tiết

**Generic base ở libs:**

```csharp
// libs/dotnet/VietRide.Shared.Application/Repositories/IRepository.cs
public interface IRepository<TEntity, TId> where TEntity : class
{
    Task<TEntity?> GetByIdAsync(TId id, CancellationToken ct);
    Task<TEntity> AddAsync(TEntity entity, CancellationToken ct);
    void Update(TEntity entity);
    void Remove(TEntity entity);
    IQueryable<TEntity> Query();        // Escape hatch cho query phức tạp — return tracked
    IQueryable<TEntity> QueryNoTracking(); // AsNoTracking cho read-only
}

// libs/dotnet/VietRide.Shared.Persistence/EfRepository.cs
public class EfRepository<TEntity, TId> : IRepository<TEntity, TId> where TEntity : class
{
    protected readonly DbContext _db;
    public EfRepository(DbContext db) { _db = db; }
    public Task<TEntity?> GetByIdAsync(TId id, CancellationToken ct) => _db.Set<TEntity>().FindAsync(new object[]{id!}, ct).AsTask();
    public async Task<TEntity> AddAsync(TEntity e, CancellationToken ct) { await _db.Set<TEntity>().AddAsync(e, ct); return e; }
    public void Update(TEntity e) => _db.Set<TEntity>().Update(e);
    public void Remove(TEntity e) => _db.Set<TEntity>().Remove(e);
    public IQueryable<TEntity> Query() => _db.Set<TEntity>();
    public IQueryable<TEntity> QueryNoTracking() => _db.Set<TEntity>().AsNoTracking();
}
```

**Per-aggregate interface + impl:**

```csharp
// apps/booking/src/VietRide.Booking.Application/Abstractions/Repositories/IBookingRepository.cs
public interface IBookingRepository : IRepository<Booking, Guid>
{
    Task<Booking?> GetByCodeAsync(string code, CancellationToken ct);
    Task<List<Booking>> GetActiveByOperatorAsync(Guid operatorId, CancellationToken ct);
    Task<List<Booking>> GetByTripIdWithPassengersAsync(Guid tripId, CancellationToken ct);
}

// apps/booking/src/VietRide.Booking.Infrastructure/Persistence/Repositories/BookingRepository.cs
public class BookingRepository : EfRepository<Booking, Guid>, IBookingRepository
{
    public BookingRepository(ApplicationDbContext db) : base(db) { }

    public Task<Booking?> GetByCodeAsync(string code, CancellationToken ct)
        => _db.Set<Booking>().FirstOrDefaultAsync(b => b.Code == code, ct);

    public Task<List<Booking>> GetActiveByOperatorAsync(Guid operatorId, CancellationToken ct)
        => _db.Set<Booking>()
              .Where(b => b.OperatorId == operatorId && b.Status != BookingStatus.EXPIRED)
              .ToListAsync(ct);

    public Task<List<Booking>> GetByTripIdWithPassengersAsync(Guid tripId, CancellationToken ct)
        => _db.Set<Booking>()
              .Include(b => b.Passengers)
              .Where(b => b.TripId == tripId)
              .ToListAsync(ct);
}
```

**Repository nhỏ hơn 10 method.** Vượt → tách `IBookingReadRepository` (Get*, List*) và `IBookingWriteRepository` (Add, Update, Remove, custom mutation).

**Optional `IQueryable<T>` extension method** — dùng khi cần compose query rất linh hoạt giữa nhiều repository hoặc nhiều Handler:

```csharp
// VietRide.Booking.Infrastructure/Persistence/Queries/BookingQueries.cs
public static class BookingQueries
{
    public static IQueryable<Booking> WhereActiveForOperator(this IQueryable<Booking> q, Guid operatorId)
        => q.Where(b => b.OperatorId == operatorId && b.Status != BookingStatus.EXPIRED);

    public static IQueryable<Booking> WithPassengersAndPayments(this IQueryable<Booking> q)
        => q.Include(b => b.Passengers).Include(b => b.Payments);
}
```

Repository method có thể dùng extension internal: `Query().WhereActiveForOperator(opId).ToListAsync(ct)`.

#### 3.2.5 Service pattern — chi tiết

```csharp
// apps/booking/src/VietRide.Booking.Application/Abstractions/Services/IBookingService.cs
public interface IBookingService
{
    Task<Money> CalculateCancellationFeeAsync(Booking booking, DateTimeOffset now, CancellationToken ct);
    Task<Result<Booking>> ApplyVoucherAsync(Booking booking, string voucherCode, CancellationToken ct);
    Task ReleaseSeatsAsync(Booking booking, CancellationToken ct);
}

// apps/booking/src/VietRide.Booking.Application/Services/BookingService.cs
public class BookingService : IBookingService
{
    private readonly IBookingRepository _bookingRepo;
    private readonly IVoucherRepository _voucherRepo;
    private readonly ITripServiceClient _tripClient;
    private readonly IClock _clock;

    public BookingService(IBookingRepository br, IVoucherRepository vr, ITripServiceClient tc, IClock clock)
    {
        _bookingRepo = br; _voucherRepo = vr; _tripClient = tc; _clock = clock;
    }

    public Task<Money> CalculateCancellationFeeAsync(Booking booking, DateTimeOffset now, CancellationToken ct)
    {
        // Business logic reusable cross-handler (CancelBookingHandler, RefundEstimateHandler, ...)
    }
    // ...
}
```

**Service dùng khi:**

- Logic reusable **≥2 Handler** (vd CalculateCancellationFee dùng bởi `CancelBookingHandler` + `GetRefundEstimateQueryHandler`).
- Cross-aggregate orchestration trong cùng service (vd `BookingService.ApplyVoucher()` cần đọc Voucher + Booking + check OperatorVoucherConsent).
- Logic gọi nhiều external client + repository (compose dependencies).

**KHÔNG tạo Service khi:**

- Logic chỉ dùng 1 lần trong 1 Handler → viết thẳng Handler.
- Service chỉ delegate sang Repository 1-1 không thêm logic → bỏ Service, Handler gọi Repository.

#### 3.2.6 Khi nào ĐƯỢC tạo interface?

| Tình huống | Tạo interface? | Lý do |
|---|---|---|
| Per-aggregate Repository (`IBookingRepository`) | ✅ **Bắt buộc** | DIP — Application không depend EF Core directly. Test mock được. |
| Per-aggregate Application Service (`IBookingService`) | ✅ **Bắt buộc khi có shared logic ≥2 handler** | Test mock service trong handler test |
| External SaaS (VNPay, SendGrid, Firebase, Google Maps) | ✅ Bắt buộc | Mock cho test, swap provider |
| Inter-service HTTP client | ✅ Bắt buộc | Mock test, Polly wrap |
| `IClock` cho `DateTime.UtcNow` | ✅ Có | Freeze time trong test |
| `IEventPublisher` (Outbox wrapper) | ✅ Có | Mock cho unit test handler không cần real RabbitMQ |
| `IUnitOfWork` | ⚙️ Optional | Nếu transaction logic phức tạp ngoài `TransactionBehavior` pipeline. Đa số dùng pipeline đủ. |
| Domain logic / Entity | ❌ Không | Entity là implementation cụ thể |
| Internal helper / utility | ❌ Không | YAGNI — chỉ tạo khi cần mock |
| `ApplicationDbContext` (DbContext) | ❌ Không | Repository đã abstract. Service không inject DbContext direct. |

### 3.3 NestJS service layout (Nx-generated)

> **Generation:** `nx g @nx/nest:app <service-name>` từ workspace root. Nx tự tạo `apps/<service-name>/` + `apps/<service-name>-e2e/` (separate e2e project).
>
> ⚠️ **Reminder:** Cây thư mục + file dưới đây là **ví dụ minh họa convention** — KHÔNG phải danh sách bắt buộc. Agent tự quyết định module/file cần tạo dựa trên use case thực tế. Vd: Gateway không cần `modules/<feature>/entities/` (không có DB); Notification không cần `outbox/` (chỉ consume); Tracking cần thêm `gateways/` folder cho Socket.IO `@WebSocketGateway`. Xem callout đầu Section 3.
>
> **Cùng nguyên tắc 3.2.3 áp dụng:** 1 file = 1 class/responsibility (theo balance, không cực đoan). NestJS service class (`<Feature>Service`) là OK — đó là Nest pattern. Tuân thủ anti-pattern checklist 3.2.3 (god service, controller chứa business logic, service-trong-service chain dài...).

```
apps/<service>/                                    (Nx project root)
├── project.json                                   Nx target: build, serve, test, lint, docker-build
├── tsconfig.app.json                              extends ../../tsconfig.base.json
├── tsconfig.spec.json
├── jest.config.ts                                 extends jest.preset.js (workspace root)
├── Dockerfile
├── webpack.config.js                              Nx Nest builder config
├── src/
│   ├── main.ts                                    Bootstrap — KHÔNG chứa business logic
│   ├── app/
│   │   └── app.module.ts                          Root module — import feature modules + infra modules
│   ├── config/
│   │   ├── env.schema.ts                          Zod schema validate env at startup
│   │   └── configuration.ts                       Typed config factory
│   ├── common/                                    Cross-cutting (service-local) — nếu reusable cross-service → push lên libs/shared/nest-common/
│   │   ├── guards/
│   │   │   ├── jwt-auth.guard.ts                  1 file/class
│   │   │   ├── internal-jwt.guard.ts
│   │   │   └── roles.guard.ts
│   │   ├── decorators/
│   │   │   ├── current-user.decorator.ts
│   │   │   └── roles.decorator.ts
│   │   ├── filters/
│   │   │   └── problem-json-exception.filter.ts
│   │   ├── interceptors/
│   │   │   ├── logging.interceptor.ts
│   │   │   └── idempotency.interceptor.ts
│   │   └── pipes/
│   │       └── zod-validation.pipe.ts
│   ├── infrastructure/                            Concrete adapters (1 module per integration)
│   │   ├── database/
│   │   │   ├── database.module.ts                 Prisma forRoot
│   │   │   ├── data-source.ts                     CLI migration source
│   │   │   └── migrations/<timestamp>-<name>.ts   1 file = 1 migration
│   │   ├── redis/redis.module.ts
│   │   ├── rabbitmq/
│   │   │   ├── rabbitmq.module.ts
│   │   │   ├── rabbitmq.producer.ts               implements EventPublisher contract từ libs/shared
│   │   │   └── rabbitmq.consumer.ts               Subscribe queue, route → handler
│   │   ├── bullmq/
│   │   │   ├── bullmq.module.ts
│   │   │   └── queues/<queue-name>.queue.ts       1 file/queue
│   │   ├── firebase/firebase.module.ts            (Notification only)
│   │   ├── sendgrid/sendgrid.client.ts            (Notification only — 1 class, 1 file)
│   │   └── http/
│   │       ├── identity.client.ts                 1 client/upstream service
│   │       ├── trip.client.ts
│   │       └── booking.client.ts
│   ├── modules/                                   ⭐ 1 folder = 1 feature/aggregate
│   │   └── <feature>/
│   │       ├── <feature>.module.ts
│   │       ├── <feature>.controller.ts            Thin — gọi service, KHÔNG chứa business logic
│   │       ├── <feature>.service.ts               Domain logic — KHÔNG bọc IXyzService
│   │       ├── entities/
│   │       │   └── <entity>.entity.ts             1 file/entity (Prisma)
│   │       ├── dto/
│   │       │   ├── create-<feature>.dto.ts        Zod schema + `type X = z.infer<typeof Schema>`
│   │       │   └── <feature>-response.dto.ts
│   │       ├── handlers/                          1 file/handler — RabbitMQ event consumers
│   │       │   └── <event-name>.handler.ts
│   │       └── workers/                           1 file/worker — BullMQ job processors
│   │           └── <queue-name>.worker.ts
│   ├── health/
│   │   ├── health.module.ts
│   │   └── health.controller.ts                   /health, /ready
│   └── outbox/                                    Chỉ có ở Tracking, RAG (services publish event)
│       ├── outbox.module.ts
│       ├── outbox.entity.ts
│       └── outbox.publisher.ts                    BullMQ scheduled poll every 5s
└── (per-app tests sống ở apps/<service>-e2e/ riêng do Nx generate)
```

#### 3.3.1 NestJS OOP & SOLID rules

Cùng checklist như 3.2.3, dịch sang NestJS context:

| ❌ Anti-pattern | ✅ Yêu cầu |
|---|---|
| `XyzService` 800 dòng làm CRUD + business + integration | Tách: `XyzService` (business), `XyzRepository` (chỉ nếu cần — thường Prisma repo đã đủ inject direct), `XyzClient` (external HTTP) |
| `IXyzService` interface + class implement (vì "test dễ hơn") | Inject class concrete. Jest mock module được — không cần interface. |
| Controller chứa logic + repository call trực tiếp | Controller → Service. Tối đa 10 dòng/method handler. |
| Module import nhau vòng tròn | Dùng `forwardRef()` cẩn thận; chia lại nếu vòng tròn |
| God module `AppModule` import 30 thứ trực tiếp | `AppModule` import feature module; feature module tự import infra cần thiết |
| `any` type trong DTO/return | Explicit type — derive từ Zod schema qua `z.infer` |
| Async without `await` (fire-and-forget) | Hoặc `await`, hoặc enqueue BullMQ rõ ràng |
| Hardcode URL/secret trong code | Đọc qua ConfigService (validated Zod) |
| Mỗi file > ~250 dòng | Cảnh báo — review xem có phải god class không |

#### 3.3.2 NestJS service vs MediatR Handler — sự khác biệt với .NET

NestJS KHÔNG dùng MediatR (có `@nestjs/cqrs` nhưng v1 KHÔNG dùng — quá nặng cho integration-heavy services như Tracking/Notification/RAG). Pipeline NestJS:

```
HTTP request
  → Guard (JwtAuth + Roles)
  → Pipe (ZodValidation)
  → Interceptor (Logging + Idempotency)
  → Controller method
  → Service method (1 method/use case — KHÔNG bọc thêm)
  → Prisma Repository inject trực tiếp HOẶC raw DataSource cho transaction
  → Response
```

**Service method = 1 use case = 1 method.** KHÔNG có `XyzService` 20 method gộp lại. Nếu service vượt ~250 dòng → tách `XyzCommandService` (write) và `XyzQueryService` (read).

### 3.4 Gateway-specific layout

#### 3.4.1 Tech stack — KHÔNG dùng Kong / YARP / Tyk / Express Gateway

> **Quyết định:** Gateway là **NestJS HTTP app custom** dùng `http-proxy-middleware` + `jose` (sign Internal JWT) + `ioredis` (rate limit + JWKS cache). KHÔNG dùng dedicated API Gateway product. Canonical source: `SU26SE101_VIETRIDE_technical_context_v7.md` Section 3.2.

**So sánh & lý do reject các option khác:**

| Option | Vai trò | Lý do KHÔNG chọn |
|---|---|---|
| **Kong** | Standalone API Gateway (Lua plugins, PostgreSQL/Cassandra backend, Admin API) | Overkill capstone scale. Cần thêm 1 container + 1 DB cho Kong itself. Custom auth logic (Internal JWT sign 120s + phone-completion gate cho passenger) phải viết qua Lua plugin — team không quen Lua. Operational burden tăng (Kong upgrade, plugin compatibility). Free tier Kong OSS thiếu nhiều feature mà Enterprise có. |
| **Kong Gateway in DB-less mode** | Stateless Kong với declarative config YAML | Đỡ hơn nhưng vẫn cần Lua plugin cho Internal JWT signing — team không có expertise. Debug khó. |
| **YARP (.NET reverse proxy)** | Microsoft official reverse proxy library cho .NET | Mạnh, performance tốt — nhưng nặng config (route table + cluster definition trong appsettings). Custom auth logic phải viết Middleware .NET. Mâu thuẫn với quyết định Gateway dùng cùng stack với 3 NestJS services khác (Tracking, Notification, RAG) → 1 framework dễ team maintain. |
| **Tyk** | Open-source API Gateway (Go) | Tương tự Kong — overkill, cần thêm Redis riêng cho Tyk, custom auth phải plugin Go middleware. |
| **Express Gateway** | NestJS-tương đương dựa trên Express | Đã abandoned (last release 2020). Không dùng. |
| **AWS API Gateway / Azure APIM** | Managed cloud service | Vendor lock-in. Capstone deploy on-prem hoặc VPS đơn lẻ — không có lý do gắn cloud SaaS gateway. Cost/dev experience không phù hợp. |
| **Nginx (chỉ Nginx, không có app gateway)** | Reverse proxy Layer 7 thuần | Nginx KHÔNG sign được Internal JWT (cần OpenResty + Lua + JWT module — về cơ bản là rolling Kong tự chế). Nginx vẫn dùng làm SSL termination + WebSocket upgrade (xem 11.6) nhưng đứng **trước** Gateway, không thay Gateway. |
| **✅ NestJS + `http-proxy-middleware` (chosen)** | Custom thin gateway | Cùng stack với Tracking/Notification/RAG (1 framework, 1 team skillset). Guard/Interceptor/Middleware first-class — JWT validation + Internal JWT signing + rate limit + phone-completion gate viết ~vài trăm dòng TS, dễ test/debug/maintain. Không cần thêm container ngoài app + Redis (đã có). Operational overhead = 0. |

**Stack chính xác của Gateway:**

| Concern | Thư viện | Notes |
|---|---|---|
| HTTP framework | NestJS 11.x + Express adapter (default) | Fastify adapter cũng OK nếu cần performance — v1 dùng Express |
| Reverse proxy | `http-proxy-middleware` v3.x | Forward method/body/query/header nguyên vẹn tới downstream service |
| User JWT verify (RS256) | `jose` v5.x + JWKS từ Identity Service | Cache JWKS trong Redis key `identity:jwks_cache` TTL 1h |
| Internal JWT sign (HS256) | `jose` v5.x | TTL 120s, shared secret `INTERNAL_JWT_SECRET` |
| Rate limit | `@nestjs/throttler` HOẶC custom Redis-based middleware | Per IP per route, key `gateway:rate_limit:{ip}:{route}` TTL 1p |
| Idempotency dedupe | KHÔNG ở Gateway — handled ở từng business service (Section 5.6 + 9.8) | Gateway chỉ forward `Idempotency-Key` header |
| SSL termination | KHÔNG ở Gateway — **Nginx đứng trước** terminate SSL | Gateway nhận HTTP plain trong Docker network nội bộ |
| WebSocket upgrade cho Tracking | KHÔNG ở Gateway — **Nginx route trực tiếp** `/tracking/socket.io/` tới Tracking Service | Gateway KHÔNG proxy WebSocket (xem 11.6) |
| Request ID propagation | Custom middleware — gen UUID hoặc forward `X-Request-Id` từ client | Inject vào downstream request + log |
| CORS | NestJS built-in `app.enableCors(...)` | Whitelist origin theo env |

#### 3.4.2 Routing approach

**Route table config-driven (KHÔNG viết controller per endpoint):**

```ts
// apps/gateway/src/config/routes.ts
export const ROUTE_TABLE: ProxyRoute[] = [
  { prefix: '/v1/auth',         target: env.IDENTITY_BASE_URL,     authRequired: false },
  { prefix: '/v1/users',        target: env.IDENTITY_BASE_URL,     authRequired: true  },
  { prefix: '/v1/operators',    target: env.IDENTITY_BASE_URL,     authRequired: 'mixed' /* /register public, rest auth */ },
  { prefix: '/v1/admin',        target: env.IDENTITY_BASE_URL,     authRequired: true, requiredRoles: ['SYSTEM_ADMIN'] },
  { prefix: '/v1/trips',        target: env.TRIP_BASE_URL,         authRequired: true  },
  { prefix: '/v1/routes',       target: env.TRIP_BASE_URL,         authRequired: true  },
  { prefix: '/v1/stations',     target: env.TRIP_BASE_URL,         authRequired: true  },
  { prefix: '/v1/stops',        target: env.TRIP_BASE_URL,         authRequired: true  },
  { prefix: '/v1/vehicles',     target: env.TRIP_BASE_URL,         authRequired: true  },
  { prefix: '/v1/operator',     target: 'multi'                                          /* role-based dispatch tới đúng service */ },
  { prefix: '/v1/bookings',     target: env.BOOKING_BASE_URL,      authRequired: true  },
  { prefix: '/v1/vouchers',     target: env.BOOKING_BASE_URL,      authRequired: true  },
  { prefix: '/v1/crew/parcels', target: env.PARCEL_BASE_URL,       authRequired: true, requiredRoles: ['DRIVER', 'ASSISTANT'] },
  { prefix: '/v1/parcels/delivery', target: env.PARCEL_BASE_URL,   authRequired: 'mixed',
    publicSubpaths: [
      { method: 'POST', path: '/v1/parcels/delivery/confirm' },
      { method: 'POST', path: '/v1/parcels/delivery/reject' },
      { method: 'POST', path: '/v1/parcels/delivery/undo-reject' },
    ] },
  { prefix: '/v1/parcels',      target: env.PARCEL_BASE_URL,       authRequired: true  },
  { prefix: '/v1/payments',     target: env.PAYMENT_BASE_URL,      authRequired: 'mixed' /* vnpay-ipn callback public */ },
  { prefix: '/v1/wallet',       target: env.PAYMENT_BASE_URL,      authRequired: true  },
  { prefix: '/v1/notifications',target: env.NOTIFICATION_BASE_URL, authRequired: true  },
  { prefix: '/v1/rag',          target: env.RAG_BASE_URL,          authRequired: true  },
  // /tracking/socket.io/* → KHÔNG qua Gateway (Nginx route trực tiếp)
];
```

**Gateway middleware chain (đúng thứ tự):**

```
Request
  → CorsMiddleware
  → RequestIdMiddleware            (gen/forward X-Request-Id)
  → RateLimitMiddleware            (per IP per route, Redis-backed)
  → RouteMatcher                   (match prefix → ProxyRoute config)
  → UserJwtVerifyMiddleware        (skip nếu authRequired=false; verify RS256 + JWKS)
  → RoleCheckGuard                 (nếu requiredRoles được khai báo)
  → PhoneCompleteGate              (block PASSENGER có phone=NULL trừ whitelist endpoints — xem 6.6)
  → InternalJwtSigner              (sign HS256 120s với userId/role/operatorId claim)
  → ProxyForwarder                 (http-proxy-middleware → downstream service base URL + X-Internal-Auth header)
  → Response
```

**Exceptions (viết controller tay, KHÔNG proxy):**

- `GET /health`, `GET /ready` — Gateway tự handle (check downstream service reachable optional)
- VNPay IPN callback (`GET` canonical, temporary `POST` compatibility on `/v1/payments/vnpay-ipn` and `/v1/payments/vnpay-topup-ipn`) — public, signature verify ở Payment Service, Gateway chỉ forward (KHÔNG sign Internal JWT vì call này external)

#### 3.4.3 Folder layout

Gateway KHÔNG có business modules — chỉ proxy + auth:

```
apps/gateway/src/
├── main.ts
├── app/app.module.ts
├── config/
│   ├── env.schema.ts                            Zod env validation
│   └── routes.ts                                Route table: prefix → downstream base URL (config-driven)
├── auth/
│   ├── user-jwt.middleware.ts                   Verify RS256 via JWKS
│   ├── jwks-cache.service.ts                    Fetch + cache JWKS (Redis-backed)
│   ├── internal-jwt.signer.ts                   Sign HS256 120s với INTERNAL_JWT_SECRET
│   └── phone-complete.guard.ts                  Block nếu phone IS NULL + role=PASSENGER (whitelist endpoints)
├── rate-limit/
│   └── redis-rate-limit.middleware.ts           Per IP per route
├── proxy/
│   └── proxy.middleware.ts                      http-proxy-middleware factory (inject X-Internal-Auth + X-Request-Id)
└── health/health.controller.ts
```

### 3.5 Naming conventions (mã nguồn)

**.NET:**

| Loại | Convention | Ví dụ |
|---|---|---|
| Project | `VietRide.<Service>.<Layer>` | `VietRide.Booking.Application` |
| Namespace | Match folder | `VietRide.Booking.Application.Features.Booking.Create` |
| Entity | PascalCase singular | `Booking`, `TripSeat`, `Voucher` |
| DbSet | PascalCase plural | `Bookings`, `TripSeats`, `Vouchers` |
| Property | PascalCase | `TotalAmount`, `DepartureDateTime` |
| Enum | PascalCase, value SCREAMING_SNAKE_CASE | `BookingStatus.PENDING_PAYMENT` |
| Command/Query | `<Verb><Aggregate>Command/Query` | `CreateBookingCommand`, `GetBookingByIdQuery` |
| Handler | `<Command/Query>Handler` | `CreateBookingCommandHandler` |
| Validator | `<Command/Query>Validator` | `CreateBookingCommandValidator` |
| Event (domain) | `<Aggregate><Verb>ed` | `BookingConfirmed`, `TripDisrupted` |
| Event (integration / RabbitMQ) | `<service>.<aggregate>.<verb_ed>` | `booking.booking.confirmed`, `payment.payment.succeeded` |
| External client interface | `I<Vendor><Capability>Client` | `IVnPayClient`, `ISendGridEmailClient`, `IFcmPushClient` |
| External client impl | `<Vendor><Capability>Client` | `VnPayClient`, `SendGridEmailClient`, `FcmPushClient` |
| Inter-service HTTP client | `I<Service>ServiceClient` + `<Service>ServiceClient` | `ITripServiceClient`, `TripServiceClient` |
| Generic repository base (libs) | `IRepository<TEntity, TId>` + `EfRepository<TEntity, TId>` | xem 3.2.4 |
| Per-aggregate repository | `I<Aggregate>Repository` + `<Aggregate>Repository` | `IBookingRepository`, `BookingRepository` |
| Read-only repository (khi tách read/write) | `I<Aggregate>ReadRepository` | `IBookingReadRepository` |
| Application service interface | `I<Aggregate>Service` | `IBookingService`, `IBookingPricingService` |
| Application service impl | `<Aggregate>Service` | `BookingService`, `BookingPricingService` |
| Unit of Work (optional) | `IUnitOfWork` + `EfUnitOfWork` | xem 5.11 |
| DTO | `<Aggregate><Verb>Dto` hoặc response-shape | `BookingResponse`, `CreateBookingRequest` |

**NestJS:**

| Loại | Convention | Ví dụ |
|---|---|---|
| File | kebab-case | `booking.controller.ts`, `create-parcel.dto.ts` |
| Class | PascalCase | `BookingController`, `CreateParcelDto` |
| Interface | PascalCase, prefix `I` optional | `BookingResponse` |
| Module | `<Feature>Module` | `TrackingModule` |
| Service | `<Feature>Service` | `GpsTrackingService` |
| Event handler | `<Event>Handler` | `BookingConfirmedHandler` |
| Decorator | camelCase | `@CurrentUser()`, `@Roles('OPERATOR_ADMIN')` |

**DB:**

| Loại | Convention | Ví dụ |
|---|---|---|
| Table | plural snake_case | `bookings`, `trip_seats`, `voucher_usages` |
| Column | snake_case | `passenger_user_id`, `total_amount`, `departure_date_time` |
| PK | `id UUID DEFAULT gen_random_uuid()` | |
| FK column | `<entity>_id` | `trip_id`, `pickup_stop_id` |
| Index | `idx_<table>_<columns>` | `idx_bookings_trip_id_status` |
| Unique | `uq_<table>_<columns>` | `uq_users_email` |
| Check | `chk_<table>_<rule>` | `chk_payments_amount_non_negative` |

EF Core / Prisma auto-map PascalCase property ↔ snake_case column qua naming strategy (cấu hình ở `ApplicationDbContext.OnModelCreating` / Prisma `namingStrategy`).

### 3.6 Shared libs philosophy — `libs/` vs `apps/`

> **TL;DR:** `libs/` chứa **building blocks generic** (patterns + helpers + cross-cutting). `apps/<service>/` chứa **domain + infrastructure cụ thể** của service đó. Mỗi app import lib qua project reference (.NET) hoặc TS path alias (NestJS) rồi tự config DI/Module setup.

#### Quan trọng — libs KHÔNG chứa gì?

> **`libs/` KHÔNG có Domain nghiệp vụ.** Booking, Trip, Parcel, Station, Voucher, … sống **trong `apps/<service>/Domain/`**. Lib chỉ có shared **kernel primitives** (Money, Result<T>, BaseEntity marker) — đây là DDD "shared kernel", không phải domain nghiệp vụ.
>
> **`libs/` KHÔNG có Infrastructure cụ thể.** ApplicationDbContext, EntityTypeConfiguration, Migration, VnPayClient, SendGridEmailClient, TripServiceClient, … sống **trong `apps/<service>/Infrastructure/`**. Lib chỉ có **generic infrastructure helpers** (EfRepository<T,TId> base class, interceptors, RabbitMQ wrapper, Polly factory) — pattern và helper, không phải concrete service implementation.
>
> **Diễn giải nhanh:**
> - Lib có "generic + cross-cutting + primitive" → reusable an toàn.
> - App có "domain nghiệp vụ + infrastructure cụ thể của service" → mỗi service tự lo, không leak ra lib.

#### Phân loại theo layer

| Layer | ✅ Thuộc `libs/` (generic) | ❌ Thuộc `apps/<service>/` (service-specific) |
|---|---|---|
| **Domain** | Shared kernel primitives: `Money`, `PhoneNumber`, `Result<T>`, `Error`, `BaseEntity`, `IAuditable`, `ISoftDeletable`, `IActivatable` markers, `IClock`, `DomainException` base | **Mọi domain entity nghiệp vụ:** `Booking`, `Trip`, `Parcel`, `Station`, `Stop`, `Route`, `Voucher`, `User`, `Operator`. Domain-specific VO (`BookingCode`, `ParcelCode`). Domain event (`BookingConfirmed`). Domain-specific enum (`BookingStatus`, `TripStatus`, `ParcelStatus`). Domain method (`booking.Confirm()`, `trip.StartBoarding()`). |
| **Application** | Generic contracts: `IRepository<T,TId>`, `IReadRepository<T>`, `IUnitOfWork`, `IApplicationService` marker. MediatR behaviors generic (`ValidationBehavior`, `LoggingBehavior`, `TransactionBehavior`). Common exception types (`ValidationException`, `NotFoundException`, …). `PagedResult<T>`. `IEventPublisher` interface. | Per-aggregate `IBookingRepository`, `IBookingService` (interface + impl). Specific Command/Query/Handler/Validator. Event consume handler. DTO. Mapping logic. External client interface (`IVnPayClient`, `ITripServiceClient`) — vì impl service-specific. |
| **Infrastructure** | EF Core generic helpers: `EfRepository<T,TId>`, `EfUnitOfWork`, interceptors (`AuditingInterceptor`, `SoftDeleteInterceptor`, `OutboxInterceptor`), `SnakeCaseNamingConvention`, `OutboxEvent` base entity. RabbitMQ wrapper: `RabbitMqConnectionFactory`, `OutboxEventPublisher`, `OutboxPublisherHostedService` base, `RoutingKeys` constants. HTTP helpers: `PollyPolicyBuilder`, `InternalJwtPropagationHandler` (DelegatingHandler), optional `BaseHttpClient`. | **`ApplicationDbContext` concrete** (DbSet per entity). Per-entity `IEntityTypeConfiguration<T>`. **EF migrations**. Per-aggregate repository impl (`BookingRepository : EfRepository<Booking,Guid>, IBookingRepository`). Concrete external client impl: `VnPayClient`, `SendGridEmailClient`, `FcmPushClient`, `FirebaseStorageClient`, `GoogleDirectionsClient`. Concrete inter-service HTTP client: `TripServiceClient`, `IdentityServiceClient`, `PaymentServiceClient`. Concrete RabbitMQ consumer dispatcher. |
| **Api / Web** | ASP.NET Core helpers: `InternalJwtAuthenticationHandler`, `JwksAuthenticationExtensions`, `ApiResponseExceptionFilter` (global exception → ApiResponse error envelope), `ApiResponseResultFilter` (success-wrap), `RequestLoggingMiddleware`, `IdempotencyMiddleware`, `HealthCheckBuilderExtensions`, Swagger setup defaults | Controllers, custom service-specific Guard/Filter, `Program.cs` composition root, appsettings, route registration. |
| **NestJS Common** | `JwtAuthGuard`, `InternalJwtGuard`, `RolesGuard`, `ProblemJsonExceptionFilter`, `RequestContextMiddleware`, `ZodValidationPipe`, `@CurrentUser()`, `@Roles()` decorators | Custom guard service-specific (vd `OperatorTenantGuard`), feature module (`BookingModule`, `TrackingModule`), service class, Prisma entity, controller. |
| **NestJS Infrastructure** | `nest-rabbitmq` (connection factory + producer/consumer base + Outbox base), `nest-persistence` (naming strategy + base entity + soft-delete subscriber), `nest-redis` (IoRedis module factory), `nest-config` (Zod env schema base + ConfigModule factory) | Prisma config service-specific, migration files, BullMQ queue worker logic, business handler. |
| **Contracts (TS shared)** | Error code enum, event payload types, DTO interface types (FE+BE consume) | Service-specific internal types (Prisma entity, query result shape không expose ra ngoài). |

#### Quy ước nhanh

**Vào lib khi:**
- Generic / không depend bất kỳ domain nào.
- Cross-cutting (auth, logging, exception handling, metrics).
- Helper/factory pattern wrap thư viện ngoài (EF Core, RabbitMQ, Polly, Redis) — không lock vào service nào.
- Contract shared FE-BE (error code, event payload type).

**Ở lại app khi:**
- Là **domain nghiệp vụ** (entity, business rule, status machine, domain method).
- Là **infrastructure cụ thể** (DbContext của service, migration, concrete external client của service đó).
- Logic phụ thuộc operatorId/tenant rule của domain (vd `OperatorTenantGuard`).
- Per-aggregate Repository/Service (extend generic từ lib).

#### Service tự config gì khi dùng lib?

Mỗi service phải tự:

1. **Khai báo dependency:** ProjectReference (`.NET`) hoặc TS path import (`@vietride/nest-common` etc.).
2. **Đăng ký DI:** `services.AddSharedApplication()` + `services.AddSharedPersistence()` + `services.AddSharedWeb()` (extension method từ lib) — Service tự choose lib nào dùng.
3. **Cung cấp DbContext concrete:** Lib không biết DbContext nào — Service register `ApplicationDbContext` rồi pass vào `EfRepository<T,TId>` factory.
4. **Define entity-specific `IEntityTypeConfiguration<T>`:** Lib cung cấp interceptor + naming, Service tự map entity của mình.
5. **Define per-aggregate `I<Aggregate>Repository`:** Lib chỉ có generic, Service extend.
6. **Config env-specific:** Lib cung cấp Zod schema base/factory, Service compose schema riêng (thêm DB connection string, API key, …).

#### Versioning

- Mỗi lib có riêng `*.csproj` (hoặc TS package.json) version — bump SemVer khi breaking change.
- App pin version qua ProjectReference (no NuGet publishing v1). Khi update lib → mọi app affected rebuild qua `nx affected -t build`.
- Breaking change ở lib → **bắt buộc** sync update mọi consumer trong cùng PR. Không có "1 service dùng old API, 1 service dùng new API" cho lib.

#### Nguyên tắc tạo lib mới

Tạo lib mới khi:

- **≥2 services** thực sự cần cùng building block (không phải "có thể sẽ cần").
- Building block đó **không depend domain logic** của bất kỳ service nào.
- Có thể test độc lập (unit test trong lib's test project).

KHÔNG tạo lib khi:

- Chỉ 1 service dùng → để trong service.
- Code phụ thuộc domain (vd `BookingValidator`) → service-specific, KHÔNG share.
- Premature abstraction "biết đâu sau này cần" → YAGNI.

---

## 4. DB Schema Reference

> **Đọc canonical:** `db-schema/_global/README.md` + per-service `db-schema/<service>/schema.sql` + `db-schema/_global/cross-service-references.md`.
> Section này KHÔNG paste DDL — chỉ inventory + bootstrap order + path map.

### 4.1 Database bootstrap order

```
Step 1: vietride_identity              (Identity & User — 7 service khác có logical FK đến User/Operator)
Step 2: vietride_trip                  (Booking/Parcel ref Trip/Route/Stop/Station)
Step 3 (parallel): vietride_booking, vietride_payment, vietride_parcel
Step 4 (parallel): vietride_tracking, vietride_notification, vietride_rag
```

Idempotent: chạy migration 2 lần không lỗi (EF Core / Prisma migrations history tự handle).

### 4.2 Entity inventory per service

> Mỗi entity dưới đây được mô tả chi tiết (business field requirements, lifecycle, rationale) trong `SU26SE101_VIETRIDE_technical_context_v7.md` Section 8 — "Entity Requirements per Service". DDL nằm trong `db-schema/<service>/schema.sql`.

#### Identity & User (`vietride_identity`)

`User` · `RefreshToken` · `EmailVerificationToken` · `OAuthIdentity` · `Operator` · `SubscriptionPlan` · `OperatorSubscription` · `ActivityLog` · `UserDevice` · `OutboxEvent`

#### Trip-Route-Vehicle (`vietride_trip`)

`Location` is the admin-managed public origin/destination catalog used by FE trip search; `Station.locationId` and `Stop.locationId` are nullable links to this catalog.

`Station` · `OperatorStation` · `Stop` · `Route` · `RouteStop` · `RouteStopFareTemplate` · `OperatorFareSurchargeSetting` · `OperatorFareSurchargePeriod` · `AlternativeRoute` · `AlternativeRouteStop` · `RouteChangeProposal` · `RouteChangeProposalStop` · `VehicleType` · `Vehicle` · `Trip` · `TripSeat` · `TripStop` · `TripStopFare` · `DriverSchedule` · `TripGenerationSkipLog` · `TripAuditLog` · `DriverScheduleAuditLog` · `ShuttleTrip` · `ShuttlePassenger` · `Incident` · `OutboxEvent`

#### Booking (`vietride_booking`)

`Booking` · `Ticket` · `BookingPendingAction` · `Passenger` · `BookingTransfer` · `BookingStats` · `Voucher` · `VoucherUsage` · `OperatorVoucherConsent` · `OutboxEvent`

#### Payment & Wallet (`vietride_payment`)

`Payment` · `TopUpRequest` · `Wallet` · `WalletTransaction` · `Invoice` · `PlatformWallet` · `PlatformWalletTransaction` · `OperatorLedgerEntry` · `OperatorWallet` · `OperatorWalletTransaction` · `OperatorTripSettlement` · `RefundFailureLog` · `OutboxEvent`

#### Parcel (`vietride_parcel`)

`Parcel` · `ParcelRouteFare` · `ParcelStats` · `OutboxEvent`

#### Tracking (`vietride_tracking`)

`GpsTrail` · `OutboxEvent`
Realtime state (last position, ETA cache, off-route timer) → Redis only.

#### Notification (`vietride_notification`)

`Notification` · `NotificationDelivery`
**KHÔNG có `OutboxEvent`** (chỉ consume RabbitMQ, không publish).

#### RAG AI (`vietride_rag`)

`KnowledgeDocument` (Cloudinary raw asset metadata) · `KnowledgeChunk` (`halfvec(2048)`, HNSW cosine) · `RagConversation` · `RagMessage` · `OutboxEvent`. Chat và embedding đều gọi OpenRouter; embedding model canonical là `nvidia/llama-nemotron-embed-vl-1b-v2:free`.

### 4.3 Cross-service logical FK

Tham chiếu `db-schema/_global/cross-service-references.md` cho danh sách đầy đủ. Enforcement pattern bắt buộc:

| Pattern | Khi dùng |
|---|---|
| **HTTP validate at WRITE** | Tạo entity có logical FK đến service khác — gọi `GET /internal/v1/<resource>/{id}` với Internal JWT, retry qua Polly. Hỏng → return `VALIDATION_ERROR` 422. |
| **Snapshot field** | Read-heavy data cần render UI mà không cross-service call. Set tại CREATE, **immutable** (operator edit nguồn KHÔNG update snapshot). Ví dụ: `Booking.tripSnapshotOriginName`. |
| **Mutable event projection beside snapshot** | Khi UI/business cần trạng thái hiện tại, thêm cột projection riêng và tiến nó theo event CAS; không sửa snapshot. Day 23 dùng `Booking.trip_current_departure` bên cạnh immutable `trip_snapshot_departure`. |
| **Event consume** | Cascading state change. RabbitMQ at-least-once + Outbox đảm bảo publish. |
| **Tenant filter via Internal JWT** | Mọi query có `operator_id` phải `WHERE operator_id = :claim` từ Internal JWT. Enforce ở handler/middleware. |

### 4.4 DB conventions canonical (extract — full ở `db-schema/_global/README.md`)

- **Money:** `BIGINT` (VND, đơn vị đồng). Giữ đến đơn vị đồng — KHÔNG floor về 1,000 (v1.11.0); kết quả tính lẻ làm tròn đến đồng gần nhất. KHÔNG dùng DECIMAL/FLOAT.
- **Timestamps:** `TIMESTAMPTZ` (UTC instant). Persistence, internal HTTP, Redis/Outbox/RabbitMQ event chuẩn hóa UTC ISO-8601 kết thúc bằng `Z`; FE-facing JSON HTTP/WebSocket serialize cùng instant qua `Asia/Ho_Chi_Minh` với offset `+07:00`; KHÔNG `TIMESTAMP` naive.
- **`departureTime`:** `TIME` (no TZ), semantic calendar `Asia/Ho_Chi_Minh`; KHÔNG lưu `Asia/Ho_Chi_Minh` hoặc `+07:00` vào DB.
- **UUID:** `UUID DEFAULT gen_random_uuid()`.
- **JSON config:** `JSONB`.
- **pgvector embedding:** `halfvec(2048)` — chỉ trong `vietride_rag`; HNSW index dùng `halfvec_cosine_ops`.
- **Soft delete:** `deleted_at timestamptz` (canonical marker) cho Operator, User, Station, Stop, Route, Vehicle. Partial unique index `WHERE deleted_at IS NULL`. `is_active boolean` là **activation flag riêng biệt** (không phải soft-delete) cho Operator, Station, Stop, Route, Vehicle — `User` không có `is_active` (dùng `status` enum). Xem ADR 0003 + markers `ISoftDeletable`/`IActivatable`.
- **Audit columns:** `created_at TIMESTAMPTZ DEFAULT now()` + `updated_at TIMESTAMPTZ DEFAULT now()` + trigger `trg_set_updated_at` cho UPDATE.
- **Optimistic concurrency:** `row_version INT DEFAULT 0` cho `wallets`, `platform_wallets`, `operator_wallets`, `operator_trip_settlements`.
- **Index baseline:** PK auto · mọi FK có index · enum status xuất hiện trong WHERE business flow có index (partial nếu cần) · timestamp có range query có index.
- **Future-dated Trip fares:** Trip DB enables PostgreSQL `btree_gist`. `route_stop_fare_templates`
  has a GiST exclusion guard on equality of `(route_id, stop_id)` plus overlap of the half-open
  range `tstzrange(effective_from, coalesce(effective_until, 'infinity'), '[)')`; app validation is
  UX only, while this database guard is the concurrency boundary. `trip_stop_fares.source` is
  exactly `TEMPLATE_SNAPSHOT|MANUAL_OVERRIDE`, with existing rows backfilled to
  `TEMPLATE_SNAPSHOT`. Day 22 creates no new `TEMPLATE_SNAPSHOT` rows; legacy rows remain readable
  only for the omitted-`pricingAt` path and are non-authoritative for explicit `pricingAt`. Only an
  explicit operator per-Trip fare override creates `MANUAL_OVERRIDE`.

### 4.5 Hangfire schema isolation

Mỗi .NET service tự tạo schema `hangfire` **trong cùng DB của service đó** (KHÔNG dùng DB share):

| Service | Hangfire schema |
|---|---|
| Identity | `vietride_identity.hangfire` |
| Trip-Route-Vehicle | `vietride_trip.hangfire` |
| Booking | `vietride_booking.hangfire` |
| Parcel | `vietride_parcel.hangfire` |
| Payment & Wallet | `vietride_payment.hangfire` |

NestJS services KHÔNG dùng Hangfire — dùng **BullMQ** (Redis-backed). Xem Section 11.

---

## 5. API Conventions

### 5.1 URL prefix

| Prefix | Audience | Auth | Exposed via Gateway |
|---|---|---|---|
| `/v1/...` | Public client (mobile app, web) | User Access Token (RS256) | ✓ |
| `/internal/v1/...` | Service-to-service | Internal JWT (HS256, 120s) | ✗ — bị block ở Gateway. Mỗi service có middleware reject `/internal/*` request không có valid Internal JWT |
| `/health`, `/ready` | Healthcheck (Docker, Nginx, UptimeRobot) | None | `/health` + `/ready` per service path (Nginx route) |
| `/v1/.well-known/jwks.json` | Public (services fetch) | None | Identity Service only |
| `/v1/payments/vnpay-ipn`, `/v1/payments/vnpay-topup-ipn` | VNPay callback | HMAC-SHA512 signature | ✓ + Nginx IP whitelist |
| `GET /v1/payments/vnpay-return-status` | VNPay browser return status (read-only) | HMAC-SHA512 query + expected merchant | ✓ |

Versioning **bắt buộc** cho mọi public endpoint. Khi breaking change → bump `/v2/...`, giữ `/v1/...` deprecated tối thiểu 1 quarter.

### 5.2 Naming

- REST resources: plural noun lowercase — `/bookings`, `/trips`, `/parcels`, `/wallet/transactions`
- Action endpoints: verb-noun hyphen — `POST /bookings/{id}/cancel`, `POST /trips/{id}/lock-seats`
- Query params: camelCase — `?passengerUserId=xxx&from=2026-01-01`
- JSON body fields: camelCase — `{ "tripId": "...", "totalAmount": 350000 }`
- DB columns: snake_case (auto-map qua EF Core / Prisma naming strategy)

### 5.3 Request headers

| Header | When required | Format |
|---|---|---|
| `Authorization: Bearer <token>` | Mọi public protected endpoint | User Access Token (JWT RS256) |
| `X-Internal-Auth: Bearer <token>` | Mọi internal endpoint | Internal JWT (HS256, 120s TTL) |
| `Idempotency-Key: <uuid>` | Các mutation endpoints quan trọng (xem 5.6) | UUID v4 |
| `X-Request-Id: <uuid>` | Optional client-supplied, fallback Gateway generate | UUID v4 — propagated qua tất cả services + log |
| `Accept-Language` | Optional | `vi`, `en` (v1 chỉ phục vụ message kèm tài liệu i18n nội bộ — error code SCREAMING_SNAKE_CASE độc lập ngôn ngữ) |
| `Content-Type: application/json` | Request body có data | |

### 5.4 Response shape — success (ADR 0004 — ApiResponse envelope)

> **Effective 2026-06-01 (ADR 0004, accepted + rolled out Day 3).** Mọi FE-facing HTTP response dùng envelope `ApiResponse<T>` thống nhất — cả .NET (`VietRide.Shared.Web`) lẫn NestJS (`nest-common`). Controller trả `Ok(dto)` / `StatusCode(201, dto)` bình thường; filter/interceptor tự wrap. **Exception:** successful service-to-service `/internal/v1/*` (or `/internal/*`) responses return the raw DTO/list, not `ApiResponse<T>`; internal errors still use the standardized §5.5 error envelope.

**Envelope success (single resource):**

```jsonc
{
  "success": true,
  "statusCode": 200,                          // mirrors HTTP status line — xem §5.5 Rule 2
  "message": "Đăng ký thành công",            // optional — FE toast/UX; bỏ khi không cần
  "data": { /* DTO camelCase */ },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T17:00:00+07:00" }
}
```

**Envelope success (list — `data` là `PagedResult<T>`, xem §5.7):**

```jsonc
{
  "success": true,
  "statusCode": 200,
  "data": {
    "items": [ /* ... */ ],
    "page": 1,
    "pageSize": 20,
    "totalItems": 57,
    "totalPages": 3,
    "hasNextPage": true,
    "hasPreviousPage": false
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

- **Created:** HTTP 201 + `data` chứa resource vừa tạo, bọc trong envelope.
- **No content:** HTTP 204 — **empty body** (không envelope, xem ADR 0004 Rule 2).
- **Internal success:** `/internal/v1/*` / `/internal/*` trả raw DTO/list để giữ contract service-to-service đơn giản; không bọc `ApiResponse<T>` trên success.
- **Money:** number trong JSON (BIGINT VND — JS safe < 2^53). Server-side luôn lưu BIGINT.
- **Datetime instant:** FE-facing `/v1/*` JSON HTTP response và Tracking WebSocket dùng RFC 3339 qua IANA `Asia/Ho_Chi_Minh`, ví dụ `"2026-05-25T14:30:00+07:00"`. Internal HTTP/event/persistence dùng instant tương ứng `"2026-05-25T07:30:00Z"`. Request datetime bắt buộc có `Z` hoặc offset rõ ràng và được normalize UTC; thiếu offset trả `422 VALIDATION_ERROR`.
- **Calendar field:** `date`/`from`/`to` dạng `YYYY-MM-DD`, `TimeOnly`, `dayOfWeek` và recurring schedule dùng `Asia/Ho_Chi_Minh`.
- **UUID:** string lowercase với dấu gạch ngang chuẩn.
- **`meta.traceId`:** lấy từ header `X-Request-Id` do Gateway stamp (ADR 0002). `meta.timestamp` dùng `Asia/Ho_Chi_Minh +07:00` trên public `/v1/*`; internal error envelope giữ UTC `Z`.

### 5.5 Response shape — error (ADR 0004 — ApiResponse error envelope)

> **`application/problem+json` (RFC 7807) bị DROP kể từ ADR 0004 (2026-06-01).** Error đi qua cùng một envelope `ApiResponse` với `success: false`. HTTP status line vẫn là source-of-truth (4xx/5xx).

**Envelope error:**

```jsonc
// HTTP status: 4xx hoặc 5xx (đặt đúng trên status line — ADR 0004 Rule 2)
{
  "success": false,
  "statusCode": 400,                          // mirrors HTTP status line
  "error": {
    "code": "AUTH_OTP_INVALID",               // §5.9 registry code (UPPER_SNAKE_CASE)
    "message": "Mã xác thực không đúng.",     // FE có thể dùng hoặc map từ code
    "fields": [                               // chỉ với validation errors (422)
      { "field": "code", "message": "..." }
    ]
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

- `error.code` là **canonical UPPER_SNAKE_CASE** từ §5.9 registry — FE map UI message từ key này (thay thế `errorCode` của RFC 7807 cũ).
- `error.fields[]` thay thế RFC 7807 `errors[]` — chỉ xuất hiện với validation errors (422), bao gồm FluentValidation và model-binding failures (malformed JSON, missing non-nullable body field, type mismatch).
- `error.message` có thể là tiếng Việt user-facing; FE thường thay bằng UI string từ `error.code`.
- **KHÔNG dùng** `application/problem+json`, `type` URL, `title`, `instance`, `detail` (RFC 7807 fields) — đã loại bỏ.
- **KHÔNG return** `200 OK` với `success: false` — HTTP status line luôn phản ánh lỗi thật (ADR 0004 Rule 2).
- **Internal errors:** `/internal/v1/*` / `/internal/*` cũng dùng envelope lỗi chuẩn này; chỉ success payload của internal endpoints là raw.

### 5.6 Idempotency

Mọi HTTP action dùng `POST`, `PATCH`, `PUT` hoặc `DELETE` phải yêu cầu
`Idempotency-Key: <uuid-v4>` theo idempotency v2 bên dưới, không phụ thuộc public/internal hay
endpoint có behavior-idempotent hay không, trừ đúng 17 action có metadata exemption được khóa ở
bảng sau. Inventory executable phải giữ tổng `190 mutation surfaces / 173 required / 17 exempt`;
thêm hoặc xóa action bắt buộc cập nhật contract, runtime metadata và inventory trong cùng patch.

**Canonical 17 exemptions (không yêu cầu `Idempotency-Key`):**

| # | Endpoint | Lý do |
|---:|---|---|
| 1 | `POST /v1/auth/login` | Trả credentials; native authentication lockout bảo vệ request. |
| 2 | `POST /v1/auth/google` | Trả credentials; provider-token validation bảo vệ request. |
| 3 | `POST /v1/auth/refresh` | Refresh-token family replay detection sở hữu dedupe/rotation. |
| 4 | `POST /v1/firebase/custom-token` | Trả credentials, không được cache response trong Redis. |
| 5 | `POST /internal/v1/operators/summaries/batch` | Read-only bounded query. |
| 6 | `POST /internal/v1/trips/summaries/batch` | Read-only bounded query. |
| 7 | `POST /internal/v1/operators/vehicle-counts/batch` | Read-only bounded query. |
| 8 | `POST /v1/operator/trips/{tripId}/cancel/preview` | Read-only cancellation preview. |
| 9 | `POST /v1/operator/driver-schedules` | Day-9 create contract; no key, business conflict rules prevent duplicate active schedules. |
| 10 | `PATCH /v1/operator/driver-schedules/{id}/activate` | Contractually behavior-idempotent activation. |
| 11 | `POST /internal/v1/vouchers/validate` | Read-only validation; không tạo usage. |
| 12 | `POST /internal/v1/payments/redirect-sessions/lookup` | Read-only redirect lookup. |
| 13 | `POST /v1/payments/vnpay-ipn` | Provider HMAC + transaction reference dedupe. |
| 14 | `POST /v1/payments/vnpay-topup-ipn` | Provider HMAC + transaction reference dedupe. |
| 15 | `POST /v1/payments/subscription-vnpay-ipn` | Provider HMAC + transaction reference dedupe. |
| 16 | `POST /v1/assistant/trips/{tripId}/parcels/qr-scan` | Read-only QR resolution. |
| 17 | `POST /v1/admin/rag-config/reload` | Chỉ invalidates in-memory cache và naturally repeatable. |

Các mutation endpoints tiêu biểu sau yêu cầu header (inventory executable là nguồn exhaustive):

| # | Endpoint | Service |
|---|---|---|
| 1 | `POST /v1/bookings` | Booking |
| 2 | `POST /v1/bookings/round-trip` | Booking |
| 3 | `POST /v1/bookings/{id}/cancel` | Booking |
| 4 | `POST /v1/bookings/{id}/edit-pickup` | Booking |
| 4b | `POST /v1/bookings/{id}/edit-dropoff` | Booking |
| 5 | `POST /v1/parcels` | Parcel |
| 5a | `POST /v1/parcels/delivery/confirm` | Parcel |
| 5b | `POST /v1/parcels/delivery/reject` | Parcel |
| 5c | `POST /v1/parcels/delivery/undo-reject` | Parcel |
| 5d | `POST /v1/operator/parcels/{id}/resend-delivery-email` | Parcel |
| 5e | `POST /v1/operator/parcels/{id}/manual-confirm` | Parcel |
| 5f | `POST /v1/crew/parcels/{id}/resend-delivery-email` | Parcel |
| 5g | `POST /v1/crew/parcels/{id}/manual-confirm` | Parcel |
| 5h | `POST /v1/operator/parcels/{id}/cancel` | Parcel |
| 5i | `POST /v1/operator/parcels/{id}/request-transfer` | Parcel |
| 5j | `POST /v1/operator/parcels/{id}/return` | Parcel |
| 5k | `POST /v1/crew/parcels/{id}/confirm-transfer` | Parcel |
| 5l | `POST /v1/assistant/parcels/{id}/confirm-delivery` · `POST /v1/operator/parcels/{id}/confirm-delivery` (retained manual-confirm aliases) | Parcel |
| 6 | `POST /internal/v1/trips/{sourceTripId}/cargo/transfer` | Trip |
| 7 | `POST /v1/payments/wallet-charge` | Payment |
| 7b | `POST /internal/v1/payments/batch-charge` | Payment |
| 8 | `POST /v1/payments/vnpay-init` | Payment |
| 9 | `POST /v1/wallet/top-up/init` | Payment |
| 10 | `POST /v1/admin/trip-settlements/{id}/settle` | Payment |
| 11 | `POST /v1/operator/subscription/upgrade` | Identity (+Payment) |
| 11b | `POST /v1/operator/subscription/upgrade/{upgradeAttemptId}/retry-payment` | Identity (+Payment) |
| 12 | `POST /v1/operator/voucher-consents/{id}/accept` | Booking |
| 13 | `POST /v1/operator/voucher-consents/{id}/reject` | Booking |
| 14 | `POST /v1/admin/vouchers` | Booking |
| 15 | `POST /v1/operator/vouchers` | Booking |
| 16 | `POST /v1/driver/trips/{tripId}/start` | Trip |
| 17 | `POST /v1/driver/trips/{tripId}/complete` | Trip |
| 18 | `PATCH /v1/operator/trips/{tripId}` | Trip |
| 19 | `PATCH /v1/operator/driver-schedules/{scheduleId}?applyTo=...` | Trip |
| 20 | `PATCH /v1/operator/driver-schedules/{scheduleId}/crew` (one-release deprecated alias) | Trip |
| 21 | `POST /v1/driver/trips/{tripId}/incident` | Trip |
| 22 | `POST /v1/driver/trips/{tripId}/stops/{stopId}/arrive` | Trip |
| 23 | `POST /v1/driver/trips/{tripId}/destination/arrive` | Trip |
| 24 | `POST /v1/assistant/parcels/{parcelId}/unload` | Parcel |
| 25 | `POST /v1/assistant/parcels/{parcelId}/deliver` | Parcel |
| 26 | `POST /v1/admin/users/{userId}/lock` | Identity |
| 27 | `POST /v1/admin/users/{userId}/unlock` | Identity |
| 28 | `POST /v1/admin/stations/{primaryStationId}/merge` | Trip |
| 29 | `DELETE /v1/operator/stops/{id}?replacedByStopId=` | Trip |
| 30 | `POST /v1/bookings/{bookingId}/pending-action/{actionId}/accept-fallback` | Booking |
| 31 | `POST /v1/driver/trips/{tripId}/stops/{stopId}/depart` | Trip |
| 32 | `POST /v1/operator/trips/{tripId}/substitute-vehicle` | Trip |
| 33 | `POST /v1/bookings/trips/{newTripId}/transfers/passengers/{passengerId}/confirm` | Booking |
| 34 | `POST /v1/admin/policies` | RAG |
| 35 | `PATCH /v1/admin/policies/{policyId}` | RAG |
| 36 | `DELETE /v1/admin/policies/{policyId}` | RAG |
| 37 | `POST /v1/operator/policies` | RAG |
| 38 | `PATCH /v1/operator/policies/{policyId}` | RAG |
| 39 | `DELETE /v1/operator/policies/{policyId}` | RAG |
| 40 | `PUT /v1/operator/parcel-route-fares/{routeId}/batch` | Parcel |
| 41 | `POST /v1/operator/trips/{tripId}/disrupt-no-substitution` | Trip |
| 42 | `POST /v1/driver/trips/{tripId}/route-change-proposals` | Trip |
| 43 | `POST /v1/operator/route-change-proposals/{proposalId}/approve` | Trip |
| 44 | `POST /v1/operator/route-change-proposals/{proposalId}/reject` | Trip |
| 45 | `POST /v1/operator/trips/{tripId}/seats/{seatNumber}/disable` | Trip |
| 46 | `POST /v1/operator/trips/{tripId}/seats/{seatNumber}/enable` | Trip |

`POST /v1/operator/trips` is deferred outside current v1 and MUST remain absent from the public
API and Gateway inventories. `Trip.source=MANUAL` is compatibility/readiness only; it does not
authorize a controller, DTO, route, or quota writer.

Day-24 mutations use the same v2 fingerprint/replay contract: UUID-v4 key, actor/method/path/
canonical-query/raw-body fingerprint, byte-identical replay before current-state lookup, and
`422 IDEMPOTENCY_KEY_MISMATCH` for reuse with a different fingerprint. The retained
`PATCH /v1/operator/stops/{id}` is details-update-only and never disables a Stop; DELETE is the
sole disable route. A same-replacement DELETE with a new key is behavior-idempotent (`200`), while
a different replacement after disable is `409 STOP_ALREADY_DISABLED`.

**Implementation:**

- Response key: `<service>:idem:v2:response:{SHA256(idempotencyKey)}`, TTL 24 giờ. Processing key:
  `<service>:idem:v2:processing:{SHA256(idempotencyKey)}`, TTL 120 giây. Không lưu raw key trong
  namespace v2.
- Fingerprint là SHA-256 của frame nhị phân length-prefix theo thứ tự: authenticated `sub`, method
  uppercase, `PathBase + Path`, canonical query và raw body bytes. Canonical query flatten toàn bộ
  key/value (kể cả duplicate), sort ordinal theo key rồi value. JSON khác whitespace hoặc property
  order là request khác; role không thuộc fingerprint.
- Request đầu tiên reserve processing key bằng Redis `SET NX EX` với owner token ngẫu nhiên rồi mới
  chạy handler. Request cùng key và fingerprint khi lock còn tồn tại trả
  `409 IDEMPOTENCY_REQUEST_PENDING`; khác fingerprint trả `422 IDEMPOTENCY_KEY_MISMATCH`. Nếu response
  vừa hoàn tất trong race window thì replay thay vì trả pending.
- Response `<500` lưu nguyên status, `Content-Type` và response bytes trong 24 giờ; replay không chạy
  downstream. Exception hoặc response `5xx` không cache và phải owner-safe release processing lock.
  Complete/release chỉ thành công khi owner token vẫn khớp, vì vậy stale request không được xóa hoặc
  ghi đè lock/response của request mới.
- Middleware áp dụng cho `POST/PATCH/PUT/DELETE` khi có header. Endpoint đánh dấu bắt buộc phải dùng
  UUID v4; thiếu header trả exact `422 IDEMPOTENCY_KEY_REQUIRED`, malformed UUID trả
  `422 VALIDATION_ERROR`.
- Legacy cache `<service>:idem:{key}` chỉ chứa body hash nên không đủ an toàn để replay cross-path.
  Trong thời gian rollout, nếu legacy key còn tồn tại thì fail closed bằng
  `422 IDEMPOTENCY_KEY_MISMATCH`; không flush Redis business keys, để legacy entry tự hết hạn tối đa
  sau 24 giờ.
- Ba mutation Day 40 dùng trực tiếp shared
  `VietRide.Shared.Web.Idempotency.RequireIdempotencyAttribute`; hai endpoint lock/unlock không có
  body đặt `AllowRequestBody=false`. Không dùng controller-local header check hoặc Trip legacy
  filter. `POST /internal/v1/operators/summaries/batch` là read-only POST, Internal-JWT-only và là
  ngoại lệ rõ ràng không yêu cầu `Idempotency-Key`.

**Day-22 query-aware baseline:** Trip/DriverSchedule mutations include every query key/value in the
fingerprint: keys sort ordinally, absent differs from empty, and repeated-value order is preserved.
Day-39 idempotency v2 supersedes the original Day-22 route/body framing with `PathBase + Path` and
raw request-body bytes while preserving those query semantics. A different `applyTo`, subject,
path (including `/crew`), query value/order, or body returns `422 IDEMPOTENCY_KEY_MISMATCH`.

The feasible pipeline preserves the current shared middleware: authentication (`401`) → endpoint
role authorization (`403`) → shared pre-reservation checks (matched route, UUID-v4 key, body
presence policy) → fingerprint reservation/replay/mismatch/pending → MVC binding plus endpoint
query/body validation (`422`) → one handler-start `now` for a new reservation → domain
preflight → one local commit → store the exact status/body for every reserved response below
`500`. Reserved malformed JSON, missing/invalid `applyTo`, unknown/empty bodies, and
FluentValidation `422` responses replay exactly. Pre-reservation authentication/authorization,
unmatched-route, and malformed-key failures are not cached; a `5xx` releases the reservation.
No MVC-before-middleware mechanism is introduced or claimed. Day-21 no-body lifecycle semantics
remain unchanged.

**Day-22 Trip PATCH domain contract:** `PATCH /v1/operator/trips/{tripId}` is
`OPERATOR_ADMIN`, UUID-v4 idempotent, and accepts only `{baseFare?,notes?,vehicleId?,routeId?}`.
Omitted means unchanged; `notes:null` clears after trim/blank normalization; null for other fields,
empty/unknown-only body, departure, and crew are `422 VALIDATION_ERROR`. Actual-change lifecycle
is `baseFare|routeId` only `SCHEDULED`, `vehicleId` in `SCHEDULED|BOARDING`, and `notes` in every
non-terminal status. Execution order is tenant-scoped Trip load/masked `TRIP_NOT_FOUND` →
normalize/actual `changedFields` → no-op `200` → lifecycle → tenant Route/Vehicle references →
one Booking edit-impact call for route/vehicle → conflicts in exact order
`TRIP_ROUTE_CHANGE_BOOKINGS_EXIST`, `TRIP_VEHICLE_SWAP_HELD_SEAT_CONFLICT`,
`TRIP_VEHICLE_SWAP_TOO_LATE`, then remaining local conflicts → open one transaction →
lock/reload/revalidate in fixed aggregate order Trip → seats → stops with stable collection order
→ mutate → audit/Outbox → one save/commit. No transaction
spans HTTP and no-op produces no audit/event/downstream call.

Compatibility is keyed by normalized seat number and only for vehicle swaps:
`STANDARD < SLEEPER_UPPER < SLEEPER_LOWER < VIP`; it never affects pricing and `DRIVER_AREA` is
not a passenger seat. Absent is `SEAT_REMOVED`; same-number disabled or `DRIVER_AREA` is
`SEAT_DISABLED`; lower rank is `SEAT_TYPE_DOWNGRADED`; equal/higher is compatible and preserves
HELD/BOOKED. In `SCHEDULED`, incompatible HELD blocks; incompatible BOOKED may create
`PENDING_SEAT_ASSIGNMENT` only when `min(now+4h, departure-30m) > now`. In `BOARDING`, any
incompatible HELD/BOOKED is too late. Disabled/`DRIVER_AREA` entries never create TripSeats.

**Manager Web seat and shuttle contract:** Trip stores `seat_layout_snapshot_json` as the
immutable per-Trip layout used by detail/seat-map projections. Vehicle template edits do not
rewrite an existing Trip snapshot; only approved vehicle substitution/swap flows may replace it.
`usablePassengerCapacity` is computed from non-disabled layout seats whose type is not
`DRIVER_AREA`; `Vehicle.totalSeats` remains for compatibility. The two seat mutations are
`OPERATOR_ADMIN`-only, UUID-v4 idempotent, tenant-scoped endpoints:
`POST /v1/operator/trips/{tripId}/seats/{seatNumber}/disable` and `/enable`. They lock the
TripSeat with `SELECT ... FOR UPDATE`, audit `TRIP_SEAT_DISABLED`/`TRIP_SEAT_ENABLED` in the
same transaction, return the latest seat-map, and emit no integration event. Disable requires a
reason and only permits `AVAILABLE -> UNAVAILABLE`; enable only permits `UNAVAILABLE -> AVAILABLE`
and clears the reason. `HELD`/`BOOKED` disable returns `409 TRIP_SEAT_IN_USE`.

`GET /v1/operator/shuttle-trips` is tenant-scoped for `OPERATOR_ADMIN`/`OPERATOR_STAFF`.
Asia/Ho_Chi_Minh `from/to` filters are inclusive, absent status filter means all statuses including
`CANCELLED`, and ordering is scheduled departure descending then ShuttleTrip UUID descending.
Passenger/stop counts are SQL projections and driver profiles are fetched in one batched Identity
call. Pending shuttle groups keep `mainTripId + direction` grouping and add nested passenger
profiles with aggregated ticket IDs; missing profiles expose null display fields and Identity
transport failure maps to `503 UPSTREAM_UNAVAILABLE`.

**Day-22 DriverSchedule PATCH domain contract:** canonical body is exactly
`{departureTime?,dayOfWeek?,driverUserId?,assistantUserId?,vehicleId?,validUntil?,isActive?}`;
`routeId` and `validFrom` are immutable. Omitted means unchanged. Explicit null clears only
assistant, vehicle, or validUntil; `validUntil:null` is open-ended. Other nulls and empty/
unknown-only bodies are `VALIDATION_ERROR`. The order is tenant schedule load/masked `404` →
normalize/no-op → local/window/null-vehicle rules → tenant/Identity references → overlap checks →
branch. `FUTURE_ONLY` leaves generated Trips unchanged, makes no Booking call, and generates only
uncovered future dates.
With `vehicleId:null`, it clears only the schedule and logs every attempted date via existing
`TripGenerationSkipLog` reason `OTHER` with a no-vehicle message until reassigned.
`ALL_PENDING` with null vehicle is `422` before any Booking call/write because Trip vehicle is
NOT NULL. Otherwise it deterministically enumerates `SCHEDULED|BOARDING`, fetches all Booking
projections before any write/transaction, blocks the entire request with
`DRIVER_SCHEDULE_EDIT_TOO_LATE` when a CONFIRMED Booking's Trip has `departure-now < 2h`, then
uses HELD-conflict before too-late precedence (no route-change code). One transaction locks in
fixed order schedule → Trips sorted `(departureDateTime,tripId)` → each Trip's seats → stops,
reloads/revalidates, applies the schedule and all cascades, stages
audit/Outbox, and saves/commits once; any failure rolls back all. Day removal cancels no-longer-
matching pending Trips. `validUntil` shortening and `isActive=false` only stop generation and never
mutate generated Trips; clear/reactivate may generate uncovered future dates. `/crew` is a
one-release deprecated alias to this command with `ALL_PENDING`, not a second use case.
Changing `departureTime`/`dayOfWeek` through `ALL_PENDING` is the only Day-22 path that cascades
`departureDateTime`; that field is absent from the Trip PATCH body and changed-field registry.
`trip_stops.estimated_arrival_time` is a static planned baseline: an approved pre-departure Route
edit or DriverSchedule `ALL_PENDING` cascade may recompute it, while GPS/Tracking dynamic ETA never
updates the column. Trip Service plans ordered origin → RouteStops → destination with Google Routes
`DRIVE`/`TRAFFIC_AWARE` at `departureTime`, adding `TRIP_STOP_DWELL_MINUTES` (default 20) after
each intermediate stop. Missing coordinates, timeout/quota, malformed legs, or an open circuit use
cumulative Route metrics without failing Trip creation. `trips.planned_eta_source` stores
`GOOGLE_ROUTES|ROUTE_BASELINE`; public projections expose only
`plannedEtaQuality=TRAFFIC_AWARE|FALLBACK`.

### 5.7 Pagination — `PagedResult<T>` + `QueryOptions` (ADR 0004)

> **`?sort=-field` convention cũ bị SUPERSEDE bởi `sortBy`+`sortDir` kể từ ADR 0004 (2026-06-01).** Xem §5.8.

**`PagedResult<T>` — response shape cho list (7 fields):**

| Field | Type | Mô tả |
|---|---|---|
| `items` | `T[]` | Danh sách items trang hiện tại |
| `page` | `int` | Trang hiện tại (1-based) |
| `pageSize` | `int` | Số items per trang |
| `totalItems` | `int` | Tổng số items |
| `totalPages` | `int` | = `ceil(totalItems / pageSize)` |
| `hasNextPage` | `bool` | `page < totalPages` |
| `hasPreviousPage` | `bool` | `page > 1` |

**`QueryOptions` — request query string (list/collection endpoints):**

| Parameter | Default | Constraint | Mô tả |
|---|---|---|---|
| `page` | `1` | `>= 1` | Trang cần lấy |
| `pageSize` | `20` | `1..100` (clamped max 100) | Số items per trang |
| `search` | `null` | optional | Full-text / partial search string |
| `searchIn` | `null` | whitelist per aggregate | Comma-separated fields to search (`email,phone`) |
| `sortBy` | aggregate default | whitelist per aggregate | Field to sort — **bắt buộc whitelisted** (xem §5.8) |
| `sortDir` | `desc` | `asc` hoặc `desc` | Chiều sắp xếp |
| `includeDeleted` | `false` | admin/privileged only | Bao gồm soft-deleted records (ADR 0003) |

- `pageSize` clamped về max 100 server-side — client không thể vượt.
- KHÔNG dùng cursor-based pagination ở v1.

### 5.8 Filter conventions + Sort (ADR 0004)

> **`?sort=-field` convention cũ đã bị SUPERSEDE.** Sort dùng `sortBy` + `sortDir` trong `QueryOptions` (§5.7).

- **Date range:** `from=<ISO8601>&to=<ISO8601>` (inclusive).
- **Status filter:** `status=<enum>` hoặc `status=A,B,C` (csv multi-value).
- **Search:** `search=<text>` + `searchIn=field1,field2` (thay thế `q=<text>` cũ; BE whitelist các field được phép search per aggregate).
- **Sort:** `sortBy=<field>&sortDir=asc|desc` — **`sortBy` PHẢI nằm trong whitelist của aggregate** (security requirement — ngăn arbitrary-column sort/search → injection / info-leak). Query handler/repository reject bất kỳ field nào không trong allow-list với `400 INVALID_SORT_FIELD`. Default `sortDir=desc`, default `sortBy` do aggregate quyết định (thường `createdAt`).
- **Soft-delete:** `includeDeleted=true` — chỉ cho phép admin/privileged endpoint (ADR 0003).

### 5.9 Canonical Error Code Registry

> Phải sync với technical_context_v7 Section 8 "Code & API Conventions". Khi thêm code mới → bump MINOR doc version + thêm vào registry này.

| Group | Code | HTTP | Khi dùng |
|---|---|---|---|
| **Auth** | `AUTH_INVALID_CREDENTIALS` | 401 | Email/password sai |
| | `AUTH_TOKEN_EXPIRED` | 401 | Access token expired |
| | `AUTH_TOKEN_INVALID` | 401 | Signature/format invalid |
| | `AUTH_GOOGLE_TOKEN_INVALID` | 401 | Google ID token signature/expiry/audience invalid |
| | `AUTH_EMAIL_NOT_VERIFIED` | 403 | Non-passenger User.status = PENDING_EMAIL_VERIFICATION |
| | `AUTH_ACCOUNT_LOCKED` | 403 | User.status = LOCKED |
| | `AUTH_OTP_INVALID` | 400 | OTP code sai |
| | `AUTH_OTP_EXPIRED` | 400 | OTP TTL 5 phút hết |
| | `AUTH_OTP_RATE_LIMIT_EXCEEDED` | 429 | OTP request rate limit (Redis `identity:otp_rate:{email}` max 3/h) exceeded |
| | `AUTH_EMAIL_ALREADY_REGISTERED` | 409 | Register email trùng |
| | `AUTH_PHONE_ALREADY_REGISTERED` | 409 | Phone trùng User khác |
| | `AUTH_PHONE_REQUIRED` | 403 | Gateway block: User.phone NULL + role=PASSENGER |
| | `AUTH_PHONE_INVALID_FORMAT` | 400 | Không match `^\+84[0-9]{9,10}$` |
| | `AUTH_INITIAL_PASSWORD_TOKEN_INVALID` | 400 | SET_INITIAL_PASSWORD token sai |
| | `AUTH_INITIAL_PASSWORD_TOKEN_EXPIRED` | 400 | Token quá 48h |
| | `AUTH_PENDING_INITIAL_PASSWORD` | 403 | User.status = PENDING_INITIAL_PASSWORD, không login được |
| **User** | `USER_INVALID_STATUS_TRANSITION` | 422 | Invalid User status transition (domain guard) |
| **Booking** | `BOOKING_SEAT_UNAVAILABLE` | 409 | Ghế đã BOOKED/HELD/UNAVAILABLE |
| | `BOOKING_TRIP_NOT_BOOKABLE` | 409 | Trip status ≠ SCHEDULED hoặc đã đóng |
| | `BOOKING_CUTOFF_EXCEEDED` | 409 | Edit/cancel sau cutoff 2h |
| | `BOOKING_MAX_SEATS_EXCEEDED` | 422 | Booking > 5 seats |
| | `BOOKING_NOT_FOUND` | 404 | Booking missing; Day-23 pending-action resolve also uses this for not-owned Booking and masks a discovered Booking/action ownership mismatch before action state is revealed |
| | `BOOKING_PENDING_ACTION_NOT_FOUND` | 404 | Owner-authorized Booking exists but the action id does not exist under that Booking |
| | `BOOKING_PENDING_ACTION_NOT_RESOLVABLE` | 409 | Active action reason/state or Booking state does not support the Day-23 `SCHEDULE_CHANGE` resolution |
| | `BOOKING_PENDING_ACTION_SUPERSEDED` | 409 | New idempotency key targets an action terminally resolved as `SUPERSEDED` |
| | `BOOKING_PENDING_ACTION_ALREADY_RESOLVED` | 409 | New idempotency key targets an action resolved as `ACCEPTED` or `REJECTED` |
| | `BOOKING_PENDING_ACTION_EXPIRED` | 409 | Passenger request is strictly after the effective cutoff; equality remains eligible, timeout owns the outcome, and the scheduled resolver only auto-accepts — it never cancels or refunds |
| | `BOOKING_NOT_CANCELLABLE` | 409 | Status không trong CONFIRMED/PENDING_PAYMENT |
| | `BOOKING_EDIT_PICKUP_PRICE_CHANGED` | 409 | Edit pickup làm THAY ĐỔI giá vé (tăng hoặc giảm) — v1 chỉ cho đổi cùng giá (fareDelta=0); muốn đổi giá: hủy vé + đặt lại (v1.11.0, thay BOOKING_EDIT_PICKUP_PRICE_INCREASE) |
| | `BOOKING_NOT_FOR_THIS_TRIP` | 422 | QR scan booking hoặc boarding-tick passenger khác trip |
| | `BOOKING_PASSENGER_ALREADY_BOARDED` | 409 | Tick lại passenger đã BOARDED |
| | `BOOKING_ROUND_TRIP_INVALID` | 422 | Return trip không hợp lệ |
| | `BOOKING_TRANSFER_NOT_FOUND` | 404 | Missing/inactive BookingTransfer for the Passenger and replacement Trip |
| | `BOOKING_TRANSFER_SEAT_PENDING` | 409 | Physical confirmation requested while replacement `newSeatNumber` is null |
| **Shuttle** | `SHUTTLE_STATION_NOT_SUPPORTED` | 422 | Shuttle intent không dùng origin Station hỗ trợ shuttle hoặc Station thiếu tọa độ |
| | `SHUTTLE_REQUEST_CUTOFF_PASSED` | 409 | Tạo shuttle intent/dispatch tại hoặc sau hard cutoff T-30 |
| | `SHUTTLE_PICKUP_LOCKED` | 409 | Edit pickup khi Booking còn shuttle intent active |
| | `SHUTTLE_REQUEST_SET_CHANGED` | 409 | Booking subset đã đổi trạng thái trong lúc operator dispatch |
| | `SHUTTLE_CAPACITY_EXCEEDED` | 409 | Tổng ticket của subset vượt sức chứa vehicle |
| | `SHUTTLE_DISTANCE_EXCEEDED` | 422 | Road distance snapshot lớn hơn 5.000 mét; đúng 5.000 mét vẫn hợp lệ |
| | `SHUTTLE_DISTANCE_UNAVAILABLE` | 503 | Google Routes thiếu key, timeout, upstream error hoặc response không hợp lệ |
| | `SHUTTLE_REQUEST_NOT_CANCELLABLE` | 409 | Request đã assign hoặc không còn ở trạng thái chưa assign |
| | `SHUTTLE_TRIP_INVALID_STATE` | 409 | ShuttleTrip không cho phép lifecycle transition được yêu cầu |
| | `SHUTTLE_PASSENGER_INVALID_STATE` | 409 | ShuttlePassenger không cho phép lifecycle transition được yêu cầu |
| | `SHUTTLE_PASSENGERS_INCOMPLETE` | 409 | Nhóm passenger shuttle chưa ở trạng thái phù hợp để hoàn tất hoặc huỷ thao tác |
| | `SHUTTLE_PASSENGER_NOT_FOUND` | 404 | Không tìm thấy passenger/service order trong ShuttleTrip |
| | `SHUTTLE_DRIVER_CONFLICT` | 409 | Driver overlap main Trip hoặc ShuttleTrip khác |
| | `SHUTTLE_VEHICLE_CONFLICT` | 409 | Vehicle overlap main Trip hoặc ShuttleTrip khác |
| | `DRIVER_NOT_FOUND` | 404 | Driver không active, không cùng operator hoặc thiếu snapshot liên hệ bắt buộc |
| | `SHUTTLE_TRIP_NOT_FOUND` | 404 | ShuttleTrip không tồn tại |
| **Voucher** | `VOUCHER_NOT_FOUND` | 404 | |
| | `VOUCHER_EXPIRED` | 422 | validUntil < now |
| | `VOUCHER_NOT_APPLICABLE` | 422 | Operator chưa consent (OPERATOR_FUNDED), hoặc route không match |
| | `VOUCHER_USAGE_LIMIT_REACHED` | 422 | Total usage limit reached |
| | `VOUCHER_USER_LIMIT_REACHED` | 422 | Per-user limit reached |
| | `VOUCHER_MIN_ORDER_NOT_MET` | 422 | Total < minOrderAmount |
| | `VOUCHER_FORBIDDEN_FUNDING` | 422 | Operator self-create voucher body truyền `fundingType` khác `OPERATOR_FUNDED` (operator-owned forced OPERATOR_FUNDED) |
| | `VOUCHER_CODE_CONFLICT` | 409 | Voucher `code` trùng voucher chưa soft-deleted (partial unique `WHERE deleted_at IS NULL`); áp cho admin + operator create |
| | `VOUCHER_LOCKED` | 409 | Operator PATCH economic field (`value`/`minOrderAmount`/`maxDiscountAmount`) trên voucher đã có >=1 `voucher_usages` (Q6 freeze-on-first-use) |
| | `CONSENT_NOT_PENDING` | 409 | consent không ở trạng thái PENDING khi accept (chỉ PENDING mới accept được — v7:665-672) |
| | `CONSENT_ALREADY_REJECTED` | 409 | consent đã REJECTED, không thể reject lại (reject precond PENDING\|ACCEPTED — v7:674-683) |
| **Payment** | `PAYMENT_INSUFFICIENT_WALLET` | 402 | Wallet balance < amount |
| | `PAYMENT_VNPAY_ERROR` | 502 | VNPay trả lỗi (không 00) |
| | `PAYMENT_TIMEOUT` | 408 | VNPay không callback trước effective deadline; legacy fallback 15 phút |
| | `PAYMENT_DEADLINE_PASSED` | 422 | Request tạo Payment có `dueAt <= now` |
| | `PAYMENT_ALREADY_PROCESSED` | 409 | Payment đã SUCCEEDED, callback duplicate |
| | `PAYMENT_SIGNATURE_INVALID` | 401 | VNPay HMAC verify fail |
| **Wallet** | `WALLET_INSUFFICIENT_BALANCE` | 402 | OperatorWallet/PassengerWallet không đủ |
| | `WALLET_TOP_UP_FAILED` | 502 | TopUp VNPay failed |
| | `WALLET_TOP_UP_AMOUNT_TOO_LOW` | 422 | < 10,000 VND |
| **Trip** | `TRIP_NOT_FOUND` | 404 | |
| | `TRIP_SEAT_NOT_FOUND` | 404 | TripSeat không tồn tại trong Trip; `DRIVER_AREA` không tạo TripSeat |
| | `TRIP_SEAT_IN_USE` | 409 | Không được disable seat đang `HELD` hoặc `BOOKED` |
| | `TRIP_SEAT_STATE_CONFLICT` | 409 | TripSeat không ở state hợp lệ cho transition disable/enable |
| | `FARE_SURCHARGE_PERIOD_NOT_FOUND` | 404 | Holiday surcharge period không tồn tại, đã soft-delete, hoặc không thuộc operator caller |
| | `FARE_SURCHARGE_PERIOD_OVERLAP` | 422 | Active holiday surcharge period overlap một active non-deleted period của cùng operator |
| | `TRIP_INVALID_TRANSITION` | 409 | Day-21 start/complete lifecycle precondition fails; do not introduce or use `INVALID_TRIP_STATUS` |
| | `TRIP_NOT_IN_PROGRESS` | 422 | Incident/arrival chỉ hợp lệ khi Trip đang `IN_PROGRESS` |
| | `TRIP_NOT_SUBSTITUTABLE` | 409 | Vehicle substitution requires an `IN_PROGRESS` old Trip |
| | `TRIP_STOP_NOT_FOUND` | 404 | TripStop không tồn tại trong Trip được chỉ định |
| | `TRIP_STOP_ALREADY_FINALIZED` | 409 | TripStop đã `ARRIVED` hoặc `SKIPPED` |
| | `TRIP_STOP_NOT_ARRIVED` | 422 | Day-24 departure requires TripStop.status = ARRIVED |
| | `TRIP_STOP_ALREADY_DEPARTED` | 409 | A new idempotency key targets a TripStop with actualDepartureTime already set |
| | `TRIP_DESTINATION_ALREADY_ARRIVED` | 409 | Destination-terminal anchor đã được ghi trước đó |
| | `VEHICLE_NOT_FOUND` | 404 | Vehicle không tồn tại, đã soft-delete, hoặc không thuộc operator caller |
| | `VEHICLE_NOT_ACTIVE` | 422 | Replacement Vehicle exists but is not active |
| | `VEHICLE_TYPE_NOT_FOUND` | 404 | VehicleType không tồn tại hoặc không active |
| | `TRIP_NOT_EDITABLE` | 409 | Requested Trip field is not editable in the current lifecycle state |
| | `ROUTE_CHANGE_PROPOSAL_NOT_FOUND` | 404 | Proposal không tồn tại hoặc bị tenant-mask cho `OPERATOR_ADMIN` khác operator |
| | `ROUTE_CHANGE_PROPOSAL_NOT_PENDING` | 409 | Approve/reject proposal đã rời `PENDING`, hoặc Trip không còn editable và pending proposal vừa được expire |
| | `ROUTE_CHANGE_PROPOSAL_STALE` | 409 | Frozen EXISTING source đã sửa/deactivate, hoặc CUSTOM destination/Stop không còn hợp lệ tại approval |
| | `INCIDENT_NOT_FOUND` | 404 | Optional proposal `incidentId` không tồn tại hoặc không thuộc cùng Trip |
| | `TRIP_ALREADY_TERMINAL` | 409 | Manual complete/fallback/disruption race already produced a terminal state |
| | `TRIP_VEHICLE_CONFLICT` | 409 | Vehicle trùng giờ trên Trip khác |
| | `TRIP_DRIVER_CONFLICT` | 409 | Driver trùng giờ |
| | `TRIP_ROUTE_CHANGE_BOOKINGS_EXIST` | 409 | Route edit has an active `PENDING_PAYMENT\|CONFIRMED` Booking impact |
| | `TRIP_VEHICLE_SWAP_HELD_SEAT_CONFLICT` | 409 | Vehicle swap would remove/disable/downgrade an HELD seat |
| | `TRIP_VEHICLE_SWAP_TOO_LATE` | 409 | Vehicle swap has incompatible BOOKED/BOARDING seats after the strict reassignment window |
| | `TRIP_NOT_ACCEPTING_PARCEL` | 409 | Trip IN_PROGRESS — không nhận parcel mới |
| | `PARCEL_CARGO_NOT_FOUND` | 404 | Source Trip không có active cargo ledger cho Parcel |
| | `TRIP_CARGO_TRANSFER_CONFLICT` | 409 | Atomic source→target cargo transfer mất race hoặc source/target invalid |
| | `TRIP_CARGO_CAPACITY_EXCEEDED` | 422 | Target Trip không đủ cargo capacity cho transfer |
| | `DRIVER_SCHEDULE_EDIT_TOO_LATE` | 409 | Edit schedule quá deadline |
| **Parcel** | `PARCEL_NOT_FOUND` | 404 | |
| | `INVALID_STATUS` | 409 | Unload/deliver không ở đúng source status hoặc CAS thua race |
| | `DROP_OFF_STOP_NOT_FOUND` | 422 | `dropoffStopId` không tồn tại trong Trip snapshot |
| | `DROP_OFF_STOP_NOT_ALLOWED` | 422 | TripStop khớp `dropoffStopId` không cho phép drop-off |
| | `DROP_OFF_STOP_NOT_ARRIVED` | 422 | TripStop khớp `dropoffStopId` chưa `ARRIVED` |
| | `DESTINATION_TERMINAL_NOT_ARRIVED` | 422 | Parcel terminal-bound chưa có destination arrival anchor |
| | `PARCEL_CAPACITY_EXCEEDED` | 409 | Vượt available cargo capacity |
| | `PARCEL_PRICING_NOT_CONFIGURED` | 422 | ParcelRouteFare chưa config |
| | `PARCEL_DELIVERY_TOKEN_INVALID` | 400 | Token parse/hash không match |
| | `PARCEL_DELIVERY_TOKEN_EXPIRED` | 400 | Token row quá 48h |
| | `PARCEL_DELIVERY_TOKEN_REVOKED` | 400 | Token row đã revoke do resend/terminal action |
| | `PARCEL_RECIPIENT_EMAIL_REQUIRED` | 422 | Resend được yêu cầu nhưng Parcel không có recipient email |
| | `PARCEL_NOT_PENDING_CONFIRM` | 400 | Public/manual delivery action không hợp lệ ở status hiện tại |
| | `INVALID_REFUND_CHOICE` | 422 | Parcel manual-cancel refund choice không thuộc `FULL|POLICY|NO` hoặc compatibility aliases |
| | `TRIP_SERVICE_UNAVAILABLE` | 503 | Parcel không thể hoàn tất cargo mutation vì Trip dependency unavailable |
| | `PARCEL_NOT_TRANSFERABLE` | 409 | Status sai khi confirm transfer |
| | `PARCEL_CARGO_RECOVERY_IN_PROGRESS` | 409 | Parcel already has a durable `TRANSFER|RETURN` cargo-recovery operation pending |
| | `PARCEL_ADDITIONAL_PAYMENT_REQUIRED` | 402 | Cân lại > ước lượng |
| | `PARCEL_REVIEW_TIMEOUT` | 409 | Timeout review 24h cho record legacy `PENDING_OPERATOR_REVIEW` |
| **Stop / Route** | `STOP_NOT_FOUND` | 404 | Day-7 Trip Stop handlers use coded 404 path; cross-tenant DELETE is masked here |
| | `STOP_REPLACEMENT_INVALID` | 422 | Replacement Stop missing, inactive, cross-operator, or self-reference |
| | `STOP_REPLACEMENT_CYCLE` | 422 | Replacement chain would create a cycle |
| | `STOP_ALREADY_DISABLED` | 409 | A different replacement is requested after the Stop has already been disabled |
| | `STOP_DISABLED_BOOKING_AFFECTED` | 200 warning (legacy/deprecated for DELETE) | Retained only for unrelated legacy warning usages; Day-24 DELETE returns `warning: null` and omits `ActiveBookingCount` |
| | `STOP_REPLACEMENT_CYCLE` | 422 | replacedByStopId tạo cycle |
| | `STOP_REPLACEMENT_DIFFERENT_OPERATOR` | 403 | Stop thay thế khác operator |
| | `STOP_NOT_PICKUP_ALLOWED` | 422 | RouteStop.allowPickup = false |
| | `STOP_NOT_DROPOFF_ALLOWED` | 422 | RouteStop.allowDropoff = false |
| | `ROUTE_NOT_FOUND` | 404 | |
| | `ROUTE_STOP_ORDER_CONFLICT` | 422 | Day-8 config-time RouteStop `orderIndex` conflict within the same Route |
| | `ROUTE_STOP_FLAGS_INVALID` | 422 | Day-8 config-time RouteStop `allowPickup=false` and `allowDropoff=false` |
| | `ROUTE_DUPLICATED` | 409 | A non-deleted Route with the same normalized name, origin Station, destination Station, and operator already exists; `error.fields.existingRouteId` identifies the oldest matching Route |
| | `ROUTE_STATION_INVALID` | 422 | A composite Route station/stop does not exist, is inactive, or is outside the operator tenant |
| | `ROUTE_STATION_IMMUTABLE` | 422 | Full Route update attempted to change the origin/destination pair; a new Route must be created |
| | `ROUTE_STOP_DUPLICATED` | 422 | A composite Route request repeats a Stop identity |
| | `ROUTE_STOP_ORDER_INVALID` | 422 | A composite Route request has duplicate, non-positive, or non-contiguous stop order indexes |
| | `ROUTE_GEOMETRY_TOO_LARGE` | 422 | Encoded route polyline exceeds 100 KiB |
| | `ROUTE_GEOMETRY_INVALID` | 422 | Encoded route polyline cannot be decoded as Google precision-5 or has points/count outside accepted bounds |
| | `ROUTE_GEOMETRY_STOP_MISMATCH` | 422 | One or more configured Stop/Station coordinates are farther than 500 m from the submitted route polyline; `error.fields` uses `stopIds`/`stationIds` |
| | `ROUTE_RETURN_NOT_CONFIGURED` | 422 | returnRouteId NULL khi đặt round-trip |
| | `ALTERNATIVE_ROUTE_LIMIT_EXCEEDED` | — (retired) | Không còn được emit; v1 không có global active-count cap cho AlternativeRoute |
| **Station** | `STATION_NOT_FOUND` | 404 | Day-7 Trip Station handlers use coded 404 path |
| | `STATION_DUPLICATE_NEARBY` | 200 (warning) | Operator tạo Station < 100m gần Station hiện có |
| | `STATION_MERGE_CONFLICT` | 409 | Merge làm Route origin=destination, vi phạm domain invariant hoặc precondition thay đổi sau khi lock; transaction không được partial relink |
| **DriverSchedule** | `SCHEDULE_HAS_TRIPS` | 409 | Soft-delete is blocked because generated Trips reference the schedule; `error.fields.tripCount` contains the count |
| **Location** | `LOCATION_NOT_FOUND` | 404 | Admin Location update/deactivate target does not exist |
| | `LOCATION_CODE_CONFLICT` | 409 | Admin Location code already exists |
| **Invoice** | `INVOICE_NOT_FOUND` | 404 | |
| | `INVOICE_PDF_GENERATION_FAILED` | 500 | Hangfire retry job |
| | `INVOICE_NUMBER_EXHAUSTED` | 409 | Monthly six-digit counter exceeded 999999; Invoice transaction rolls back |
| | `INVOICE_RETRY_ALREADY_PENDING` | 409 | Another retry/reconciliation worker already owns PENDING/PROCESSING |
| | `INVOICE_RETRY_NOT_ALLOWED` | 409 | ISSUED/CANCELLED or all five attempts consumed |
| **Operator** | `OPERATOR_DUPLICATE_REGISTRATION` | 409 | businessRegistrationNumber trùng |
| | `OPERATOR_DUPLICATE_TAX_CODE` | 409 | taxCode trùng |
| **Subscription** | `SUBSCRIPTION_LIMIT_EXCEEDED` | 422 | Vượt maxVehicles/maxRoutes/etc. |
| | `SUBSCRIPTION_MODULE_DISABLED` | 403 | Module flag = false (e.g. enableParcel) |
| | `SUBSCRIPTION_EXPIRED` | 402 | OperatorSubscription expired |
| | `SUBSCRIPTION_PAYMENT_PENDING` | 409 | Có upgrade attempt active; dùng GET subscription và retry session nếu được phép |
| | `SUBSCRIPTION_UPGRADE_EXPIRED` | 409 | Upgrade attempt đã quá hạn 15 phút |
| | `SUBSCRIPTION_PAYMENT_NOT_RETRYABLE` | 409 | Latest payment chưa FAILED/EXPIRED hoặc session khác còn pending |
| **Settlement** | `TRIP_SETTLEMENT_NOT_FOUND` | 404 | |
| | `TRIP_SETTLEMENT_ALREADY_SETTLED` | 409 | Status = SETTLED/CANCELLED |
| | `PLATFORM_WALLET_INSUFFICIENT_BALANCE` | 500 | Refund/settle thất bại, alert Admin |
| **Refund** | `REFUND_FAILURE_PERSISTED` | 500 | Retry exhausted, Admin manual handle |
| | `REFUND_RETRY_EXHAUSTED` | 500 | Hangfire job retry 5 lần |
| **Tracking** | `TRACKING_ACCESS_DENIED` | 403 | joinTripTracking unauthorized |
| | `TRACKING_TRIP_NOT_ACTIVE` | 409 | Trip chưa IN_PROGRESS |
| **RAG** | `RAG_DOCUMENT_NOT_APPROVED` | 403 | Status ≠ APPROVED |
| | `RAG_ACCESS_DENIED_FOR_ROLE` | 403 | accessLevel không match role |
| | `POLICY_NOT_FOUND` | 404 | Generic RAG Policy is missing, soft-deleted, or outside the caller tenant |
| | `POLICY_VERSION_CONFLICT` | 409 | PATCH version does not match the current Policy version |
| **Validation** | `VALIDATION_ERROR` | 422 | Field-level — kèm `errors` array |
| | `IDEMPOTENCY_KEY_REQUIRED` | 422 | Mutation contract requires the header explicitly; middleware pass-through is not acceptance |
| | `IDEMPOTENCY_KEY_MISMATCH` | 422 | Same key, different request fingerprint (actor/method/path/query/raw body) |
| | `IDEMPOTENCY_REQUEST_PENDING` | 409 | Same key is still being processed |
| | `RESOURCE_CONFLICT` | 409 | Resource snapshot changed during a guarded mutation; caller must reload before retrying with a new key |
| | `INVALID_SORT_FIELD` | 400 | sortBy value not in the per-aggregate whitelist |
| **Generic** | `RESOURCE_NOT_FOUND` | 404 | Fallback |
| | `FORBIDDEN` | 403 | RBAC reject |
| | `RATE_LIMITED` | 429 | Vượt rate limit |
| | `RATE_LIMIT_EXCEEDED` | 429 | Per-user/per-resource Day-38 invoice download limit |
| | `REPORT_VALUE_OVERFLOW` | 500 | Report source/orchestrator gặp count hoặc BIGINT/NUMERIC aggregate ngoài phạm vi Int64; không wrap, saturate hoặc trả partial |
| | `REPORT_RANGE_INVALID` | 422 | Operator report range không phải ngày Asia/Ho_Chi_Minh hợp lệ, đảo chiều hoặc vượt 92 ngày inclusive |
| | `UPSTREAM_UNAVAILABLE` | 502 or 503 by boundary | `502` only for Gateway/proxy and legacy pass-through endpoints; `503` for an in-service fail-closed orchestration explicitly documented by its endpoint, including Parcel email issuance, Platform Report sources, Admin Dashboard and Revenue facades. An endpoint MUST list the applicable status and never choose dynamically for the same failure class. |
| | `INTERNAL_ERROR` | 500 | Unhandled exception (Sentry capture) |

**Day-23 exact resolver mapping — POST
`/v1/bookings/{bookingId}/pending-actions/{actionId}/resolve`:** missing, invalid, or expired user
JWT is `401 AUTH_TOKEN_INVALID`; a valid non-`PASSENGER` role is `403 FORBIDDEN` before any
Booking/action lookup. Missing/not-owned Booking and a discovered Booking/action ownership
mismatch are masked as `404 BOOKING_NOT_FOUND`; only after owner authorization may a missing
action under that Booking return `404 BOOKING_PENDING_ACTION_NOT_FOUND`. An active incompatible
reason/state returns `409 BOOKING_PENDING_ACTION_NOT_RESOLVABLE`; a new key targeting
`SUPERSEDED`, `ACCEPTED|REJECTED`, or strictly-after-effective-cutoff state returns respectively
`409 BOOKING_PENDING_ACTION_SUPERSEDED`, `409 BOOKING_PENDING_ACTION_ALREADY_RESOLVED`, or
`409 BOOKING_PENDING_ACTION_EXPIRED` (equality remains eligible; timeout owns the outcome and only
auto-accepts, never cancels or refunds). Missing key is
`422 IDEMPOTENCY_KEY_REQUIRED`; malformed/non-v4 key, malformed route UUID, invalid action,
`selectedStopId`, or another shape failure is `422 VALIDATION_ERROR`; same key with a different
actor/method/path/query/raw-body fingerprint is `422 IDEMPOTENCY_KEY_MISMATCH`; the same executing
fingerprint is `409 IDEMPOTENCY_REQUEST_PENDING`. Same key + same payload replays the stored
response byte-identical before terminal lookup; only a new key evaluates the current state.

> Day-21 carry-over: the only current out-of-scope `INVALID_TRIP_STATUS` usage is
> `ArriveTripStopCommandHandler.cs:49`; Day-21 lifecycle code must use
> `TRIP_INVALID_TRANSITION` and must not copy that stale code.

> Day-34 preservation rule: `TRIP_NOT_SUBSTITUTABLE` is additive and substitution-only.
> `TRIP_NOT_IN_PROGRESS` remains HTTP 422 with its existing meaning for depart-stop, arrival,
> incident, and every other pre-Day-34 lifecycle contract; do not rewrite those endpoints to the
> new substitution code.

### 5.10 Data access pattern (Repository + optional Service)

**.NET — pattern bắt buộc:**

```
Controller
  → MediatR.Send(Command/Query)
    → Handler (inject I<Aggregate>Repository + I<Aggregate>Service khi cần)
       → I<Aggregate>Service (orchestrate, business logic — optional, chỉ khi shared logic)
          → I<Aggregate>Repository (data access)
             → EfRepository<T,TId> base impl (in libs)
                → ApplicationDbContext (concrete, sống trong Infrastructure)
```

- **Command Handler:** inject `I<Aggregate>Repository` (+ `I<Aggregate>Service` khi có shared logic). Modify entity qua domain method (`booking.Confirm()`), `repo.Update(booking)`. SaveChanges + commit transaction qua `TransactionBehavior` pipeline (5.11). Outbox INSERT cùng `SaveChangesAsync` qua `OutboxInterceptor`.
- **Query Handler:** inject `I<Aggregate>ReadRepository` (nếu có) hoặc `I<Aggregate>Repository.QueryNoTracking()`. Project sang DTO. KHÔNG dùng tracking cho read.
- **Repository:** chỉ data access — Get/Add/Update/Remove + query domain-specific. KHÔNG chứa business logic.
- **Service:** business logic + orchestration. Inject Repository + external client. KHÔNG inject DbContext direct.
- **Test handler:** mock `I<Aggregate>Repository` + `I<Aggregate>Service` qua NSubstitute. KHÔNG cần spin up DB cho unit test.

**NestJS — pattern:**

```
Controller
  → <Feature>Service.<method>()        (1 method = 1 use case)
     → @InjectRepository(Entity) repo  (Prisma, không bọc IXyzRepository)
        → DataSource (cho multi-write transaction)
```

- Prisma Repository đã đủ generic — KHÔNG cần wrap `IXyzRepository` interface ở TS layer (Jest mock module được).
- Service method = 1 use case. Vượt ~250 dòng → tách `XyzCommandService` / `XyzQueryService`.
- Multi-write transaction: `dataSource.transaction(async manager => { ... })`. Outbox INSERT trong cùng callback.

### 5.11 Transaction conventions

- **.NET:**
  - **Default (recommended):** Mỗi Command Handler wrap trong `TransactionBehavior` (MediatR pipeline từ libs) — pipeline tự `_db.Database.BeginTransactionAsync` trước handler, `SaveChangesAsync` + commit nếu success, rollback nếu exception. Handler **không tự manage transaction**.
  - **Optional `IUnitOfWork`:** Nếu service có long-running flow cần savepoint hoặc batch multiple aggregate trong cùng transaction phức tạp → inject `IUnitOfWork` (từ libs/dotnet/VietRide.Shared.Application), call `await _uow.BeginAsync(ct)` + `await _uow.CommitAsync(ct)` thủ công. Có comment giải thích lý do.
  - **Outbox INSERT** PHẢI nằm trong cùng transaction với business write — đảm bảo via `OutboxInterceptor` (libs) hook vào `SaveChanges`.
- **NestJS:**
  - `dataSource.transaction(async manager => { /* manager.getRepository(Entity).save(...) */ })` cho multi-write.
  - Outbox INSERT trong cùng callback.

---

## 6. Authentication & Authorization

> **Business rule chi tiết:** technical_context_v7 Section 5 (Auth & Authz) + Section 4.4 (System Admin) + Section 8 "Authentication & Authorization — Business rules chi tiết".

### 6.1 Token types

| Token | Algo | TTL | Issuer | Verified by | Storage |
|---|---|---|---|---|---|
| **User Access Token** | RS256 | 15 phút | Identity Service (private key in env) | Gateway + Tracking (qua JWKS public key) | Client memory only (KHÔNG localStorage cho mobile; web có thể HTTP-only cookie hoặc memory) |
| **Refresh Token** | Opaque random (UUID) | 30 ngày | Identity Service | Identity Service DB lookup | DB (`refresh_tokens` table) với rotate |
| **Internal JWT** | HS256 | 120 giây | API Gateway (sign với `INTERNAL_JWT_SECRET`) | Mỗi business service middleware | Per-request, không persist |
| **VNPay HMAC signature** | HMAC-SHA512 | — | VNPay | Payment Service callback handler | Per-request |
| **Parcel deliveryToken** | Opaque random (URL-safe) | 48 giờ | Parcel Service | Parcel Service DB lookup (hash compare) | DB hashed only |
| **SET_INITIAL_PASSWORD token** | UUID v4 | 48 giờ | Identity Service | Identity Service DB lookup | DB (`email_verification_tokens`) |
| **EMAIL OTP** | 6-digit numeric | 5 phút | Identity Service | Identity Service DB lookup + brute-force counter | DB (`email_verification_tokens`) |

### 6.2 User Access Token (RS256)

**Claims:**

```json
{
  "iss": "vietride-identity",
  "sub": "<userId UUID>",
  "role": "PASSENGER | DRIVER | ASSISTANT | OPERATOR_STAFF | OPERATOR_ADMIN | SYSTEM_ADMIN",
  "operatorId": "<uuid|null>",
  "email": "<string>",
  "iat": <unix>,
  "exp": <unix>,
  "kid": "<key id>"
}
```

**JWKS endpoint:** `GET /v1/.well-known/jwks.json` — Identity Service, public, no auth. Gateway & Tracking cache JWKS tại startup, refresh mỗi 1h hoặc khi gặp unknown `kid`.

**Key rotation policy:**

- **Normal:** add key mới vào JWKS, ký token mới bằng key mới, giữ key cũ tối thiểu `accessTokenTtl 15 phút + JWKS cache 1 giờ` (≈75 phút) rồi remove.
- **Compromised:** rotate, force JWKS refresh qua redeploy/restart. **KHÔNG implement blacklist access token v1** — chấp nhận token cũ vẫn valid tối đa 15 phút. RefreshToken có thể revoke per user/family/global qua DB.

### 6.3 Refresh Token rotation

- Opaque UUID, lưu DB, 30 ngày.
- Mỗi lần dùng → revoke token cũ, issue token mới cùng `familyId`.
- Grace period 30s tolerate parallel refresh từ mobile.
- **Reuse detection:** token revoked được dùng lại sau grace → revoke toàn bộ family → force re-login.
- Fields: `familyId UUID`, `parentTokenId nullable self-FK`, `revokedAt nullable`, `revokedReason enum (NORMAL_ROTATION | REUSE_DETECTED | USER_LOGOUT | ADMIN_REVOKE | PASSWORD_RESET)`.

### 6.4 Internal JWT (HS256, service-to-service)

Mọi inter-service HTTP call **bắt buộc** dùng Internal JWT — Gateway hoặc service caller sign mới mỗi request.

**Claims:**

```json
{
  "sub": "<userId từ User Access Token gốc>",
  "role": "<role>",
  "operatorId": "<uuid|null>",
  "callerService": "gateway | booking | trip | payment | parcel | ...",
  "iat": <unix>,
  "exp": <unix>   // iat + 120s
}
```

**Header:** `X-Internal-Auth: Bearer <internalJWT>`

**Flow:**

```
Client → Gateway:    Authorization: Bearer <userAccessToken>
Gateway verify RS256 → extract { userId, role, operatorId }
Gateway sign new Internal JWT với callerService="gateway"
Gateway → Booking:   X-Internal-Auth: Bearer <internalJWT>
Booking middleware verify HS256 + check exp → set HttpContext.User
Booking → Trip:      sign new Internal JWT với callerService="booking" (giữ user context)
```

**Security trade-off v1:** shared `INTERNAL_JWT_SECRET` env var. Compromise 1 service → forge token cho service khác. Acceptable cho capstone; v2 sẽ chuyển asymmetric per-service key hoặc mTLS.

**Exemptions (KHÔNG cần Internal JWT):**

- `/health`, `/ready` healthcheck.
- VNPay IPN callback (`POST /v1/payments/vnpay-ipn`, `/v1/payments/vnpay-topup-ipn`) — verify HMAC-SHA512.
- Parcel delivery email link confirmation (token-based, no JWT).
- Public registration / OTP request endpoints (`POST /v1/auth/register`, `POST /v1/operators/register`).
- Public password reset endpoints (`POST /v1/auth/forgot-password`, `POST /v1/auth/reset-password`).
- JWKS public endpoint.

### 6.5 Socket.IO authentication (Tracking)

Tracking Service nhận kết nối **trực tiếp từ Driver App + Passenger App** qua Socket.IO — **client-to-service**, dùng User Access Token (KHÔNG Internal JWT).

**Handshake:**

```js
io("wss://api.vietride.app", {
  path: "/tracking/socket.io",
  auth: { token: "<userAccessToken>" }
});
```

**Server middleware:**

1. Extract `socket.handshake.auth.token`.
2. Verify RS256 qua JWKS cache.
3. Attach `{ userId, role, operatorId }` vào `socket.data`.
4. Invalid → disconnect với error `UNAUTHORIZED`.

**Room assignment:**

```
role=DRIVER:           socket.join(`driver:${driverId}`)
role=OPERATOR_*:       socket.join(`operator:${operatorId}`)
```

**Trip-specific room:** client emit `joinTripTracking { tripId }` → Tracking Service verify quyền qua HTTP internal call tới Booking/Trip/Parcel Service → join `trip:{tripId}` nếu authorized.

**Token expiry trong long-lived connection:** Tracking middleware **chỉ verify JWT tại handshake**, KHÔNG re-verify mid-session. Client responsibility:

- Proactive refresh token ~1 phút trước TTL.
- Trên reconnect, pass token mới qua `auth: { token: newToken }`.
- `socket.io-client` có built-in exponential backoff reconnect.

### 6.6 RBAC Roles

| Role | Scope | operatorId required |
|---|---|---|
| `PASSENGER` | Passenger App | NULL |
| `DRIVER` | Driver App | NOT NULL |
| `ASSISTANT` | Driver App + cargo extras | NOT NULL |
| `OPERATOR_STAFF` | Operator Web (read + limited write trong operator) | NOT NULL |
| `OPERATOR_ADMIN` | Operator Web (full quyền operator) | NOT NULL |
| `SYSTEM_ADMIN` | Admin Web (toàn hệ thống) | NULL |

**Tenant isolation:** mọi query trong service có entity gắn `operatorId` BẮT BUỘC filter `WHERE operator_id = :claim` từ Internal JWT (trừ SYSTEM_ADMIN — không filter).

**Day-6 Operator status guard (Identity):** OPERATOR_ADMIN/OPERATOR_STAFF login is rejected with HTTP 403 `FORBIDDEN` when the caller's `Operator.registrationStatus != APPROVED`. Because access tokens can outlive a later suspend/reject, Identity application handlers MUST also re-check current Operator status for operator write/action endpoints. In Day 6 this applies to `POST /v1/operator/users`, `POST /v1/operator/users/{userId}/resend-initial-password`, and `PATCH /v1/operator/profile`: require current `Operator.registrationStatus=APPROVED`, otherwise return 403 `FORBIDDEN` with no side effects. `GET /v1/operator/profile` remains readable for OPERATOR_ADMIN/OPERATOR_STAFF even when non-APPROVED so the UI can display current status/policies. No Gateway -> Identity synchronous status hop is added.

**Operator lifecycle ActivityLog actor convention (Identity):** for operator onboarding/lifecycle actions, `activity_logs.user_id` stores the actor user id. Authenticated actions use the caller's user id; public operator self-registration uses the newly created OPERATOR_ADMIN user id as the self actor. Metadata is JSONB built via serializer (not string interpolation) and includes `operatorId`, `actorUserId`, `targetUserId` when different from actor, and `source` (for example `SELF_REGISTER`, `SYSTEM_ADMIN_CREATE_OPERATOR`, `OPERATOR_USER_CREATE`). System Admin suspend and reactivate actions use `SUSPEND_OPERATOR` and `REACTIVATE_OPERATOR` respectively and record actor, operator ID and source.

**Day-6 reject subscription rule (Identity):** when System Admin rejects a PENDING operator, Identity sets `Operator.registrationStatus=REJECTED` and sets the matching PENDING_APPROVAL `OperatorSubscription.status=CANCELLED`. `operator_subscriptions` has no `deleted_at` column and is not soft-deletable, so implementations MUST NOT set a subscription `deletedAt` value for reject.

**Day-37 pending-payment entitlement rule (Identity and consumers):** while
`OperatorSubscription.status=PENDING_PAYMENT`, `activePlanId` remains the sole entitlement source
for quota allocation/increment and module flags (`enableParcel`, `enableShuttle`, `enableRag`). The
target plan in `SubscriptionUpgradeAttempt` grants no entitlement before Payment succeeds.

### 6.7 Account status enums

**`User.status`:**
```
PENDING_EMAIL_VERIFICATION | PENDING_INITIAL_PASSWORD | ACTIVE | LOCKED | DELETED
```

**`Operator.registrationStatus`:**
```
PENDING | APPROVED | REJECTED | SUSPENDED
```

> KHÔNG dùng `LOCKED` cho Operator — chỉ User. KHÔNG dùng `SUSPENDED` cho User — chỉ Operator. Tránh nhầm.

### 6.8 Password policy

- Min 8 ký tự, ≥1 chữ + ≥1 số.
- Hash bcrypt cost 12.
- Password change require verify mật khẩu cũ.
- Login allows `PASSENGER` users in `PENDING_EMAIL_VERIFICATION` to receive a normal token bundle for the mobile restricted session; FE gates features via `data.user.status`. Non-passenger `PENDING_EMAIL_VERIFICATION` users still fail with `AUTH_EMAIL_NOT_VERIFIED`.
- Password reset for any `ACTIVE` user role uses a `PASSWORD_RESET` email OTP. `forgot-password` returns generic success for unknown/non-eligible emails; `reset-password` marks the OTP used, hashes the new password, and revokes active refresh tokens with reason `PASSWORD_RESET`.
- **Account lockout:** 5 lần sai trong 15 phút → `User.status = LOCKED`. Chỉ System Admin mở khóa. Login thành công reset counter.
- Track `failedLoginAttempts` + `lastFailedLoginAt` trên User entity.

### 6.9 Email OTP — 2 lớp bảo vệ

- **Rate limit:** Redis `identity:otp_rate:{email}` max 3/giờ TTL 1h for registration OTP, and `identity:pwd_reset_rate:{email}` max 3/giờ TTL 1h for password reset OTP.
- **Brute-force:** `EmailVerificationToken.failedAttempts` increment mỗi nhập sai; invalidate sau 5 lần.

### 6.10 RBAC enforcement pattern

**.NET:**

```csharp
[Authorize(Roles = "OPERATOR_ADMIN,OPERATOR_STAFF")]
[HttpPost("/v1/operator/vehicles")]
public Task<IActionResult> CreateVehicle(...) { ... }
```

Custom `OperatorTenantFilter` injected → filter query theo `operatorId` claim.

Driver/Assistant trip reads use assignment scope rather than operator scope. In particular,
`GET /v1/driver/trips/{tripId}/route` accepts `DRIVER`/`ASSISTANT` only and returns Route geometry
only when JWT `sub` equals the Trip's `driver_user_id` or `assistant_user_id`; an existing
unassigned Trip returns `403 FORBIDDEN`. The response exposes nullable precision-5
`pathPolyline`, origin/destination Station coordinates, and ordered TripStop coordinates, with no
PII or operator-management metadata. The existing `/v1/driver` Gateway prefix already owns this
route; the operator Route endpoint remains role-isolated.

**NestJS:**

```ts
@UseGuards(JwtAuthGuard, RolesGuard)
@Roles('OPERATOR_ADMIN', 'OPERATOR_STAFF')
@Post('/v1/operator/vehicles')
createVehicle(@CurrentUser() user: UserContext, @Body() dto: CreateVehicleDto) { ... }
```

**Day-37 Trip create guard:** before dispatch/persistence, `POST /v1/operator/vehicles` and
`POST /v1/operator/routes` validate operator active/approved and the general active subscription
with `requireShuttleModule=false`; neither endpoint depends on `enableShuttle`.
`POST /v1/operator/shuttle-trips` performs the same operator guard and validates subscription with
`requireShuttleModule=true` before calling the dispatch service. Stable outcomes are expired
subscription → `402 SUBSCRIPTION_EXPIRED`, disabled Shuttle module → `403
SUBSCRIPTION_MODULE_DISABLED` (Shuttle only), non-eligible operator/role → `403 FORBIDDEN`, and
Identity unavailable/unusable response → `503 UPSTREAM_UNAVAILABLE`.
Mọi mutation Shuttle (cancel request, cancel ShuttleTrip, driver start/pickup/delivered/no-show/
complete) áp dụng lại cùng hai guard này; replay lifecycle chỉ phát Outbox khi domain thực sự
chuyển trạng thái.

### 6.11 Day-40 Identity per-user serialization

Mọi path đọc `User.status` rồi có thể phát token/OTP hoặc ghi auth state phải tuyến tính hóa trên
cùng User: password login của User hiện hữu, Google login của linked/matched User, refresh,
forgot/reset password, password-reset OTP failure, failed-login persistence và admin lock/unlock.

1. Lookup email/OAuth subject/token hash trước transaction chỉ là routing hint để tìm `userId`.
2. Mở PostgreSQL transaction, lock `users` bằng `SELECT ... FOR UPDATE`, rồi force reload status;
   EF identity map không được trả snapshot đã track. Nhiều User được lock theo UUID lowercase format
   `D`, ordinal ascending.
3. Lock order sau User là `EmailVerificationToken` theo UUID, refresh-token row/family theo UUID,
   rồi mới insert ActivityLog/Outbox. Không path nào lock token trước rồi quay lại User.
4. Password login đúng verify lại password hash trên entity đã lock, update login state và insert
   refresh token trong transaction; chỉ trả token sau commit. Login sai giao cho fresh-scope
   `FailedLoginPersister.PersistAsync(userId)` để tránh ambient self-deadlock.
5. Refresh dùng fresh-scope executor: lock User trước, re-read presented token `FOR UPDATE`, recheck
   owner/revocation/expiry/status, rotate hoặc revoke family và commit outcome trước khi trả/throw.
6. Forgot/reset password và OTP failure lock/reload User trước; chỉ `ACTIVE` được tạo/consume
   `PASSWORD_RESET` OTP. Lock-first không tạo OTP/Outbox, không consume OTP, không đổi password.
7. Failed-login persister chỉ xử lý `ACTIVE` hoặc passenger `PENDING_EMAIL_VERIFICATION`, tăng
   Redis counter dưới row lock và ghi từ entity vừa reload. Khi auto-lock, nó lưu
   `locked_from_status` bằng status nguồn; User đã `LOCKED` hoặc không login-eligible là no-op.
8. Google account chưa tồn tại dựa vào unique email/OAuth constraints cho create race; token issue
   vẫn nằm trong transaction sở hữu row mới.

Password login và Google login cùng project stored `User.avatarUrl` vào `UserSummaryDto`. Google
provider avatar chỉ seed User mới; link/re-login không overwrite avatar hiện hữu. Null giữ
`JsonIgnore(WhenWritingNull)`.

Identity thêm `users.locked_from_status user_status NULL`. Backfill User `LOCKED` cũ thành `ACTIVE`;
check constraint chỉ nhận `ACTIVE|PENDING_EMAIL_VERIFICATION` và bắt buộc origin khác null đúng khi
`status=LOCKED`. Manual lock chỉ cho `ACTIVE -> LOCKED`; password lockout còn cho phép
`PENDING_EMAIL_VERIFICATION -> LOCKED`. Unlock restore đúng origin, reset DB + Redis lockout state,
clear origin và không phục hồi refresh token đã revoke. Verify email vẫn là đường duy nhất chuyển
pending passenger lên `ACTIVE`.

Linearized outcomes bắt buộc:

- Auth/refresh commit trước thì lock chạy sau revoke refresh token vừa tạo/rotate; lock commit trước
  thì auth recheck thấy `LOCKED` và không tạo token.
- Failed login commit trước thì lock ensure-locked chạy sau; lock trước thì persister no-op.
- Failed login commit trước unlock thì unlock restore origin và clean counter; unlock trước thì lần
  fail sau được tính trên status vừa restore, không promote pending-email thành active.
- Forgot/reset commit trước có thể hoàn tất rồi lock giữ final `LOCKED`; lock trước làm password flow
  generic-success/deny mà không tạo hoặc consume OTP.

`POST /v1/admin/users/{userId}/lock` và `/unlock` chỉ cho `SYSTEM_ADMIN`, cấm self-action và dùng
shared idempotency v2. Lock/ensure-lock revoke active refresh tokens với `ADMIN_REVOKE` và insert
`LOCK_USER`; unlock insert `UNLOCK_USER`. Mỗi logical idempotent request ghi đúng một ActivityLog.
Day 40 không thêm access-token denylist; access token cũ sống tối đa tới expiry.

### 6.12 Day-40 immutable ActivityLog

`ActivityLog.user_id` luôn là actor. Metadata lock/unlock chỉ chứa allow-list
`targetUserId,previousStatus,newStatus,statusChanged`; không chứa password, OTP, token hoặc full
request. Bổ sung action `UNLOCK_USER`, `STATION_MERGED`, `STATION_NORMALIZED`, nullable
`source_event_id`, partial unique index trên source event và global index
`(created_at DESC, id DESC)`. Application chỉ expose Add/read; PostgreSQL trigger từ chối mọi
`UPDATE`/`DELETE`. Query admin dùng UTC half-open `[from,to)` và deterministic
`created_at DESC,id DESC`.

---

## 7. Inter-service Communication

### 7.1 Khi nào sync (HTTP REST) vs async (RabbitMQ)

| Pattern | Khi dùng | Ví dụ |
|---|---|---|
| **HTTP REST (sync)** | Cần kết quả ngay để tiếp tục flow | Booking → Trip: "lock seats" · Booking → Payment: "wallet charge" · Operator dashboard validate ref |
| **RabbitMQ (async)** | Side-effect, không cần chờ | Booking confirmed → Notification push · Trip completed → Payment settle |

### 7.2 HTTP internal endpoint registry (tổng hợp)

> Mọi endpoint dưới đây dùng prefix `/internal/v1/...`, yêu cầu `X-Internal-Auth` header.

#### Identity & User Service

| Method + Path | Caller | Mục đích |
|---|---|---|
| `GET /internal/v1/users/{userId}` | All services | Internal-JWT-only raw user lookup `{ id, displayName, avatarUrl, role, operatorId, status, phone }` for HTTP validate logical FK. Errors use ADR 0004 envelope. Trip DriverSchedule create/activation validates role/operator; Shuttle dispatch còn yêu cầu driver active có display name/phone và snapshot hai field này vào assignment event. |
| `GET /internal/v1/users?ids=<uuid>&ids=<uuid>` | Booking, Trip, Parcel, RAG | Read-only 1..100 user batch; raw additive display/contact/status DTO, including a redacted representation for requested soft-deleted IDs |
| `GET /internal/v1/users/by-phone?phone={normalizedE164}` | Booking | Internal-JWT-only exact non-deleted-user phone lookup for the operator booking-monitor filter. Caller URI-escapes a prevalidated canonical E.164 value; raw success is exactly `{ userId }`, no PII. No match is ADR 0004 `404 RESOURCE_NOT_FOUND`. Booking maps only that exact response to an empty result; all other failures map to `502 UPSTREAM_UNAVAILABLE`. |
| `GET /internal/v1/users/by-email?email=` | Parcel | Lookup recipient user khi tạo parcel |
| `GET /internal/v1/users/{userId}/device-tokens` | Notification | Lấy FCM tokens active để push |
| `GET /internal/v1/operators/{operatorId}` | All services | Lookup operator info for logical FK validation (raw success DTO) |
| `GET /internal/v1/operators/{operatorId}/subscription` | Booking, Trip, Parcel | Raw current subscription + plan limits/module flags + usage counters |
| `POST /internal/v1/operators/{operatorId}/usage/increment` | Trip, Booking, Parcel | Body `{resource, delta}` where resource is `VEHICLES|DRIVERS|ASSISTANTS|OPERATOR_USERS|ROUTES|TRIPS_THIS_MONTH`; atomically increment usage counter without concurrent overshoot |
| `POST /internal/v1/operators/{operatorId}/quota-allocations` | Trip | Claim durable idempotent quota allocation by `{ resource, resourceId, periodKey? }`; no distributed transaction |
| `POST /internal/v1/operators/{operatorId}/quota-allocations/{allocationId}/release` | Trip | Idempotently release an allocation after local persistence fails or its resource is soft-deleted |
| `POST /internal/v1/operators/summaries/batch` | Booking, Payment | Read-only batch lookup `{ operatorIds }`, tối đa 500 distinct non-empty UUID; raw additive `{ operatorId, operatorName, logoUrl, contactPhone }`, gồm cả operator soft-deleted, sort ID tăng dần; empty input trả empty list; không yêu cầu Idempotency-Key |
| `GET /internal/v1/admin/dashboard/identity-metrics?from=&to=` | Booking | Raw latest-login count for the Asia/Ho_Chi_Minh range plus current user-role counts, approved/active operator IDs and operator-status distribution; no historical status inference |
| `POST /internal/v1/payments/subscription` | Identity | Create/replay a VNPay subscription payment from a server-side upgrade snapshot |
| `POST /internal/v1/payments/{paymentId}/expire-subscription` | Identity | Idempotently expire a pending subscription payment during the Identity-owned auto-revert job |

#### Trip-Route-Vehicle Service

| Method + Path | Caller | Mục đích |
|---|---|---|
| `GET /internal/v1/trips/{tripId}?pricingAt=` | Booking, Parcel, Tracking, Payment | Raw Trip snapshot; includes nullable `actualDepartureTime`, nullable route `totalDistanceKm`, and stops with `status`, nullable `actualArrivalTime`, nullable `distanceFromOriginKm`, and `orderIndex`. Valid Internal JWT only (`401 AUTH_TOKEN_INVALID`), no tenant authorization. Optional ISO-offset `pricingAt` resolves ordinary Booking fare as `MANUAL_OVERRIDE` → active half-open `RouteStopFareTemplate` → `Trip.baseFare`, then applies the matching active operator holiday surcharge by the Trip departure Asia/Ho_Chi_Minh date. Omitted preserves persisted legacy snapshot semantics and applies no new surcharge. No event/projection is added. |
| `GET /internal/v1/trips/{tripId}/shuttle-road-distance?direction=&latitude=&longitude=` | Booking | Internal-JWT-only road distance to origin Station (`INBOUND_TO_STATION`) or destination Station (`OUTBOUND_FROM_STATION`). Trip validates Station active/supportsShuttle/coordinates and calls Google Routes `travelMode=DRIVE`; raw success is `{ distanceMeters }`. Google/configuration/timeout/invalid response maps to `503 SHUTTLE_DISTANCE_UNAVAILABLE`; direction/coordinates/station eligibility maps to `422`. |
| `POST /internal/v1/trips/summaries/batch` | Parcel | Read-only `{ tripIds }`, 1..100 distinct UUIDs; one Trip query returns route/station/vehicle/crew/timing summaries; missing IDs are omitted |
| `POST /internal/v1/operators/vehicle-counts/batch` | Payment | Read-only `{ operatorIds }`, 1..100 distinct UUIDs; raw current vehicle counts by operator |
| `GET /internal/v1/operators/{operatorId}/route-performance?month=YYYY-MM` | Payment | Raw Asia/Ho_Chi_Minh-month trip/completed-trip aggregates grouped by route for the explicit operator tenant |
| `POST /internal/v1/trips/{tripId}/lock-seats` | Booking | Lock seats trong checkout (TTL 10 phút Redis) |
| `POST /internal/v1/trips/round-trip/lock-seats` | Booking | Lock outbound + return seats atomically in one Trip-owned Redis Lua script; if either leg fails, no seat is held |
| `POST /internal/v1/trips/{tripId}/release-seats` | Booking | Release seat khi payment fail/timeout |
| `POST /internal/v1/trips/{tripId}/book-seats` | Booking | Convert HELD → BOOKED khi payment success (API contract canonical name; was `confirm-seats`) |
| `GET /internal/v1/trips/{tripId}/passengers-pending` | Booking | Cho operator dashboard |
| `GET /internal/v1/stations/{id}` · `GET /internal/v1/stops/{id}` · `GET /internal/v1/routes/{id}` | All services | Trip internal-auth required; raw DTO lookup. Station active returns canonical resolution; merged soft-delete returns original identity plus terminal `canonicalStationId`; ordinary soft-delete/missing returns `STATION_NOT_FOUND`. Stop not found returns `STOP_NOT_FOUND`. Errors use ADR 0004 envelope. |
| `GET /internal/v1/reports/platform/trips?from=&to=` | Booking | Raw completed-Trip count grouped by operator; `status=COMPLETED`, `completed_at` in UTC `[from,to)` |
| `GET /internal/v1/trips/{tripId}/cargo/capacity` | Parcel | Lấy available cargo capacity |
| `POST /internal/v1/trips/{tripId}/cargo/reserve` · `remeasure` · `load` · `release` | Parcel | Idempotent single-Trip cargo-ledger mutation and counter update |
| `POST /internal/v1/trips/{sourceTripId}/cargo/transfer` | Parcel | Exact `{parcelId,targetTripId,targetState:RESERVED\|LOADED,allowCapacityOverflow}`; lock source/target by ascending UUID and atomically release source plus reserve/load target in one Trip-local transaction. `RESERVED` always enforces capacity; `LOADED` permits explicit overflow only for approved substitution recovery. |
| `GET /internal/v1/trips/{tripId}/route-geometry` | Tracking | Additive route-map context `{tripId,geometrySource:ROUTE_POLYLINE\|STOPS_ONLY,points,originStation?,intermediateStops,destinationStation?,alertRecipientUserIds?}`. Polyline malformed/null dùng ordered stop fallback; public Tracking chỉ render line cho `ROUTE_POLYLINE`. |
| `GET /internal/v1/trips/{tripId}/route-stops` | Tracking | Ordered ETA stops gồm additive `status`; Tracking vẫn chấp nhận thiếu/null status khi rolling deploy. |
| `GET /internal/v1/shuttle-trips/{shuttleTripId}/tracking-context` | Tracking | Additive `direction`, ShuttleTrip `status`, `isOwnPickup` theo queried `userId` và public station metadata. Passenger allowed khi own manifest là `PENDING\|PICKED_UP`; full stops chỉ là internal input cho Driver/ETA và không được phát public. Outbound Station stop chuyển terminal sau khi ShuttleTrip rời bến để ETA tiến tới passenger đầu tiên. |

#### Booking Service

| Method + Path | Caller | Mục đích |
|---|---|---|
| `GET /internal/v1/bookings/{id}` | Tracking, Payment, Parcel | Lookup booking snapshot, including active ticket count for parcel attach |
| `GET /internal/v1/bookings/history?userId=&status=&from=&to=&page=&pageSize=` | Parcel | Owner-scoped Booking history for the passenger facade; pages Booking aggregates, includes Ticket summaries, uses `[from,to)` over `created_at`, and orders `created_at DESC, id DESC` |
| `GET /internal/v1/bookings/trips/{tripId}/edit-impact?operatorId=` | Trip | Required trusted `operatorId`; every query predicates `trip_id` and `operator_id`, active is exactly `PENDING_PAYMENT|CONFIRMED`, raw PII-free `{tripId,activeBookingCount,activeBookings:[{bookingId,status,seatNumbers}]}`, empty is `200`. |
| `GET /internal/v1/bookings/trips/{tripId}/vehicle-substitution-impact?operatorId={operatorId}` | Trip | Internal-JWT-only raw exact `{oldTripId,operatorId,bookings:[{bookingId,bookingStatus,passengers:[{passengerId,boardingStatus,originalSeatNumber}]}]}`. Eligibility is `CONFIRMED\|PARTIAL_NO_SHOW` Booking and `BOARDED\|PENDING` Passenger; `originalSeatNumber` is nullable for chained substitutions. Booking does not return `seatType`. Trip derives the preferred seat type by looking up the old Trip's `TripSeat` when `originalSeatNumber` is non-null; a null original seat or no matching old TripSeat means no preferred type, so deterministic allocation falls back to the remaining passenger-seat order, then null when exhausted. Every query uses the `tripId` and `operatorId` predicate, results use bookingId then passengerId order, empty is `200` with `bookings:[]`, and no PII is returned. Invalid JWT is `401 AUTH_TOKEN_INVALID`; invalid input is `422 VALIDATION_ERROR`. |
| `GET /internal/v1/bookings/trips/{tripId}/stops/{stopId}/pending-passenger-count?operatorId=` | Trip | Raw exact `{tripId,stopId,pendingPassengerCount}`. Predicate is `Booking.status=CONFIRMED AND Passenger.boardingStatus=PENDING AND Booking.tripId=:tripId AND Booking.pickupStopId=:stopId AND Booking.operatorId=:operatorId`. Valid Internal JWT only; malformed/all-zero UUID → `422 VALIDATION_ERROR`, invalid JWT → `401 AUTH_TOKEN_INVALID`; no Trip/Stop lookup, tenant claim, or absent-reference `403`/`404`. |
| `GET /internal/v1/bookings/{id}/access-check?userId=` | Tracking | Verify Socket.IO joinTripTracking authz |
| `GET /internal/v1/vouchers/by-code/{code}` | Booking (own service); also exposed for admin reports |
| `GET /internal/v1/reports/platform/bookings?from=&to=` | Booking/raw verification | Raw completed-Booking count grouped by operator; legacy money field không thay Payment ledger; `status=COMPLETED`, `completed_at` in UTC `[from,to)` |

#### Payment & Wallet Service

| Method + Path | Caller | Mục đích |
|---|---|---|
| `POST /internal/v1/payments/charge` | Booking, Parcel | Charge WALLET/VNPAY; nullable `dueAt`, Booking VNPay truyền exact seat-lock expiry (round-trip dùng leg sớm hơn); `dueAt <= now` → `422 PAYMENT_DEADLINE_PASSED` |
| `POST /internal/v1/payments/batch-charge` | Booking | WALLET batch charge for round-trip: per-item Payment `referenceType=BOOKING`, per-item wallet ledger `referenceType=BOOKING_PAYMENT`, all-or-nothing in one Payment DB transaction |
| `POST /internal/v1/payments/vnpay-init` | Booking, Parcel | Tạo VNPay redirect URL |
| `POST /internal/v1/payments/redirect-sessions/lookup` | Booking, Parcel | Read-only raw redirect lookup; Internal JWT, `[SkipIdempotency]`, `Cache-Control: no-store`, 1–100 unique references, one `AsNoTracking` query; latest attempt first then strict owner/context/VNPAY/PENDING_REDIRECT/future persisted dueAt/trusted-authority eligibility |
| `GET /internal/v1/revenue/admin-summary?from=&to=` | Booking | Raw canonical project revenue và independent settlement summary; input calendar Asia/Ho_Chi_Minh, persistence range UTC half-open |
| `GET /internal/v1/revenue/operators/{operatorId}/summary?from=&to=` | Parcel | Raw canonical operator ticket/parcel revenue, gồm gross Parcel và signed refund; input calendar Asia/Ho_Chi_Minh, persistence range UTC half-open |
| `POST /internal/v1/revenue/backfills/parcel-voucher-reversals?dryRun=` | Vận hành nội bộ | Dry-run/apply append-only idempotent Parcel voucher reversal backfill; không qua Gateway |
| `GET /internal/v1/wallets/{userId}/balance` | Booking (preview) | Check balance UI trước checkout |
| `POST /internal/v1/refunds` | Booking, Parcel | Trigger refund (event-driven preferred — HTTP fallback) |

#### Parcel Service

| Method + Path | Caller | Mục đích |
|---|---|---|
| `GET /internal/v1/parcels/{id}` | Tracking, Notification | Verify Socket.IO joinTripTracking hoặc resolve recipient policy từ snapshot `{ parcelId, tripId, status, senderUserId, recipientUserId?, operatorId, dropoffStopId? }`; trả ADR 0004 envelope và vẫn đọc được terminal rows |
| `GET /internal/v1/parcels/{id}/access-check?userId=` | Tracking | Same |
| `GET /internal/v1/reports/platform/parcels?from=&to=` | Booking | Raw delivery-confirmed count grouped by operator; legacy money field không thay Payment ledger; `confirmed_at` in UTC `[from,to)` |

#### Tracking Service

| Method + Path | Caller | Mục đích |
|---|---|---|
| `GET /internal/v1/tracking/trips/{tripId}/latest` | Operator dashboard (REST fallback) | Last known GPS |

#### Notification Service

| Method + Path | Caller | Mục đích |
|---|---|---|
| `POST /internal/v1/emails` | Identity, Payment, Parcel | Enqueue transactional email delivery (Internal-JWT only, UUID-v4 Idempotency-Key, `202 Accepted`, ADR 0004 envelope). General body `{ notificationId?: uuid\|null, dedupeKey?, toEmail, templateKey, templateData }`. Parcel's exact body is `{notificationId:null,dedupeKey:"parcel-delivery-token:<tokenRowId>",toEmail:recipientEmail,templateKey:"PARCEL_DELIVERY_LINK",templateData:{deliveryUrl,parcelCode,expiresAt}}`, with HTTP Idempotency-Key equal to `tokenRowId`. **Sensitive — OTP, set-password URL, and Parcel delivery URL are runtime-only and MUST NOT be persisted in `outbox_messages` or logged**; Notification scrubs them before audit/log. Do not route sensitive links through Outbox events. Parcel commits issuance only after `202`; every other response/timeout/transport failure maps to `503 UPSTREAM_UNAVAILABLE` with no token/state commit. |

> Identity owns its existing provider selection. Parcel always calls this Notification endpoint
> through `NOTIFICATION_BASE_URL` and uses `PUBLIC_APP_URL` to build the delivery link. Parcel
> never selects `EMAIL_PROVIDER` and takes no SendGrid dependency; outbound auth uses the existing
> Internal JWT factory/handler pattern (§5.3).

#### Day-24 public Trip stop departure

`POST /v1/driver/trips/{tripId}/stops/{stopId}/depart` is bodyless, assigned `DRIVER`/`ASSISTANT`
only, tenant-scoped, and requires UUID-v4 Idempotency-Key. First execution requires
`Trip.status=IN_PROGRESS`, `TripStop.status=ARRIVED`, and null `actualDepartureTime`; Trip and
TripStop lock/CAS recheck before persisting one timestamp. Success is public `200 ApiResponse` with
exact data `{tripId,stopId,departedAt,pendingPassengerCount,eventEmitted}`. New-key repeat after
departure is `409 TRIP_STOP_ALREADY_DEPARTED`; `PENDING|SKIPPED` is
`422 TRIP_STOP_NOT_ARRIVED`; upstream Booking failure is `502 UPSTREAM_UNAVAILABLE`. Same-key
replay and mismatch follow §5.6. A positive exact Booking pending-count result emits only
`trip.stop.departed_with_pending`.

### 7.3 RabbitMQ event registry

**Exchange:** `vietride.events` (topic exchange).
**Routing key format:** `<producer-service>.<aggregate>.<verb_past>` — all lowercase, dot-separated.
**Queue per consumer:** `<consumer-service>.<purpose>` — durable, manual ack.
**Dead letter:** `vietride.events.dlq` (queue) attached qua DLX `vietride.events.deadletter`.

| Event (routing key) | Producer | Consumers | Payload essentials |
|---|---|---|---|
| `identity.user.created` | Identity | Payment (init Wallet UPSERT idempotent) | `{ userId, role, email, createdAt }` |
| `identity.user.deleted` | Identity | Booking, Payment | `{ userId }` (soft delete cascade) |
| `identity.operator.approved` | Identity | Payment (init OperatorWallet) | `{ eventId, operatorId, approvedAt }`; new approvals generate an eventId in the approval transaction; legacy backfill reuses the stable eventId persisted in `operator_wallet_backfill_markers` |
| `identity.operator.suspended` | Identity | Trip, Booking | `{ operatorId, suspendedAt }` |
| `identity.firebase_session.revoke_requested` | Identity | Identity | `{ eventId, occurredAt, userId, reason }`; user lock emits one request, operator suspend emits one per scoped `OPERATOR_ADMIN`; consumer calls Firebase refresh-token revocation, treats missing Firebase users as no-op, and lets transient failures retry/DLQ |
| `booking.booking.created` | Booking | Tracking, Notification | Exact `{ eventId, occurredAt, bookingId, bookingCode, tripId, status: "CONFIRMED", ticketCodes[], seatNumbers[], departureDateTime, passengerCount, pickup: { stationId, stopId, address }, dropoff: { stationId, stopId, address }, driverUserId, assistantUserId }`; emitted atomically with the CONFIRMED transition; `eventId == OutboxMessage.Id == RabbitMQ MessageId`. |
| `booking.booking.confirmed` | Booking | Notification, Payment (settle hold), Booking (BookingStats counter), Trip (shuttle fan-out) | `{ bookingId, tripId, totalAmount, userId, voucherUsageId?, bookingCode?, tickets?: [{ ticketId, passengerUserId? }], ticketCodes?, ticketCount?, shuttleRequests?: [{ direction, address, latitude, longitude, roadDistanceMeters }], shuttlePickup?: { address, latitude, longitude } }`; `shuttlePickup` is legacy inbound compatibility. |
| `booking.booking.cancelled` | Booking | Notification, Trip, Payment, Booking, Tracking | Canonical fields plus operational `{ tripId, previousStatus: PENDING_PAYMENT\|CONFIRMED, seatNumbers[] }`. Legacy payload remains accepted. Crew Notification/Tracking process only `previousStatus=CONFIRMED` and suppress per-booking crew fan-out for terminal Trip cancellation/disruption. Passenger refund and existing consumers remain unchanged. |
| `booking.booking.disrupted` | Booking | Notification | Exact `{ eventId, occurredAt, bookingId, bookingCode, tripId, operatorId, userId, traveledRatio, refundAmount, cancellationReason }`; sole passenger-facing notification fact for no-substitution disruption. Payment MUST NOT bind it; canonical `booking.booking.cancelled` remains the sole Booking refund trigger. Booking writes status plus both facts atomically with distinct stable EventIds, each equal to its own Outbox id/MessageId. |
| `booking.booking.refunded` | Booking | Notification, Booking (BookingStats counter) | `{ bookingId, userId, amount, bookingCode?, ticketCodes?, ticketCount? }` |
| `booking.payment_refund.requested` | Booking | Payment | One event per Booking allocation: `{ eventId, occurredAt, paymentId, paymentReferenceType: BOOKING\|BOOKING_GROUP, paymentReferenceId, bookingId, userId, amount, reason: PAYMENT_CAPTURE_AFTER_BOOKING_EXPIRY\|SEAT_CONFIRMATION_FAILED }`. Payment ignores event `userId`/`amount` until exact captured VNPay Payment, owner, original reference and trusted-context net allocation are revalidated. |
| `booking.booking.seat_reassignment_required` | Booking | Notification | `{ eventId, occurredAt, bookingId, tripId, userId, pendingActionId, deadline, seatNumbers, reason: SEAT_REMOVED\|SEAT_DISABLED\|SEAT_TYPE_DOWNGRADED }` |
| `booking.booking.schedule_change_informational` | Booking | Notification | For `CONFIRMED` Bookings only; exact MINOR-only `{ eventId, occurredAt, bookingId, tripId, userId, oldDeparture, newDeparture, severity: MINOR }`; no pending-action fields |
| `booking.booking.schedule_change_required` | Booking | Notification | For `CONFIRMED` Bookings only; MEDIUM/MAJOR-only `{ eventId, occurredAt, bookingId, tripId, userId, pendingActionId, deadline, oldDeparture, newDeparture, severity: MEDIUM\|MAJOR }` |
| `booking.booking.pending_action_realerted` | Booking | Notification | Common `{ eventId, occurredAt, bookingId, tripId, userId, pendingActionId, deadline }` plus either `{ reason: PENDING_SEAT_ASSIGNMENT, seatNumbers, seatImpactReason }` or `{ reason: SCHEDULE_CHANGE, oldDeparture, newDeparture, severity: MEDIUM\|MAJOR }` |
| `booking.booking.pending_action_auto_resolved` | Booking | Notification | Exact `{ eventId, occurredAt, bookingId, tripId, userId, pendingActionId, resolvedAction, severity, oldDeparture, newDeparture }`; `resolvedAction=ACCEPTED` |
| `booking.voucher.consent_requested` | Booking | Notification (operator) | `{ eventId, occurredAt, voucherId, operatorId, voucherCode, voucherType, voucherValue }`; one fact per newly-created consent for an admin-created `OPERATOR_FUNDED` voucher; operator-owned self-consented vouchers emit none |
| `booking.booking.transferred` | Booking | Notification, Tracking | Exact `{eventId,occurredAt,sourceSubstitutionEventId,bookingId,recipientUserId,operatorId,oldTripId,newTripId,newVehicleId,newVehiclePlateNumber,newTripDepartureDateTime,notifyPassengers,transfers:[{passengerId,originalSeatNumber,newSeatNumber,confirmationStatus}]}`; exactly one fact per eligible Booking per substitution. Tracking emits `booking:updated` with `reason=BOOKING_TRANSFERRED` into both old/new Trip crew rooms. |
| `booking.passenger.boarded` | Booking | Tracking | Exact `{ eventId, occurredAt, bookingId, bookingCode, tripId, passengerRecordId, seatNumber, ticketCode, boardedAt }`; written to Outbox atomically with passenger `BOARDED` and ticket `USED`, then broadcast as crew-only `booking:updated` with `reason=PASSENGER_BOARDED`. |
| `booking.voucher.consent_accepted` | Booking | Notification | `{ voucherId, operatorId }` |
| `booking.voucher.consent_rejected` | Booking | Notification | `{ voucherId, operatorId, reason? }` |
| `trip.trip.boarding_started` | Trip | Notification | `{ tripId, boardingStartedAt }` |
| `trip.trip.assigned` | Trip | Notification | `{ tripId, operatorId, driverUserId, assistantUserId?, routeName, vehiclePlateNumber, departureDateTime }` |
| `trip.trip.crew_changed` | Trip | Notification | `{ tripId, operatorId, oldDriverUserId, oldAssistantUserId?, driverUserId, assistantUserId?, routeName, vehiclePlateNumber?, departureDateTime }` |
| `trip.trip.started` | Trip | Parcel (block new parcel), Tracking | `{ tripId, actualDepartureTime }` |
| `trip.trip.completed` | Trip | Booking, Parcel, Payment (settlement eligibility) | `{ eventId, occurredAt, tripId, operatorId, terminalAt, completedAt, hasSubstitution }`; `completedAt` equals `terminalAt` and is retained as the Booking compatibility alias |
| `trip.trip.disrupted` | Trip | Booking, Parcel, Payment | Exact `{ eventId, occurredAt, tripId, operatorId, terminalAt, hasSubstitution, reason? }`; Booking and Parcel execute disruption recovery only when `hasSubstitution=false`, while Payment records settlement eligibility for either value. The event never carries a Trip-wide traveled ratio. |
| `trip.trip.cancelled` | Trip | Booking, Parcel | { eventId, occurredAt, tripId, operatorId, cancelledAt, cancelReason } |
| `trip.trip.vehicle_swapped` | Trip | Booking, Notification (crew only) | Exact `{ eventId,occurredAt,tripId,operatorId,oldVehicleId,newVehicleId,oldVehiclePlateNumber,newVehiclePlateNumber,departureDateTime,driverUserId,assistantUserId,seatImpacts:[{bookingId,seatNumbers,reason}] }`; `assistantUserId` present nullable, reasons exactly `SEAT_REMOVED\|SEAT_DISABLED\|SEAT_TYPE_DOWNGRADED` |
| `trip.trip.vehicle_substituted` | Trip | Booking, Parcel (Day 35) | Exact `{eventId,occurredAt,substitutionId,disruptedAt,operatorId,oldTripId,oldTripStatus,oldVehicleId,newTripId,newTripStatus,newVehicleId,newVehiclePlateNumber,newTripDepartureDateTime,actorUserId,reason,notifyPassengers,mappings:[{bookingId,passengerId,originalSeatNumber,newSeatNumber,originalBoardingStatus}]}`; exactly one fact per substitution; `occurredAt = disruptedAt`; `substitutionId = eventId`; `oldTripStatus=DISRUPTED`; `newTripStatus=BOARDING`; both `originalSeatNumber` and `newSeatNumber` are nullable; `originalBoardingStatus=BOARDED\|PENDING`; `payload.eventId == Outbox row id == RabbitMQ MessageId`. |
| `trip.trip.route_changed` | Trip | Booking, Notification | { eventId, occurredAt, tripId, operatorId, tripStatus, alternativeRouteId, affectedBookings } |
| `trip.route_change_proposal.created` | Trip | Notification, Tracking | Exact `{ eventId, occurredAt, proposalId, tripId, operatorId, proposedByUserId, actorUserId, proposalType, status, sourceAlternativeRouteId, approvedAlternativeRouteId, incidentId, reason, rejectionReason, resolutionCode, supersededByProposalId }`; nullable fields serialize as null; `actorUserId=proposedByUserId`, `status=PENDING`. Notification resolves every active `OPERATOR_ADMIN` plus `proposedByUserId`; Tracking emits `routeProposal:created` to the authenticated operator fleet room. |
| `trip.route_change_proposal.approved` | Trip | Notification | Exact `{ eventId, occurredAt, proposalId, tripId, operatorId, proposedByUserId, actorUserId, proposalType, status, sourceAlternativeRouteId, approvedAlternativeRouteId, incidentId, reason, rejectionReason, resolutionCode, supersededByProposalId }`; `actorUserId` is deciding `OPERATOR_ADMIN`, `status=APPROVED`, `approvedAlternativeRouteId` is non-null. Notification sends `ROUTE_CHANGE_PROPOSAL_APPROVED` to `proposedByUserId`; approval also emits canonical `trip.trip.route_changed` atomically. |
| `trip.route_change_proposal.rejected` | Trip | Notification | Exact `{ eventId, occurredAt, proposalId, tripId, operatorId, proposedByUserId, actorUserId, proposalType, status, sourceAlternativeRouteId, approvedAlternativeRouteId, incidentId, reason, rejectionReason, resolutionCode, supersededByProposalId }`; `actorUserId` is deciding `OPERATOR_ADMIN`, `status=REJECTED`; optional `rejectionReason` is trimmed/null-normalized. Notification sends `ROUTE_CHANGE_PROPOSAL_REJECTED` to `proposedByUserId`. |
| `trip.route_change_proposal.superseded` | Trip | Notification | Exact `{ eventId, occurredAt, proposalId, tripId, operatorId, proposedByUserId, actorUserId, proposalType, status, sourceAlternativeRouteId, approvedAlternativeRouteId, incidentId, reason, rejectionReason, resolutionCode, supersededByProposalId }`; `status=SUPERSEDED`; `resolutionCode=ANOTHER_PROPOSAL_APPROVED|ROUTE_CHANGED_DIRECTLY`; `supersededByProposalId` is winner id for approval and null for direct route change. Notification sends `ROUTE_CHANGE_PROPOSAL_SUPERSEDED` to `proposedByUserId`. |
| `trip.route_change_proposal.expired` | Trip | Notification | Exact `{ eventId, occurredAt, proposalId, tripId, operatorId, proposedByUserId, actorUserId, proposalType, status, sourceAlternativeRouteId, approvedAlternativeRouteId, incidentId, reason, rejectionReason, resolutionCode, supersededByProposalId }`; `status=EXPIRED`; `actorUserId=null`; `resolutionCode=TRIP_NO_LONGER_EDITABLE|SOURCE_ROUTE_CHANGED`. Notification sends `ROUTE_CHANGE_PROPOSAL_EXPIRED` to `proposedByUserId`. |
| `trip.trip.schedule_changed` | Trip | Booking, Notification (crew only) | Exact `{ eventId,occurredAt,tripId,operatorId,oldDeparture,newDeparture,severity }`, severity `MINOR\|MEDIUM\|MAJOR`; Booking owns passenger facts while Notification resolves current Trip crew only |
| `trip.stop.disabled` | Trip | Booking | Exact `{ eventId, occurredAt, eventType, stopId, operatorId, replacedByStopId? }`; `eventId == OutboxEvent.Id == RabbitMQ MessageId`. |
| `trip.station.merged` | Trip | Booking, Identity | `{ eventId, occurredAt, eventType, actorUserId, ipAddress?, userAgent?, primaryStationId, duplicateStationId, primaryBefore, duplicateBefore, primaryAfter, relinkedCounts }`; Station snapshots omit contact phone/email |
| `trip.station.normalized` | Trip | Identity | `{ eventId, occurredAt, eventType, actorUserId, ipAddress?, userAgent?, stationId, before, after }`; snapshots omit contact phone/email |
| `booking.stop_disabled.affected` | Booking | Notification | Exact `{ eventId, occurredAt, eventType, stopId, replacedByStopId?, recipientUserIds[], affectedBookingCount }`; explicit deduped recipients; identity equals Outbox/Rabbit MessageId. |
| `booking.booking.stop_disabled_auto_fallback_applied` | Booking | Notification | Exact `{ eventId, occurredAt, eventType, bookingId, tripId, userId, pendingActionId, disabledStopId, affectedField, fallbackStationId, resolvedAction }`; `affectedField=PICKUP|DROPOFF`, `resolvedAction=AUTO_FALLBACK_DESTINATION`; one fact per action. |
| `booking.booking.route_change_auto_fallback_applied` | Booking | Notification | Exact `{ eventId, occurredAt, eventType, bookingId, tripId, userId, pendingActionId, originalStopId, fallbackDestinationStationId, shuttleRequired, resolvedAction }`; `shuttleRequired=true`, `resolvedAction=AUTO_FALLBACK_DESTINATION`; one fact per timed-out ROUTE_CHANGE action. |
| `booking.booking.passenger_no_show_marked` | Booking | Notification | Exact `{ eventId, occurredAt, eventType, bookingId, tripId, userId, bookingStatus, newlyNoShowPassengerIds[], triggerType, pickupStopId? }`; status `NO_SHOW|PARTIAL_NO_SHOW`, trigger `ALONG_ROUTE|TERMINAL`; one fact per Booking transition. |
| `trip.stop.departed_with_pending` | Trip | Notification (Driver App boarding warning) | `{ eventId: Guid, occurredAt: DateTime (UTC), eventType: "trip.stop.departed_with_pending", tripId: Guid, stopId: Guid, stopName: string, pendingPassengerCount: int (> 0), driverUserId: Guid, assistantUserId: Guid?, departedAt: DateTimeOffset (UTC ISO-8601) }` |
| `trip.stop.arrived` | Trip | Notification | `{ eventId, occurredAt, eventType, tripId, stopId, operatorId, actorUserId, actualArrivalTime }`; Trip và TripStop lock theo thứ tự, `PENDING -> ARRIVED`, static ETA không đổi, business row + Outbox commit atomic; Parcel reads the Trip snapshot synchronously and has no arrival projection |
| `trip.destination.arrived` | Trip | — (no v1 consumer) | `{ eventId, occurredAt, eventType, tripId, destinationStationId, operatorId, actorUserId, actualArrivalTime }`; destination Station derive từ Route, anchor độc lập `completedAt`, express Trip zero-stop vẫn hợp lệ; event does not drive Parcel state |
| `trip.trip.delayed` | Tracking | Notification | `{ eventId, occurredAt, tripId, stopId, delayMinutes, etaNew, staticEstimatedArrivalTime, dynamicEstimatedArrivalTime, userIds? }`; one durable Outbox fact per `tripId/stopId/5-minute-window`, deduped by unique `dedupeKey`; Notification resolves active passengers and operator admins |
| `trip.incident.reported` | Trip | Notification | `{ eventId, occurredAt, incidentId, tripId, operatorId, reporterUserId, category, description?, photoUrls?, latitude?, longitude?, reportedAt }`; optional fields được omit khi null; Notification resolve active `OPERATOR_ADMIN` theo `operatorId` |
| `trip.cargo.threshold_crossed` | Trip | Notification | Exact `{ eventId, occurredAt, tripId, operatorId, loadedWeightKg, maxCargoWeightKg, percentFull }`; `eventId == OutboxEvent.id == RabbitMQ MessageId` |
| `trip.shuttle.assigned` | Trip | Notification | `{ eventId?, shuttleTripId, mainTripId, operatorId?, bookingId, passengerUserId, direction, ticketIds, pickupOrder, scheduledDepartureTime, scheduledEndTime, driver: { userId, displayName, phone }, vehicle: { id, licensePlate } }` |
| `trip.shuttle.warning_issued` | Trip | Notification | `{ mainTripId, operatorId, alertType: WARNING_120|WARNING_60, pendingBookingCount, pendingPassengerCount, hardCutoffAt }` |
| `trip.shuttle.unfulfilled` | Trip | Notification | `{ mainTripId, bookingId, passengerUserId, stationId, reason: AUTO_UNFULFILLED_CUTOFF }` |
| `trip.shuttle.cancelled` · `trip.shuttle.picked_up` · `trip.shuttle.delivered` · `trip.shuttle.no_show` · `trip.shuttle.completed` | Trip | Notification | Common `{ eventId, occurredAt, shuttleTripId?, mainTripId, operatorId, bookingId?, passengerUserId?, direction, serviceAddress?, serviceOrder?, status, roadDistanceMeters?, reason? }`; Notification dedupes by `eventId/MessageId`, sends only to the affected Passenger and same-tenant Operator recipients. |
| `tracking.gps.off_route` | Tracking | Notification | `{ eventId, occurredAt, tripId, durationSeconds }`; Notification resolves assigned driver, assistant, and operator admins |
| `tracking.gps.approaching_stop` | Tracking | Notification | `{ tripId, stopId, bookingIds, wave, etaMinutes }` |
| `payment.payment.succeeded` | Payment | Booking, Parcel | `{ eventId, occurredAt, paymentId, referenceType, referenceId, amount, method, paidAt, dueAt?, context }`; `paidAt` is authoritative `Payment.succeededAt`, not publish/consume time; context is the immutable server snapshot and may contain multiple allocations |
| `payment.payment.refunded` | Payment | Booking, Parcel | `{ eventId, occurredAt, paymentId, referenceType, referenceId, amount, context }` |
| `payment.payment.failed` | Payment | Booking, Parcel | `{ paymentId, referenceType, referenceId, reason }` |
| `payment.payment.expired` | Payment | Booking, Parcel | `{ paymentId, referenceType, referenceId }` |
| `payment.wallet.credited` | Payment | Payment (self-consume to mark the funding Payment REFUNDED), Booking (mark REFUNDED), Parcel (mark REFUNDED), Notification | `{ eventId, occurredAt, userId, amount, referenceType, referenceId, paymentId? }`; `paymentId` is optional for wire compatibility. The current `BOOKING_REFUND` producer populates it for both generic and exact refunds after resolving the correlated funding Payment; it is omitted only for legacy payloads and non-Booking refunds. |
| `payment.wallet.debited` | Payment | Notification | `{ eventId, occurredAt, userId, walletTransactionId?, amount, balanceAfter?, referenceType, referenceId }`; one fact per committed wallet ledger item |
| `payment.subscription.payment_succeeded` | Payment | Identity, Payment Invoice pipeline | `{ eventId, occurredAt, paymentId, upgradeAttemptId, operatorId, operatorSubscriptionId, planId, amount, method, planName, billingPeriod, periodFrom, periodTo, succeededAt, buyerSnapshot }`; WALLET and VNPay use one schema |
| `payment.subscription.payment_failed` | Payment | Identity | `{ eventId, occurredAt, paymentId, upgradeAttemptId, operatorId, operatorSubscriptionId, responseCode }`; đóng session, attempt còn retry được trước dueAt |
| `payment.subscription.payment_expired` | Payment | Identity | `{ eventId, occurredAt, paymentId, upgradeAttemptId, operatorId, operatorSubscriptionId }`; đóng session, không kéo dài attempt dueAt |
| `identity.operator.registration_submitted` | Identity | Notification (System Admin) | `{ eventId, occurredAt, operatorId, companyName }` |
| `identity.subscription.usage_warning` | Identity | Notification | `{ eventId, occurredAt, subscriptionId, operatorId, resource, periodKey, used, limit, usagePercent }`; emit once when crossing from below 80% to at least 80% per resource and period |
| `identity.subscription.trial_expiring` | Identity | Notification | `{ subscriptionId, operatorId, expiresAt, daysRemaining, occurredAt }` |
| `identity.subscription.expired` | Identity | Notification | `{ subscriptionId, operatorId, expiredAt, occurredAt }` |
| `identity.subscription.payment_auto_reverted` | Identity | Notification | `{ subscriptionId, operatorId, previousPlanId, restoredPlanId, occurredAt }` |
| `subscription.limit.trip_skipped` | Trip | Notification | `{ operatorId, driverScheduleId, skippedDate, periodKey, occurredAt }` |
| `payment.invoice.issued` | Payment | Notification | `{ eventId, occurredAt, invoiceId, invoiceNumber, operatorId, amount, invoiceWebUrl, downloadApiUrl }`; neither URL is a Firebase signed URL |
| `payment.trip_settlement.completed` | Payment | Notification (operator) | `{ eventId, occurredAt, settlementId, tripId, operatorId, netAmount, settlementMethod, settledAt }` |
| `parcel.parcel.created` | Parcel | Notification | `{ parcelId, tripId, senderUserId, recipientUserId? }`; cargo soft hold is an idempotent synchronous Trip mutation when deposit payment starts |
| `parcel.parcel.loaded` | Parcel | Notification, Trip (counter update) | Exact `{ eventId, occurredAt, parcelId, tripId, actualWeightKg, userIds[] }`; direct `userIds[]` contains the sender and recipient account when present; `eventId == OutboxEvent.id == RabbitMQ MessageId` |
| `parcel.parcel.unloaded` | Parcel | Notification | `{ parcelId, tripId, userIds[] }`; chỉ CAS `IN_TRANSIT -> UNLOADED` thắng mới enqueue, `userIds` distinct gồm sender và recipient account nếu có |
| `parcel.parcel.delivered_pending_confirm` | Parcel | Notification | Exact `{ eventId, occurredAt, parcelId, parcelCode, operatorId, tripId, userId?, recipientUserIds[]?, expiresAt? }`; `eventId == Outbox row id == RabbitMQ MessageId`. Recipient fields are omitted when no account is linked, and `expiresAt` is omitted when no recipient email/token exists. Raw tokens and delivery URLs are forbidden; Parcel sends the recipient email through `POST /internal/v1/emails`. |
| `parcel.parcel.delivery_confirmation_realerted` | Parcel | Notification (operator) | Exact `{ eventId, occurredAt, parcelId, parcelCode, operatorId, tripId, expiredAt }`; emitted only after the active confirmation token has been expired for at least seven days. It does not rotate the token or change Parcel status. |
| `parcel.parcel.pending_operator_action_realerted` | Parcel | Notification (operator) | Exact `{ eventId, occurredAt, parcelId, parcelCode, operatorId, userId, tripId }`; `userId` is always the persisted Parcel sender. The 2-hour recovery reminder is replay-safe and contains no token or mutable recipient secret. |
| `parcel.parcel.delivery_confirmed` | Parcel | Notification | `{ parcelId }` |
| `parcel.parcel.delivery_rejected` | Parcel | Notification | `{ parcelId, reason }` |
| `parcel.parcel.cancelled` · `parcel.parcel.rejected` · `parcel.parcel.returned` | Parcel | Notification | Canonical Day-32 terminal notification fact `{eventId,occurredAt,parcelId,parcelCode,operatorId,userId,tripId,refundAmount,reason}`. Trip cargo is mutated synchronously through its idempotent internal API; Payment MUST NOT bind these canonical facts. Exact legacy payloads without canonical identity remain accepted for one release only and normalize to the same deterministic Parcel reference identity described below. |
| `parcel.parcel.auto_rejected` | Parcel | Notification, Trip (counter), Payment (refund) | Legacy exact `{ eventId, occurredAt, parcelId, parcelCode, operatorId, userId, tripId, refundAmount }` remains accepted for late-load/additional-payment producers. Settlement v2 exact variant adds `reason: CHECK_IN_TIMEOUT\|FINAL_PAYMENT_TIMEOUT` and `forfeitedDepositVnd`; `userId` is always the persisted sender and `eventId == OutboxEvent.id == RabbitMQ MessageId` |
| `parcel.parcel.review_requested` | Parcel legacy | Notification (operator) | `{ parcelId, operatorId }`; không phát cho Parcel mới, kể cả `EXTRA_LARGE` |
| `parcel.parcel.review_approved` | Parcel legacy | Notification (sender) | Exact `{ eventId, occurredAt, parcelId, parcelCode, operatorId, userId, depositRequiredVnd }`; enqueue trong cùng transaction chuyển record legacy sang `PENDING_PAYMENT` |
| `parcel.parcel.final_payment_requested` | Parcel | Notification (sender) | Exact `{ eventId, occurredAt, parcelId, parcelCode, operatorId, userId, tripId, balanceRequiredVnd, balancePaidVnd, finalPaymentDeadline }`; enqueue trong cùng transaction cân lại chuyển sang `PENDING_FINAL_PAYMENT` |
| `parcel.parcel.settlement_recovered` | Parcel | Notification (sender) | Exact `{ eventId, occurredAt, parcelId, parcelCode, userId, tripId, recoveredStatus: READY_TO_LOAD\|CANCELLED, refundAmountVnd }`; corrective fact khi callback có `paidAt` đúng hạn thắng timeout đã phát trước đó |
| `parcel.parcel.transfer_initiated` | Parcel | Notification | `{ parcelId, originalTripId, newTripId }` |
| `parcel.refund.initiated` | Parcel | Payment | Exact `{ eventId, occurredAt, parcelId, senderUserId, amount, referenceType:"PARCEL_REFUND", referenceId, reason, idempotencyKey }`; sole authoritative Day-32 refund trigger. `referenceId=parcelId`. Allowed `reason` is `TRIP_CANCELLED_PRE_LOAD`, `MANUAL_CANCEL_FULL`, `MANUAL_CANCEL_POLICY`, `OPERATOR_RETURN`, or `TRIP_DISRUPTED_PRE_LOAD`. `idempotencyKey` is the stable UUID-v4 derived once per Parcel + source event/request + refund phase and is reused on retry. Payment dedupes the one-release legacy terminal refund and this canonical fact by `(referenceType,referenceId,idempotencyKey)`, so a partial and later final refund remain distinct while the same phase can create at most one wallet credit/ledger entry. |

**Cancellation event compatibility:** A canonical `booking.booking.cancelled` producer creates a
fresh UUID-v4 `eventId` and captures offset-date-time `occurredAt`. One-release consumers accept
only the complete canonical shape in the registry or the exact legacy shape with both identity
fields absent, reject partial/malformed/extra fields, and fall back to `bookingId` only for that
exact legacy payload. Optional `bookingCode`, `ticketCodes`, and `ticketCount` enrich
notifications but do not alter refund authority.

Payment attempts a positive Booking refund once while consuming the cancellation fact. On failure,
it acknowledges only after persisting one unresolved `RefundFailureLog` with `retryCount=0`; the
recurring `RefundFailureRetryJob` then owns at most five attempts at the §10.1 ten-minute cadence.
An exhausted row remains unresolved for Admin handling. Refund failure never rolls back the
already-committed Trip or Booking cancellation.

An exact `booking.payment_refund.requested` failure is persisted with
`referenceType=BOOKING_REFUND_PAYMENT` and `referenceId=paymentId`; unlike generic refund rows,
its authoritative `amount` may be zero when the captured allocation is fully voucher-funded.
Retry reconstructs the same exact-payment command so the required VietRide-funded voucher
ledger reversal is not lost.

**Day-22 ownership:** For Day-22 vehicle swap, schedule change, and schedule-day-removal
cancellation only, Trip emits domain facts while Booking owns passenger-impact state and passenger-
notification facts. This scoped rule does not replace or alter the existing
`trip.trip.route_changed` registry/consumer behavior. Notification consumes
`trip.trip.schedule_changed` only to notify current crew and never uses that Trip fact for
passengers; it still does not consume `trip.trip.cancelled` directly. The vehicle-swapped Trip fact
targets crew only. For passenger schedule changes, only `CONFIRMED` Bookings emit a Booking schedule fact:
MINOR emits `booking.booking.schedule_change_informational`, MEDIUM/MAJOR emit
`booking.booking.schedule_change_required`, and every other Booking status emits neither. On
Day-22 day-removal cancellation, Booking cancels active rows and emits existing
`booking.booking.cancelled`: `PENDING_PAYMENT` uses `refundAmount=0`; `CONFIRMED` uses a 100%
refund of immutable persisted `Booking.totalAmount`. Payment refunds only from that Booking fact,
preventing double refunds. Parcel independently consumes Trip cancellation. Day 22 owns fact
publication, pending-action creation, and T+2h re-alert. Day 23 owns passenger accept/reject and
scheduled resolution: passenger rejection may refund by severity, while timeout only auto-accepts
and never cancels or refunds.

**Canonical `trip.trip.route_changed` payload:**

```json
{
  "eventId": "00000000-0000-4000-8000-000000000041",
  "occurredAt": "2026-07-23T01:00:00Z",
  "tripId": "00000000-0000-4000-8000-000000000033",
  "operatorId": "00000000-0000-4000-8000-000000000001",
  "tripStatus": "IN_PROGRESS",
  "alternativeRouteId": "00000000-0000-4000-8000-000000000036",
  "affectedBookings": [
    {
      "bookingId": "00000000-0000-4000-8000-000000000034",
      "candidateStops": [
        {
          "stopId": "00000000-0000-4000-8000-000000000037",
          "stationId": null,
          "stationName": "Alternative stop",
          "sequence": 1,
          "estimatedArrivalAt": "2026-07-23T01:45:00Z"
        },
        {
          "stopId": null,
          "stationId": "00000000-0000-4000-8000-000000000038",
          "stationName": "Destination station",
          "sequence": 2,
          "estimatedArrivalAt": "2026-07-23T04:50:00Z"
        }
      ]
    }
  ]
}
```

The top-level fields are exact; `affectedBookingIds` is forbidden. `tripStatus` is exactly
`SCHEDULED|BOARDING|IN_PROGRESS` at the route-change transition. `affectedBookings` is ordered by
`bookingId`; every immutable `candidateStops` array is ordered by `sequence` and contains exactly
`{stopId,stationId,stationName,sequence,estimatedArrivalAt}`. Candidate identity is XOR: an
intermediate `AlternativeRouteStop` has `stopId` and null `stationId`; the appended destination
Station has null `stopId` and `stationId`. Trip snapshots names and ETA locally from the selected
AlternativeRoute in the same transaction as the mutation/Outbox write. ETA uses
`actualDepartureTime ?? departureDateTime` plus each stop's duration from origin; the destination
uses `AlternativeRoute.estimatedDurationMinutes`, falling back to the final intermediate duration
or zero. There is no cross-DB FK and no synchronous consumer lookup.

**Route-change proposal lifecycle:** assigned `DRIVER|ASSISTANT` may create `EXISTING|CUSTOM`
snapshot proposals only while the Trip is `SCHEDULED|BOARDING|IN_PROGRESS`; multiple pending rows
per Trip are permitted. `OPERATOR_ADMIN` owns tenant-scoped list/detail/approve/reject. Approval
promotes CUSTOM into an official AlternativeRoute when needed, applies the route, and supersedes
all other pending rows atomically. Direct admin change-route remains supported and supersedes all
pending rows. Source mutation/deactivation or terminal Trip transitions expire pending rows.
The public CUSTOM body property is `route`; its `pathPolyline` is required and reuses the canonical
Google precision-5 geometry/500-metre waypoint validator. Its destination Station must have an
active OperatorStation mapping for the proposal tenant, and all Stops must be active and owned by
that tenant. The public reject body property is optional `reason`; the persisted/response/event
field remains `rejectionReason`.
All creation, approval, source-write, direct-change, and terminal-transition paths use one fixed
PostgreSQL transaction lock protocol: source-referencing paths first acquire the transaction-scoped
AlternativeRoute advisory lock, then lock `Trip`, pending proposal rows by UUID ascending, and the
required AlternativeRoute/Station/OperatorStation/Stop dependency rows in deterministic order.
Proposal creation runs this full protocol, audit write, and Outbox enqueue in one transaction.

Notification consumes all five `trip.route_change_proposal.*` facts. Created resolves all active
`OPERATOR_ADMIN` recipients by `operatorId` plus `proposedByUserId`. Each terminal fact resolves
the current assigned Driver/Assistant from Trip and adds `proposedByUserId`; duplicate users are
collapsed before persistence and FCM enqueue.
The exact type mapping is
`created→ROUTE_CHANGE_PROPOSAL_CREATED`, `approved→ROUTE_CHANGE_PROPOSAL_APPROVED`,
`rejected→ROUTE_CHANGE_PROPOSAL_REJECTED`, `superseded→ROUTE_CHANGE_PROPOSAL_SUPERSEDED`, and
`expired→ROUTE_CHANGE_PROPOSAL_EXPIRED`. Consumer dedupe uses `eventId`, which equals Outbox id and
RabbitMQ `MessageId`; replay/redelivery creates no duplicate Notification or push. These operational
notifications are additive; passenger messaging remains owned by existing `trip.trip.route_changed`
→ Booking pending-action facts and the existing `TRIP_ROUTE_CHANGED` mapping.

Day-22 schedule-day removal sets `trip.trip.cancelled.cancelReason` to
`DRIVER_SCHEDULE_DAY_REMOVED` and `cancelledAt=occurredAt`. Payment does not consume the terminal
Trip cancellation fact directly; all Booking refunds remain driven only by
`booking.booking.cancelled`.

**Day-23 schedule-change ownership:** PATCH
`/v1/operator/driver-schedules/{scheduleId}?applyTo=FUTURE_ONLY|ALL_PENDING` remains the sole
producer; there is no dedicated Trip schedule endpoint or Gateway route. Booking updates the
mutable current-departure projection for `PENDING_PAYMENT|CONFIRMED`, but only `CONFIRMED` emits
the informational/required passenger facts or holds one active action. Passenger resolution uses
POST `/v1/bookings/{bookingId}/pending-actions/{actionId}/resolve`; a reject transaction emits the
single authoritative `booking.booking.cancelled` carrying frozen `refundAmount`. Terminal MEDIUM
and MAJOR scheduled acceptance emits `booking.booking.pending_action_auto_resolved`. Notification
maps required, re-alerted, and auto-resolved facts to existing `TRIP_SCHEDULE_CHANGED`, deduped by
MessageId; it needs no new notification type or persistence schema.

### 7.4 Outbox Pattern (durability cho publish event)

Mỗi service publish event đều có bảng `outbox_events`:

```
outbox_events:
  id UUID PK
  event_type TEXT (= routing key)
  payload JSONB
  status ENUM(PENDING, PUBLISHING, PUBLISHED, FAILED)
  retry_count INT DEFAULT 0
  last_error TEXT NULL
  created_at TIMESTAMPTZ DEFAULT now()
  published_at TIMESTAMPTZ NULL
  -- index: (status, created_at) partial WHERE status IN ('PENDING','FAILED')
```

**Publisher process:**

- **.NET services:** `IHostedService` / `BackgroundService` poll mỗi 5s (KHÔNG dùng Hangfire cho Outbox).
- **NestJS services có Outbox** (Tracking, RAG): BullMQ scheduled job poll mỗi 5s.
- **Notification Service KHÔNG có Outbox** (chỉ consume).

**Write flow:**

```
BEGIN TRANSACTION
  UPDATE Booking SET status = 'CONFIRMED' ...
  INSERT outbox_events { eventType: 'booking.booking.confirmed', payload: {...}, status: PENDING }
COMMIT
```

For every Day-23 and Day-24 integration event, the producer allocates the UUID before serialization
and uses one identity end to end: `payload.eventId == outbox_events.id == RabbitMQ MessageId`.
Retries reuse
that row/id; they never generate a new payload identity. Fresh `booking.booking.cancelled`
producers always write both UUID-v4 `eventId` and offset-date-time `occurredAt` together. The
temporary legacy consumer branch is permitted only for the exact old payload with both fields
absent; partial identity presence is invalid.

A Day-24 crash/restart after local commit reuses the persisted Outbox id and payload; publisher
retry never allocates a new EventId. Consumers dedupe by RabbitMQ MessageId/EventId before state or
notification side effects, so redelivery/restart creates no duplicate action, transition,
event-derived notification, or refund.

Day-34 Trip substitution writes the old/replacement Trip, replacement seats/stops, audit, canonical
`trip.trip.vehicle_substituted`, and canonical `trip.trip.disrupted {hasSubstitution:true}` rows in
one Trip-local transaction. The two facts have distinct EventIds. Booking applies each substitution
in one Booking-local transaction, updates eligible Bookings/Passengers, creates transfer rows, and
enqueues one `booking.booking.transferred` per eligible Booking. For every Day-34 fact independently,
`payload.eventId == outbox_events.id == RabbitMQ MessageId`; publisher retry/restart preserves both
that identity and the exact routing key.

**Publisher flow:**

```
SELECT * FROM outbox_events WHERE status IN ('PENDING','FAILED') ORDER BY created_at LIMIT 50
For each row:
  UPDATE status = 'PUBLISHING'
  publish to RabbitMQ
  on success: UPDATE status = 'PUBLISHED', published_at = now()
  on fail:    UPDATE status = 'FAILED', retry_count += 1, last_error = ...
After retry_count >= 10: alert Sentry, leave FAILED for manual handle
```

### 7.5 Compensation pattern

| Scenario | Compensation |
|---|---|
| Payment fail in checkout | HTTP `POST /internal/v1/trips/{id}/release-seats` (sync) |
| Authoritative Payment deadline | Hangfire job (Booking Service) → release seats + Booking → EXPIRED; verified capture is refund-only |
| Refund event consume fail | `RefundFailureLog` + Hangfire retry max 5 lần · alert Admin sau exhausted |
| Wallet credit fail | Same as above (RefundFailureLog) |

### 7.6 HTTP client conventions

- **.NET:** `HttpClient` typed registered ở `Infrastructure/Http/` qua `services.AddHttpClient<ITripServiceClient, TripServiceClient>()`.
- Wrap call qua **Polly** policy:
  - Retry: 3 lần, exponential backoff (200ms, 500ms, 1s) — chỉ retry transient (5xx, network).
  - Circuit breaker: 5 failures trong 30s → open 30s.
  - Timeout: 5s per request.
- Mỗi call kèm header `X-Internal-Auth` + `X-Request-Id` propagated.
- **NestJS:** Axios typed client, same Polly equivalent qua `axios-retry` + circuit breaker `opossum`.

**Day-19 Identity phone lookup boundary (Booking):** Booking validates and normalizes the public phone with `PhoneNumber.Normalize` before sending a URI-escaped canonical E.164 value. Retry remains limited to transient 5xx/network failures; 4xx is never retried. Only an Identity HTTP 404 whose ADR 0004 body has `error.code = RESOURCE_NOT_FOUND` is the expected no-match result. Caller-request cancellation propagates unchanged. Identity 401/403, every other or malformed 4xx response, 5xx after policy handling, timeout, circuit-open, transport, and response-deserialization failures are dependency failures and must become FE-facing HTTP 502 `UPSTREAM_UNAVAILABLE`; they must not be reported as caller authorization failures or empty results.

### 7.7 Day-40 Station canonicalization và platform reports

#### Trip Station normalize/merge

`PATCH /v1/admin/stations/{id}` giữ toàn bộ request hiện hữu (`name`, address/location,
city/ward, coordinate pair, contact, operating hours, facilities, shuttle flag, active flag)
và deterministic slug từ `name+city+ward`; collision dùng station-ID hash suffix, không thêm
`STATION_SLUG_CONFLICT`. Station đã merged không được normalize. Update và
`trip.station.normalized` Outbox commit cùng transaction.

`POST /v1/admin/stations/{primaryStationId}/merge` lock hai Station theo UUID ascending và recheck
precondition. Primary thắng `name,slug,city,ward`; `addressStreet,locationId,contactPhone,
contactEmail,operatingHours,facilities` chỉ fill từ duplicate khi primary null; coordinates là một
cặp; `supportsShuttle` dùng OR. Cùng một Trip DB transaction relink
`OperatorStation.stationId`, Route origin/destination, AlternativeRoute destination,
`ShuttleTrip.stationId` và mọi redirect cũ đang trỏ duplicate. OperatorStation collision giữ row
primary, OR `isActive`, fill nullable config rồi xóa duplicate mapping. Preflight từ chối Route
origin=destination/domain violation bằng `409 STATION_MERGE_CONFLICT`, không partial write/Outbox.
Duplicate được `isActive=false`, soft-delete và set `merged_into_station_id=primaryId`; redirect cũ
được flatten trực tiếp. Self-FK dùng `ON DELETE RESTRICT`, check khác chính nó và partial redirect
index. Outbox `trip.station.merged` nằm cùng transaction.

Internal Station lookup phân biệt: canonical active trả `200 isMerged=false`; soft-deleted do merge
trả original identity cùng `isMerged=true,canonicalStationId`; ordinary soft-delete hoặc missing trả
`404 STATION_NOT_FOUND`. Public lookup/search không expose deleted Station.

#### Booking durable Station redirect

Booking lưu redirect và processed marker trong một bảng, không cross-DB FK:

```text
booking_station_redirects
  duplicate_station_id UUID PRIMARY KEY
  canonical_station_id UUID NOT NULL
  source_event_id UUID NOT NULL UNIQUE
  occurred_at TIMESTAMPTZ NOT NULL
  created_at TIMESTAMPTZ NOT NULL
  updated_at TIMESTAMPTZ NOT NULL
  CHECK (duplicate_station_id <> canonical_station_id)
  INDEX (canonical_station_id)
```

Queue `booking.station-merged` là durable. Replay cùng `source_event_id` ACK; cùng duplicate nhưng
khác event/target là poison conflict và không mark processed. Resolver follow tối đa 32 hop với
visited set; cycle/self/overflow rollback để retry/DLQ. Event out-of-order vẫn flatten mọi alias
trực tiếp tới terminal canonical, giữ `source_event_id` gốc của row cũ.

Mọi writer và consumer dùng PostgreSQL transaction advisory lock
`pg_advisory_xact_lock(hashtextextended('booking-station:' || stationId::text, 0))`; UUID format `D`
lowercase được sort ordinal ascending. Consumer pre-read graph, lock union primary/duplicate/path và
alias, re-read dưới lock; graph phát sinh ID mới thì rollback/retry tối đa ba lần rồi NACK transient.
Trong transaction ổn định, upsert redirect, flatten alias và relink Booking
`PENDING_PAYMENT|CONFIRMED` cùng nhau; terminal/historical Booking không đổi.

`CreateBooking`, `CreateRoundTripBooking`, `EditPickup`, `EditDropoff` lock union Station ID từ
request, Booking hiện tại và fresh Trip snapshot, canonicalize cả request lẫn snapshot trước mọi
equality/domain validation. Edit còn lock/reload Booking row `FOR UPDATE`; không mutate tracked
snapshot stale. Lock chung đóng race: writer-first thì consumer relink row vừa commit;
consumer-first thì writer persist canonical ID. Sau cả hai commit không còn active Booking trỏ
duplicate. Consumer không phát Payment/refund event.

Identity consume hai Station events trên durable queue, insert `STATION_MERGED` hoặc
`STATION_NORMALIZED` với `user_id=actorUserId`, `source_event_id=eventId`; IP/user-agent vào cột audit
riêng, không metadata. Insert và marker atomic, duplicate no-op/ACK; missing actor retry/DLQ. Logs
vận hành không in full payload, contact, IP hoặc user-agent.

#### Earned platform report

Booking sở hữu public facade `GET /v1/admin/reports/platform?from=&to=` từ Day 42; Payment không
đọc foreign DB và vẫn là authoritative ledger source. Public `from/to` là ngày Asia/Ho_Chi_Minh inclusive theo
`YYYY-MM-DD`, `from <= to`, tối đa 366 ngày. Booking chuẩn hóa chúng thành UTC half-open
`[fromUtc,toUtcExclusive)` trước khi query/call ba source vận hành cung cấp metrics count:

- Booking: `COMPLETED`, anchor `completed_at`, count. Doanh thu cuối cùng không lấy từ amount của
  Booking mà lấy từ Payment ledger.
- Trip: `COMPLETED`, anchor `completed_at`, count.
- Parcel: `DELIVERY_CONFIRMED`, anchor `confirmed_at`, count. Doanh thu cuối cùng không lấy từ amount
  vận hành của Parcel mà lấy từ Payment ledger.

Earned live vẫn là anchor cho count vận hành; không dùng payment-ledger time, non-terminal row hoặc
Stats/cache chưa reconciliation làm nguồn count. Payment ledger là authority cuối cùng cho public
`netTicketRevenueVnd`/`netParcelRevenueVnd`; raw internal platform-ledger DTO tạm giữ tên legacy
`bookingRevenueVnd`/`parcelRevenueVnd`. Paid Booking `NO_SHOW` có thể cộng revenue ledger nhưng
`completedBookingCount` vẫn bằng 0. Source PostgreSQL đọc `SUM(BIGINT)` dưới dạng NUMERIC rồi
checked-convert từng group và total về Int64. Booking checked mọi count/totals, union operator IDs
cùng Payment ledger, lookup Identity theo chunk 500, giữ missing operator với tên null, sort net
revenue giảm dần rồi operator ID. Totals phải bằng sum `byOperator`; public
`netTicketRevenueVnd`/`netParcelRevenueVnd`/`netTransportRevenueVnd` có thể âm và không clamp.

Booking gọi bốn nguồn Trip/Parcel/Payment-ledger và Booking local song song với timeout 5 giây,
sau đó mới lookup Identity. Canonical upstream
`500 REPORT_VALUE_OVERFLOW` được propagate cùng code; timeout/5xx khác/payload unusable thành
`503 UPSTREAM_UNAVAILABLE`; source unavailable, ledger malformed/duplicate hoặc source-local
live/projection mismatch cũng trả `503`. Chênh lệch operational amount với Payment ledger không
phải mismatch vì Payment là authority cuối cùng. Không partial/stale response hoặc Payment DB write.
Chỉ kết quả đã reconciliation mới được ghi Redis cache. Partial indexes:

```text
Booking (completed_at, operator_id) WHERE status='COMPLETED' AND completed_at IS NOT NULL
Trip    (completed_at, operator_id) WHERE status='COMPLETED' AND completed_at IS NOT NULL
Parcel  (confirmed_at, operator_id) WHERE status='DELIVERY_CONFIRMED' AND confirmed_at IS NOT NULL
```

Day 40 là live indexed-report baseline. Day 42 materializes/validates Stats, đối chiếu projection
với live operational source trong chính từng service, rồi promote Booking-owned Redis hot read sau
reconciliation thành công. Redis TTL là 60 giây; exact UTC range và `platform-report:v3` là một
phần của key. Day 41 sở hữu sáu operator XLSX route và ClosedXML writer; không cross-DB query hoặc
Payment attribution table mới.

Mỗi nguồn Day 42 có projection per-earned-record riêng trong DB của service:
`platform_booking_stats`, `platform_trip_stats`, `platform_parcel_stats`. Projection lưu source ID,
operator, earned timestamp và revenue tương ứng; trigger đồng bộ nó trong cùng transaction với
Booking/Trip/Parcel. Các recurring job `booking|trip|parcel.platform-stats-backfill` chạy mỗi năm
phút và rebuild idempotent từ bảng live để sửa drift. Mỗi internal source query đối chiếu live với
projection theo từng operator và exact UTC range trước khi trả dữ liệu; source-local mismatch ghi
structured log, trả `503 UPSTREAM_UNAVAILABLE` và không được cache. `projected_at` là freshness
marker vận hành, nhưng timestamp mới không được dùng thay cho đối chiếu giá trị thực. Ledger-only
revenue không bị coi là source mismatch; ledger malformed/duplicate vẫn trả `503`.

#### Day 43 reliability contract

All Outbox publishers, including Tracking, transition an exhausted event to a per-service durable
DLQ after the sixth failed publish (`retry_count > 5`). The terminal row preserves event identity,
type, payload, retry count, last error, created time and terminal time, and is unique by event id.
Identity owns the `SYSTEM_ADMIN` read-only aggregate facade `GET /v1/admin/outbox/dlq`; it uses an
opaque composite cursor and reports unavailable source services without inventing totals. No
replay or purge is implemented in v1. Every Hangfire-owning service exposes the internal JWT-only
`GET /internal/jobs/status`; lag is `max(0, nowUtc - nextRunUtc)` or null when no next run exists.

#### Day 36/43 payment and FE-gap repair contract

- Payment expiration uses `effectiveDueAt = DueAt ?? CreatedAt + 15 minutes` and the inclusive
  `effectiveDueAt <= now` boundary. The expiry repository performs one atomic CAS from
  `PENDING_REDIRECT`; status and `payment.payment.expired` Outbox commit together. No new index or
  migration is part of this repair.
- Booking VNPay `DueAt` is the exact Trip seat-lock `ExpiresAt`; round-trip uses the earlier leg.
  Parcel keeps its own deposit/final deadlines. The 15-minute configuration is legacy fallback and
  never extends a 10-minute Booking seat lock.
- VNPay IPN verifies signature, merchant, amount, transaction status and signed `vnp_PayDate`
  before mutation, then reloads/locks the exact Payment. A capture is recorded once even if expiry
  won first. Expired Booking is never resurrected; `paidAt >= effectiveDueAt`, already-expired
  Booking, or definitive seat loss emits one `booking.payment_refund.requested` per allocation.
  Transient Trip/network/5xx failures retry instead of expiring/refunding.
- Payment revalidates captured VNPay Payment, owner, original reference and immutable trusted
  allocation. One-way Payment becomes `REFUNDED` after exact refund. `BOOKING_GROUP` becomes
  `REFUNDED` only when every allocation has exact matching `BOOKING_REFUND` credits; historical
  partial refunds do not satisfy the group.
- Redirect lookup selects latest Payment by `created_at DESC, id DESC` before eligibility and
  never falls back. It accepts only exact owner/context/amount, `VNPAY`, `PENDING_REDIRECT`,
  persisted `due_at > now`, and absolute credential-free HTTPS URL whose authority exactly matches
  configured VNPay base URI. Signed URL/query/body logging is forbidden.
- Booking/Passenger History always serialize nullable root `paymentRedirectUrl`; enrichment is
  fail-open. Ticket forwards Booking's URL. Parcel enriches only exact deposit
  `PENDING_PAYMENT/PARCEL` or final `PENDING_FINAL_PAYMENT/PARCEL_ADDITIONAL` candidates and does
  not expose settlement internals through `/v1/parcels/sent`.

#### UI gap contract freeze (2026-07-29)

This freeze is additive and supersedes the UI-gap backlog wherever that backlog conflicts with
the current service ownership or schema. It does not authorize frontend work, Admin Station work,
new dependencies, cross-database foreign keys, or enrichment of the existing public
`GET /v1/admin/operators` response.

- Booking owns Admin Dashboard and BookingStats. Payment owns Admin/Operator Revenue Analytics.
  The Booking Dashboard facade combines Booking-local aggregates with Identity internal metrics;
  it does not depend on the Platform Report facade.
- RAG owns generic `Policy` and immutable `PolicyAuditLog` Prisma aggregates. They are distinct
  from `KnowledgeDocument` and from Identity's operator cancellation/luggage/no-show JSON.
- Trip exposes a tenant-scoped `GET /v1/operator/trips` selector for `OPERATOR_ADMIN`. Search is
  limited to normalized vehicle plate and route name. No `tripCode`, `routeCode`, schema migration
  or search index is authorized unless a later measured query plan establishes a separate task.
- Parcel fare batch preserves the physical `(route_id, size_category)` key and atomically mutates
  that current row after checking the route belongs to the JWT operator; it does not introduce
  fare history. Existing single-size endpoints remain compatible.
- Cross-service display snapshots are nullable schema additions. EF/Prisma migrations never call
  another service. Historical fill is an idempotent application backfill with bounded batch calls,
  and reads retain a bounded internal-API fallback until fill completes.
- `parcel_status_history` is immutable and trigger-backed so EF bulk updates and raw SQL cannot
  bypass it. The trigger fires only when `OLD.status IS DISTINCT FROM NEW.status`; legacy rows get
  one `MIGRATION_BASELINE` record, never fabricated transitions.
- Dashboard `activeUsers` means the account's current latest `last_login_at` falls in the requested
  period; it is not a full login-history metric. `activeOperators` means currently
  `APPROVED + is_active` and having at least one BookingStats booking in the period.
- Payment là financial source of truth cho mọi Dashboard, Revenue Analytics, Platform Report và
  export có tiền. Booking/Trip/Parcel chỉ sở hữu count vận hành; không được fallback financial về
  BookingStats/ParcelStats khi Payment lỗi.
- Canonical revenue predicate chỉ nhận Booking/Parcel revenue, refund,
  `VOUCHER_VIETRIDE_FUNDED_CREDIT` đúng reference và typed
  `VIETRIDE_FUNDED_VOUCHER_REVERSAL`. `note` chỉ để audit, không quyết định category. Manual,
  generic entitlement, legacy unclassified, operator-funded audit và reference khác đều bị loại.
- `netTransportRevenueVnd = netTicketRevenueVnd + netParcelRevenueVnd`;
  `totalProjectRevenueVnd = netTransportRevenueVnd + subscriptionRevenueVnd`.
  `subscriptionRevenueVnd` chỉ gồm SUBSCRIPTION Payment `SUCCEEDED` theo `succeeded_at`; v1 không
  có subscription refund/proration. `paidToOperatorsVnd` là settlement cash-flow độc lập, không
  thuộc revenue.
- VietRide-funded voucher credit là operator entitlement/KPI quản trị, không phải passenger cash;
  các revenue API không phải báo cáo kế toán pháp lý. Refund theo kỳ `created_at` có thể làm tháng
  âm và không được clamp.
- For comparisons, previous=0/current=0 trả `0/FLAT`; previous=0/current khác 0 trả
  `changePercent=null` và trend theo dấu current. Input/bucket theo lịch
  `Asia/Ho_Chi_Minh`, nhưng mọi persistence filter và internal service range phải được chuẩn hóa
  thành UTC half-open `[fromUtc,toUtcExclusive)` trước khi query.
- All UI-gap public mutations require `Idempotency-Key`; all public responses use ADR 0004. Internal APIs
  require Internal JWT, return raw DTOs on success, and are never exposed through Gateway.

#### Unified financial reporting contract (2026-08-07)

Canonical adjustment taxonomy:

| Reason | DB semantics | Revenue |
|---|---|---|
| `VIETRIDE_FUNDED_VOUCHER_REVERSAL` | amount âm, reference `BOOKING|PARCEL` | recognized theo reference |
| `GENERIC_BOOKING_REFUND_ENTITLEMENT` | amount 0, reference `BOOKING` | marker entitlement, bị loại |
| `MANUAL_WALLET_ADJUSTMENT` | amount khác 0, reference `MANUAL` | bị loại |
| `LEGACY_UNCLASSIFIED` | chỉ dữ liệu lịch sử, application không tạo mới | bị loại và phải audit trước enforce rollout |

`ADJUSTMENT` luôn có reason; non-adjustment luôn null. DB CHECK enforce presence và semantics;
`note` không được dùng làm predicate. Canonical ledger revenue dùng `created_at`; subscription
`SUCCEEDED` dùng `succeeded_at`; settlement `SETTLED` dùng `settled_at`.

Payment expose raw Internal-JWT-only
`GET /internal/v1/revenue/admin-summary` và
`GET /internal/v1/revenue/operators/{operatorId}/summary`; không route qua Gateway. Booking dùng
admin summary cho Dashboard, Payment ledger cho Platform Report; Parcel dùng operator summary cho
summary/legacy CSV. Financial downstream timeout tổng 5 giây, retry tối đa một lần với GET
transient, circuit 5 failed operations/open 30 giây/half-open một probe. Lỗi hoặc malformed trả
`503 UPSTREAM_UNAVAILABLE`, không dùng local financial fallback.

Financial cache key phải versioned theo scope/operator/range và TTL tối đa 60 giây. Booking
Platform Report dùng `platform-report:v3`; Payment analytics/internal summaries dùng `revenue:v2`.
Parcel không đặt cache full response thứ hai. `generatedAt` là UTC; support/QA phải chấp nhận độ
trễ hiển thị tối đa 60 giây sau giao dịch mới.

---

## 8. Status Machines

> **Canonical định nghĩa:** technical_context_v7 Section 8 "Enum cần define khi thiết kế DB". Section dưới đây chỉ visualize lại để agent dễ tra.

### 8.1 BookingStatus

```
PENDING_PAYMENT ─┬─→ CONFIRMED ─┬─→ COMPLETED
                 │              ├─→ NO_SHOW
                 │              ├─→ PARTIAL_NO_SHOW ─┬─→ COMPLETED
                 │              │                    └─→ DISRUPTED ─→ REFUNDED
                 │              ├─→ CANCELLED ─→ REFUNDED   (operator hoặc user hủy)
                 │              └─→ DISRUPTED ─→ REFUNDED   (Trip DISRUPTED, no substitution)
                 │
                 └─→ EXPIRED   (authoritative Payment deadline; capture sau đó được refund)
```

**Triggers:**

- `CONFIRMED`: Payment Service publish `payment.payment.succeeded` → Booking Service consume.
- `EXPIRED`: Hangfire khi authoritative Payment deadline `<= now`; Booking không được hồi sinh sau
  seat release.
- `COMPLETED`: Booking Service consume `trip.trip.completed`.
- Day-21 history source for this consumer is `COMPLETE_ON_TRIP_COMPLETED`; it appends `COMPLETED`
  with null actor/reason in the same Booking-local transaction as the guarded status transition.
- `NO_SHOW`: Day-24 `NoShowDetectionJob` marks all remaining `PENDING` passengers only after the
  along-route anchor `TripStop.actualArrivalTime + 15m < now` or terminal anchor
  `Trip.actualDepartureTime + 15m < now` (strict; equality excluded). All-pending bookings become
  `NO_SHOW`.
- `PARTIAL_NO_SHOW`: the same locked/rechecked job marks remaining pending passengers and sets
  the Booking to `PARTIAL_NO_SHOW` when at least one passenger is already `BOARDED`; all-boarded
  bookings are unchanged. Missing/stale raw anchors fail closed.
- `CANCELLED|DISRUPTED → REFUNDED`: Booking Service consumes `payment.wallet.credited` with
  `referenceType=BOOKING_REFUND` and CASes either source status.
- `DISRUPTED`: Booking Service consumes `trip.trip.disrupted { hasSubstitution: false }` for
  `CONFIRMED|PARTIAL_NO_SHOW`; `NO_SHOW` and all other statuses are no-op.
- Vehicle substitution does not change eligible Booking status: `CONFIRMED|PARTIAL_NO_SHOW` is
  retained. Mapped old `BOARDED` Passengers receive the nullable mapped seat plus one
  `PENDING_CONFIRM` transfer; mapped old `PENDING` Passengers receive the nullable mapped seat plus
  one `NOT_REQUIRED` transfer. `NO_SHOW` Passengers are not mapped. Duplicate delivery has no state,
  transfer, or Outbox effect.
- `cancellationReason` enum: `USER_INITIATED | OPERATOR_CANCELLED_TRIP | OPERATOR_DISRUPTED_IN_PROGRESS | SCHEDULE_CHANGED | ROUTE_CHANGED_REFUSED | VEHICLE_SUBSTITUTION_DOWNGRADE | VEHICLE_SUBSTITUTION_NO_SEAT | STOP_DISABLED_REFUSED`.

#### Authoritative Booking status timeline (Day 19)

The operator booking-monitor timeline is sourced only from the append-only `booking_status_history` read model. This supersedes the Day-19 timeline wording “events from Outbox audit”: Outbox delivery time is not a domain-status occurrence time, and lifecycle timestamp columns must not be used to fabricate history.

| Column | PostgreSQL type | Null | Constraint / meaning |
|---|---|---|---|
| `id` | `uuid` | no | Primary key. |
| `booking_id` | `uuid` | no | Local FK to `bookings(id)` with `ON DELETE RESTRICT`. |
| `status` | `booking_status` | no | Status reached by the successful creation/transition. |
| `occurred_at` | `timestamptz` | no | Application-captured occurrence time. |
| `reason_code` | `varchar(100)` | yes | Canonical machine-readable domain reason only. |
| `actor_user_id` | `uuid` | yes | Logical FK to Identity User; deliberately no cross-database FK. |
| `source` | `varchar(100)` | no | Required application-controlled source constant. |

The required read index and deterministic timeline order are `(booking_id, occurred_at, id)` and `occurred_at ASC, id ASC`. History has insert/read surfaces only: no update/delete repository/API, no integration event, and no historical backfill for pre-migration bookings. Booking remains non-deletable; `ON DELETE RESTRICT` is nevertheless mandatory protection.

Current writers and population rules are frozen as follows:

| Source constant | Recorded status | Actor | Reason | Occurrence / transaction / guarded no-op |
|---|---|---|---|---|
| `CREATE_BOOKING` | `PENDING_PAYMENT` | authenticated passenger user id | null | Per writer rules below. |
| `CREATE_ROUND_TRIP_BOOKING` | `PENDING_PAYMENT` for each created leg | authenticated passenger user id | null | Per writer rules below. |
| `CONFIRM_ON_PAYMENT` | `CONFIRMED` | null | null | Per writer rules below. |
| `EXPIRE_ON_PAYMENT` | `EXPIRED` | null | null | Per writer rules below. |
| `CANCEL_BOOKING` | `CANCELLED` | authenticated passenger user id | exact existing `BookingCancellationReason` enum name | Per writer rules below. |
| `MARK_NO_SHOW` | `NO_SHOW` or `PARTIAL_NO_SHOW` | null | null | Day-24 strict anchor job; one row only for a guarded Booking transition. |
| `MARK_REFUNDED` | `REFUNDED` | null | null | Per writer rules below. |
| `COMPLETE_ON_TRIP_COMPLETED` | `COMPLETED` | null | null | `occurredAt=event.completedAt`; same Booking-local transaction as the guarded status transition; guarded no-op/replay appends no row. |
| `DISRUPT_ON_TRIP_DISRUPTED` | `DISRUPTED` | null | `OPERATOR_DISRUPTED_IN_PROGRESS` | `occurredAt=event.terminalAt`; same Booking-local transaction as the guarded `CONFIRMED\|PARTIAL_NO_SHOW -> DISRUPTED` transition, voucher-usage compensation, and both canonical Outbox facts; guarded no-op/replay appends no row. |

Each writer captures `IClock.UtcNow` exactly once and reuses that value for the Booking creation/transition timestamp work and its history row; it never uses database-read time or Outbox publish time. A creation appends `PENDING_PAYMENT`, and every guarded successful transition appends exactly one row in the same local database transaction. A guarded no-op/replay appends no row. If the transaction rolls back, both state and history roll back atomically.

Future status writers require SOT review and an explicitly approved `source` constant before implementation. Authenticated-human transitions record the caller user id; automated/system/event-driven transitions record null. `reason_code`, when applicable, must be an existing canonical domain reason code rather than free text.

### 8.2 TripStatus

```
SCHEDULED ─┬─→ BOARDING ─→ IN_PROGRESS ─┬─→ COMPLETED
           │      │                     └─→ DISRUPTED  (terminal)
           │      └─→ CANCELLED
           └─→ CANCELLED
```

**Triggers:**

- `BOARDING`: Hangfire (Trip) 30 phút trước `departureDateTime`.
- `IN_PROGRESS`: PRIMARY là Driver được gán bấm "Start trip" khi `BOARDING`; SECONDARY là
  Hangfire recurring scan mỗi 5 phút, chỉ auto-start khi `departureDateTime < now - 30 phút`.
  GPS không phải PRIMARY trigger và chỉ bắt đầu tracking sau `trip.trip.started`.
- `COMPLETED`: PRIMARY là Driver/Assistant được gán bấm "End trip" khi `IN_PROGRESS`; SECONDARY là
  Hangfire recurring scan mỗi 15 phút, chỉ auto-complete khi
  `estimatedArrivalTime < now - 30 phút`.
- `DISRUPTED`: 2 case — phân biệt qua presence của `BookingTransfer`:
  - Case 1: Vehicle Substitution → Trip_old DISRUPTED, BookingTransfer created, KHÔNG refund.
  - Case 2: Operator hủy IN_PROGRESS bất khả kháng → Trip DISRUPTED, KHÔNG BookingTransfer, auto-refund proportional theo `distanceFromOriginKm`.
- Day-34 Vehicle Substitution requires old Trip `IN_PROGRESS`, terminalizes it as `DISRUPTED` with
  `hasSubstitution=true`, and creates one dedicated replacement as `BOARDING` with
  `source=VEHICLE_SUBSTITUTION`. The existing assigned-driver start flow alone performs
  `BOARDING -> IN_PROGRESS`; no new Trip status is introduced.
- `Trip.source` enum: `MANUAL | AUTO_FROM_SCHEDULE | VEHICLE_SUBSTITUTION` (VEHICLE_SUBSTITUTION exempt subscription `maxTripsPerMonth` counter).
- `DELAYED` là overlay flag (Redis), KHÔNG phải status riêng.

**Public Trip detail operational projection:** `GET /v1/trips/{tripId}` trả
`destinationArrivedAt` nullable và mỗi TripStop trả `status=PENDING|ARRIVED|SKIPPED` cùng
`actualArrivalTime` nullable. `actualArrivalTime` chỉ có giá trị khi stop là `ARRIVED`; stop
`PENDING`/`SKIPPED` và Trip chưa đến destination trả `null`. Đây là read projection từ trạng thái
đã persist, không tạo lifecycle transition, event hoặc schema mới.

#### Authoritative Trip manual-completion audit contract (Day 21)

`trip_audit_logs` is append-only and Trip-owned. It has exactly these columns:

| Column | PostgreSQL type | Null | Constraint / meaning |
|---|---|---|---|
| `id` | `uuid` | no | Primary key. |
| `trip_id` | `uuid` | no | Local FK to `trips(id)` with `ON DELETE RESTRICT`. |
| `actor_user_id` | `uuid` | yes | Logical Identity User reference; deliberately no database FK. |
| `action` | `varchar(64)` | no | Application-controlled action constant. |
| `metadata` | `jsonb` | yes | Action metadata. |
| `occurred_at` | `timestamptz` | no | Application-captured occurrence time. |
| `created_at` | `timestamptz` | no | `DEFAULT now()`. |

Indexes are exactly `(trip_id, occurred_at DESC)`,
`(actor_user_id, occurred_at DESC) WHERE actor_user_id IS NOT NULL`, and
`(action, occurred_at DESC)`. The only Day-21 action is
`TripAuditAction.TripCompletedManual = "TRIP_COMPLETED_MANUAL"`.

Manual completion atomically persists the Trip `COMPLETED` state/timestamps, one audit row with
the authenticated actor and metadata `{tripId,role}`, and the `trip.trip.completed` Outbox row in
one Trip-local transaction. It performs no Identity read/write, creates no cross-database FK, and
publishes no audit integration event.

#### Day-22 Trip and DriverSchedule edit audit/pricing contract

Day-22 extends `TripAuditAction` with exactly `TRIP_EDITED`, `TRIP_VEHICLE_SWAPPED`,
`TRIP_ROUTE_CHANGED`, and `DRIVER_SCHEDULE_CASCADE_APPLIED`. Real schedule changes use separate
`DriverScheduleAuditAction.DriverScheduleEdited = "DRIVER_SCHEDULE_EDITED"`. Both audit stores are
append-only; same-value requests append nothing. Day-22 metadata is exactly
`{changedFields,before,after,requestId}` and never contains the raw Idempotency-Key.

Route-change proposals extend `TripAuditAction` with exactly
`ROUTE_CHANGE_PROPOSAL_CREATED|ROUTE_CHANGE_PROPOSAL_APPROVED|ROUTE_CHANGE_PROPOSAL_REJECTED|ROUTE_CHANGE_PROPOSAL_SUPERSEDED|ROUTE_CHANGE_PROPOSAL_EXPIRED`.
Their metadata is exactly `{proposalId,proposalType,status}`. Proposal approval additionally appends
the existing `TRIP_ROUTE_CHANGED` action with metadata `{proposalId,alternativeRouteId}`. Automatic
expiry uses null `actorUserId`; crew/admin transitions use the authenticated actor. Proposal state,
audit rows, and Outbox rows commit in the same Trip-local transaction.

`driver_schedule_audit_logs` mirrors the Trip audit shape with `driver_schedule_id` as a local FK
to `driver_schedules(id) ON DELETE RESTRICT`, nullable logical `actor_user_id` (no cross-DB FK),
`action varchar(64)`, nullable `metadata jsonb`, required application-captured `occurred_at`, and
`created_at DEFAULT now()`. Indexes mirror the Trip audit read paths for schedule, nullable actor,
and action. Trip/schedule state, every applicable audit row, and every Outbox row are staged and
saved/committed once in the same Trip-local transaction.

Trip edit persistence adds nullable `notes varchar(2000)` (trim; blank → null) and
`trip_stop_fares.source = TEMPLATE_SNAPSHOT|MANUAL_OVERRIDE`. Existing fare rows backfill
`TEMPLATE_SNAPSHOT`, but Day 22 creates no new rows with that source. Legacy snapshots remain
readable for omitted `pricingAt`, are non-authoritative for explicit `pricingAt`, and only an
explicit operator per-Trip fare override creates `MANUAL_OVERRIDE`. Booking pricing captures one
handler-start `pricingAt` and resolves `MANUAL_OVERRIDE` → active template satisfying
`effectiveFrom <= pricingAt < effectiveUntil` (or open-ended) → `Trip.baseFare`. Internal callers
that omit `pricingAt` keep the persisted operational snapshot and never consult current templates.
Once a Booking is inserted,
`baseFare`, `discountAmount`, and `totalAmount` remain immutable; Payment success never re-queries
Trip, and cancellation/refund uses persisted `totalAmount`.

### 8.3 ParcelStatus

```
PENDING_OPERATOR_REVIEW (legacy only) ──→ PENDING_PAYMENT | REJECTED | CANCELLED(review timeout)
PENDING_PAYMENT ──→ RESERVED | EXPIRED | CANCELLED
PENDING (legacy paid compatibility) ──→ CANCELLED | PENDING_OPERATOR_ACTION
PENDING_ADDITIONAL_PAYMENT (legacy compatibility) ──→ CANCELLED | PENDING_OPERATOR_ACTION | REJECTED
RESERVED ──→ CHECKED_IN | REJECTED(check-in timeout) | CANCELLED | PENDING_OPERATOR_ACTION
CHECKED_IN ──→ PENDING_FINAL_PAYMENT | READY_TO_LOAD | PENDING_OPERATOR_ACTION
PENDING_FINAL_PAYMENT ──→ READY_TO_LOAD | REJECTED(final-payment timeout) | CANCELLED
READY_TO_LOAD ──→ LOADED | CANCELLED
LOADED ──→ IN_TRANSIT | PENDING_TRANSFER_CONFIRM | PENDING_OPERATOR_ACTION
IN_TRANSIT ──→ UNLOADED | PENDING_TRANSFER_CONFIRM | PENDING_OPERATOR_ACTION
PENDING_TRANSFER_CONFIRM ──→ LOADED (target trip) | TRANSFER_ESCALATED
TRANSFER_ESCALATED ──→ PENDING_TRANSFER_CONFIRM | RETURNED
UNLOADED ──→ DELIVERED_PENDING_CONFIRM
DELIVERED_PENDING_CONFIRM ──→ DELIVERY_CONFIRMED | DELIVERY_REJECTED
DELIVERY_REJECTED ──→ RETURN_INITIATED  (Hangfire sau 15 phút undo window)
PENDING_OPERATOR_ACTION ──→ pendingActionResumeStatus | RETURNED
```

**Terminal:** `DELIVERY_CONFIRMED`, `RETURN_INITIATED`, `CANCELLED`, `EXPIRED`, `REJECTED`, `RETURNED`.

**Settlement v2 invariants:** pricing uses exact decimal chargeable weight and rounds fractional VND to the nearest đồng with `MidpointRounding.AwayFromZero`; it never ceilings kg or floors money to 1,000 VND. Voucher discount is clamped independently against estimated/final gross. Deposit is 20% of estimated total. Every derived size, including `EXTRA_LARGE`, requires a configured fare and starts at `PENDING_PAYMENT`; capacity is enforced at deposit hold and reweigh. `PENDING_OPERATOR_REVIEW` is legacy-only. Only `READY_TO_LOAD` can load. A Payment success is judged by authoritative `paidAt`, not webhook delivery time. If final-payment timeout wins before an on-time callback, the callback cancels forfeiture and restores `READY_TO_LOAD` when the trip can still serve; otherwise the Parcel becomes `CANCELLED` and all collected money is refunded.

**Canonical two-step delivery (Day 39):**

- `POST /v1/assistant/parcels/{parcelId}/unload` performs a synchronous Internal-JWT read of
  `GET /internal/v1/trips/{tripId}`. Parcel owns no consumer/projection for
  `trip.stop.arrived` or `trip.destination.arrived`; those facts never drive Parcel state.
- Unload chỉ cho phép `IN_TRANSIT -> UNLOADED`. Parcel có `dropoffStopId` phải dùng đúng TripStop
  khớp ID, `allowDropoff=true` và `status=ARRIVED`. Parcel có `dropoffStopId=null` chỉ dùng
  `destinationArrivedAt`; không suy diễn từ stop trung gian cuối. Express Trip không có TripStop vẫn
  unload được sau destination arrival.
- CAS unload thắng set duy nhất `status`, `unloadedAt`, clear pending-confirm metadata, revoke any
  active token-history row if present, and enqueue exactly one `parcel.parcel.unloaded` in the same
  Parcel-local transaction. Cargo
  release chỉ chạy ở unload và không lặp lại ở deliver.
- Deliver chỉ cho phép `UNLOADED -> DELIVERED_PENDING_CONFIRM`. The winner creates a runtime-only
  UUID-v4 token, persists only its normalized SHA-256 hash in `parcel_delivery_tokens`, and calls
  Notification through Internal JWT using `POST /internal/v1/emails`. The delivery link uses
  `PUBLIC_APP_URL`; neither the raw token nor URL may enter Parcel rows, Outbox, logs, or events.
  Token issuance, `deliveredPendingConfirmAt`, and the canonical
  `parcel.parcel.delivered_pending_confirm` fact commit only after Notification returns `202`.
  The existing recipient confirm flow continues from `DELIVERED_PENDING_CONFIRM` by hashing the
  supplied token for lookup.
- Resend revokes the active token-history row and rotates the token. When invoked during the
  reject undo window it first restores `DELIVERED_PENDING_CONFIRM`. Manual confirmation does not
  require a recipient email. Operator endpoints and assigned Driver/Assistant aliases enforce the
  same tenant and Trip assignment boundaries.
- Migration `Up` hashes normalized legacy UUID tokens, backfills history with
  `MIGRATION_BACKFILL`, then drops plaintext columns. SHA-256 is irreversible: `Down` may restore
  the legacy schema shape only with null raw-token values, must invalidate affected links, and
  requires reissue after rollback; it MUST NOT claim to reconstruct plaintext tokens.
- Replay được idempotency cache phục vụ mà không chạy handler. Hai request dùng key khác cùng race
  thì chỉ một CAS thắng; request thua trả `409 RESOURCE_CONFLICT`, không enqueue Outbox và không gọi
  cargo release.

**Trip cargo counter rule:** các flow reserve/load/transfer giữ nguyên theo technical context
Section 6.6e. Riêng canonical two-step delivery, cargo counter là Trip-owned qua internal API;
Parcel không tạo cross-database transaction, release chỉ được gọi một lần sau CAS unload thắng và
deliver không gọi lại.

### 8.4 PaymentStatus

**Wallet payment:** INSERT trực tiếp `SUCCEEDED` (trong cùng DB transaction với Wallet deduct) → `REFUNDED`.

**VNPay payment:**
```
PENDING_REDIRECT ─→ SUCCEEDED ─→ REFUNDED
                 ├─→ FAILED
                 └─→ EXPIRED
```

**`REFUNDED` trigger:** Payment Service consume `payment.wallet.credited` event với `referenceType ∈ (BOOKING_REFUND, PARCEL_REFUND)` → UPDATE Payment SET status=REFUNDED.

### 8.5 UserStatus

```
PENDING_EMAIL_VERIFICATION ─→ ACTIVE  (passenger verify OTP)
PENDING_INITIAL_PASSWORD   ─→ ACTIVE  (Driver/Assistant/OperatorStaff set password via email link)
ACTIVE ↔ LOCKED                       (password lockout / Admin)
ACTIVE/LOCKED ─→ DELETED              (soft delete, terminal v1)
```

### 8.6 OperatorRegistrationStatus

```
PENDING ─→ APPROVED | REJECTED
APPROVED ↔ SUSPENDED
REJECTED terminal
```

### 8.7 TopUpRequestStatus

```
PENDING ─→ SUCCEEDED | FAILED | EXPIRED  (15 phút timeout)
```

### 8.8 SeatStatus / TripSeatStatus

```
AVAILABLE ↔ HELD ↔ BOOKED
AVAILABLE ↔ UNAVAILABLE   (operator disable / seatLayout disabled)
BOOKED ─→ AVAILABLE       (khi booking cancelled)
```

### 8.9 OperatorTripSettlementStatus

```
PENDING_HOLD ─→ ELIGIBLE ─→ SETTLED
              └─→ CANCELLED
```

- `PENDING_HOLD` set khi Trip terminal (COMPLETED/DISRUPTED).
- `ELIGIBLE` set bởi Hangfire daily 02:00 khi `eligibleAt <= now` (= `tripTerminalAt + 7 days`).
- `SETTLED` set bởi Hangfire Monday weekly 09:00 (auto) hoặc Admin manual (`POST /v1/admin/trip-settlements/{id}/settle`).
- Cron canonical chạy UTC: eligibility `0 19 * * *` (=02:00 Asia/Ho_Chi_Minh), weekly `0 2 * * 1` (=09:00 Asia/Ho_Chi_Minh thứ Hai). Mỗi settlement dùng một local transaction, bounded batch và failure isolation; không gộp toàn batch vào một transaction.
- Marker và settlement là cùng một row. Mọi transition dùng expected status + `row_version`; manual/weekly race chỉ có một winner. Weekly loser no-op, manual loser trả `TRIP_SETTLEMENT_ALREADY_SETTLED`.
- `netAmount` luôn recompute từ immutable operator ledger tại settle time. Nếu `netAmount <= 0`, row chuyển `CANCELLED` và không tạo wallet movement/event.
- Thiếu PlatformWallet balance rollback toàn bộ movement, giữ `ELIGIBLE`, tăng `settlement_failure_count`, set `last_settlement_failure_at`/`active_failure_code`, và retry mỗi tuần không giới hạn. HIGH khi count `>=3` **hoặc** stuck `>21 ngày`; Redis key `payment:settlement_insufficient:{settlementId}` throttle alert 24h.
- Sau recovery thành công, giữ historical failure count/time, clear active error, set `failure_resolved_at`; row không còn thuộc stuck filter. Earned settlement không bị filter theo subscription hiện tại.

### 8.9.1 Invoice PDF lifecycle (Day 38 freeze)

`Invoice.status`: `DRAFT → ISSUED`; không ISSUED trước khi PDF upload thành công. `pdf_generation_status`: `PENDING → PROCESSING → COMPLETED|FAILED`.

- Invoice unique theo `payment_id`; number dùng atomic monthly counter `VR-INV-yyyyMM-XXXXXX`, bắt đầu `000001`, giới hạn `999999`.
- CAS claim `PENDING → PROCESSING` increment `pdf_generation_attempts` trước render. Tối đa năm total attempts. Failure attempts 1..4 set `FAILED` + `next_retry_at` theo `[1,5,15,30]` phút; attempt 5 terminal `FAILED`, `next_retry_at=NULL`.
- Reconciliation mỗi 5 phút requeue due FAILED và recover PROCESSING stale >15 phút. Stale recovery đã tiêu attempt đang chạy và không increment lần hai.
- Manual admin retry dùng Idempotency-Key; request chỉ CAS FAILED→PENDING/enqueue, không reset/increment attempts. Same key replay response gốc; different keys race chỉ một winner.
- DB/event lưu protected VietRide `downloadApiUrl`, không signed URL. Authenticated download sinh signed Firebase URL mới TTL 60 phút, rate limit 10/phút/user/invoice. Notification/email dùng `invoiceWebUrl`, không link thẳng protected API hoặc signed URL.

### 8.10 BookingPendingAction lifecycle

`BookingPendingAction` track confirmation cần passenger phản hồi (ROUTE_CHANGE, SEAT_DOWNGRADE, SCHEDULE_CHANGE, PENDING_SEAT_ASSIGNMENT, STOP_DISABLED). Partial unique `UNIQUE(bookingId) WHERE resolvedAt IS NULL` — chỉ 1 active per booking. Action mới phát sinh → close action cũ với `resolvedAction = SUPERSEDED` rồi INSERT mới.

For `ROUTE_CHANGE`, Booking consumes only its matching element from the frozen
`trip.trip.route_changed.affectedBookings` array and persists its `candidateStops`, `tripStatus`,
`sourceEventId`, `originalStopId`, `fallbackDestinationStationId`, `shuttleRequired=true`, and
deadline in immutable action metadata. Deadline is `occurredAt + 30m` for
`IN_PROGRESS` and `occurredAt + 60m` for `SCHEDULED|BOARDING`. Acceptance must match exactly one
candidate identity from that metadata (`stopId` XOR `stationId`); it never calls Trip to refresh
the candidates. Explicit passenger rejection cancels with `ROUTE_CHANGED_REFUSED`, freezes a 100%
refund from `Booking.totalAmount`, and emits only authoritative `booking.booking.cancelled`.
Strictly after the deadline, no-response expiry instead resolves
`AUTO_FALLBACK_DESTINATION`, leaves Booking `CONFIRMED`, changes no pickup field, creates no refund,
and emits one `booking.booking.route_change_auto_fallback_applied` fact for Notification and
shuttle coordination. Duplicate delivery/job execution is idempotent and creates no duplicate
ID source, active action, refund, or event.

Day-22 vehicle swap creates `PENDING_SEAT_ASSIGNMENT` only for an incompatible BOOKED seat on a
`SCHEDULED` Trip when `deadline = min(event.occurredAt + 4h, departureDateTime - 30m)` is strictly
later than the Booking handler clock. Metadata stores `sourceEventId` plus exact seat detail in the
existing JSONB; no column is added.

Day-23 schedule changes are produced only by PATCH `/v1/operator/driver-schedules/{scheduleId}?applyTo=FUTURE_ONLY|ALL_PENDING`, specifically the
`ALL_PENDING` branch. One captured clock preflights every affected Trip with a `CONFIRMED`
Booking: both `oldDeparture - now` and computed `newDeparture - now` must be `>= 2h`; equality is
allowed. Severity uses absolute delta and Asia/Ho_Chi_Minh calendar dates: MINOR is same-date `delta <= 2h`;
MEDIUM is same-date `delta > 2h && delta < 6h`; MAJOR is `delta >= 6h` or an Asia/Ho_Chi_Minh date change.

Booking preserves immutable `trip_snapshot_departure` and maintains nullable
`trip_current_departure` (`TripCurrentDeparture`) as the mutable projection. Existing rows are
backfilled from the snapshot. On `trip.trip.schedule_changed`, both `PENDING_PAYMENT` and
`CONFIRMED` Bookings apply causal CAS: `current==old` advances to new, `current==new` is a duplicate
no-op, and any other value retries then quarantines instead of overwriting. Only `CONFIRMED`
Bookings emit passenger facts: MINOR emits informational only; MEDIUM/MAJOR supersede the old
active action, create one `SCHEDULE_CHANGE`, and emit required. `date` queries use the Asia/Ho_Chi_Minh
half-open day of the current projection; existing `sortBy=departureAt` orders that projection and
then `id` in `sortDir`. List/detail add nested `trip.currentDepartureAt` beside immutable
`trip.departureAt`; the `currentDepartureAt` field exists only under `trip`, with no top-level
duplicate or new sort key. `STOP_DISABLED` deadlines use
the current projection.

Schedule action metadata freezes exact `sourceEventId`, `oldDeparture`, `newDeparture`, `severity`,
`initialDeadline`, nullable `terminalDeadline`, `refundBasisAmount`, `refundPercent`, and
`refundAmount`. The basis is immutable `Booking.totalAmount`; MEDIUM uses 50%, MAJOR uses 100%, and
money arithmetic rounds to the nearest VND with `MidpointRounding.AwayFromZero`. For MAJOR,
`terminalDeadline = newDeparture - 30m`; MEDIUM has no terminal deadline. The effective passenger
cutoff is terminal only when MAJOR has `initialDeadline < terminalDeadline`, otherwise initial.

POST `/v1/bookings/{bookingId}/pending-actions/{actionId}/resolve` is UUID-v4 idempotent,
`PASSENGER`-only, owner-only, and limited to persisted reason `SCHEDULE_CHANGE`; the exact body is
`{ action: ACCEPTED|REJECTED, note? }`, while `selectedStopId` is invalid. Same-key/same-payload
replays byte-identical before terminal lookup. Equality at the effective cutoff remains eligible;
only strictly-after is expired. ACCEPTED resolves the action and preserves `CONFIRMED`. REJECTED
atomically resolves it, sets `refundOverride=true`, cancels Booking with `SCHEDULE_CHANGED`, appends history, and enqueues one
authoritative `booking.booking.cancelled` carrying the frozen amount. No accept/reject alias or
operator seat-assignment path is ratified here.

The existing Day-22 `PendingActionRealertJob` remains an occurrence `+2h` informational phase for
unresolved `PENDING_SEAT_ASSIGNMENT` and MEDIUM/MAJOR `SCHEDULE_CHANGE`, at most once for that
intended phase. Separate Day-23 `ScheduleChangeAutoAcceptJob` owns the initial/terminal scheduled
resolution phases in §10.1; neither scheduled phase cancels a Booking or creates a refund.

#### Day-24 STOP_DISABLED and no-show rules

`trip.stop.disabled` is the sole impact source for the canonical bodyless DELETE stop mutation;
the response has `warning: null` as a present property and omits `ActiveBookingCount`. Booking
creates one active `STOP_DISABLED` action per eligible confirmed booking (`Trip.status` is
`SCHEDULED|BOARDING`) and stores exact metadata `{disabledStopId, affectedField:PICKUP|DROPOFF,
suggestedStopId?, fallbackStationId}`, where fallback is route origin for `PICKUP` and route
destination for `DROPOFF`. The handler captures one `capturedNow` and persists
`deadline = min(capturedNow + 24h, tripCurrentDeparture - 2h)`; `deadline == now` remains passenger
eligible, with no synchronous fallback.
only a later scheduler pass after the equality edge may resolve that action.

Passenger replacement reuses edit-pickup/edit-dropoff and atomically resolves the action. The
bodyless singular `accept-fallback` route maps pickup to the route origin or dropoff to the route
destination and resolves `AUTO_FALLBACK_DESTINATION`; the response shape is the existing generic
`ApiResponse` result (D24-2 ratifies no additional fields). Cancellation reuses Booking cancel with
`STOP_DISABLED_REFUSED`, atomically resolves/cancels, sets `refundOverride=true`, and refunds 100%.
All mutations are owner/deadline/idempotency checked; Day-23 `SCHEDULE_CHANGE` resolver/body is
unchanged. A new key after terminal resolution is `BOOKING_PENDING_ACTION_ALREADY_RESOLVED`.

`StopDisabledAutoFallbackJob` runs every 5 minutes and selects only `reason=STOP_DISABLED`,
`resolvedAt IS NULL`, and strict `deadline < now`. It resolves the terminal fallback and emits one
`booking.booking.stop_disabled_auto_fallback_applied` fact per action. `NoShowDetectionJob` also
runs every 5 minutes with separate strict along-route and terminal anchors, fails closed on missing
raw snapshots, lock/rechecks state, records `MARK_NO_SHOW`, and emits one
`booking.booking.passenger_no_show_marked` fact per Booking transition.

Task ownership is explicit: Task 24.0a owns only Trip EF/domain/migration paths for nullable
`trip_stops.actual_departure_time`; Task 24.0b owns only Notification Prisma/DDL paths for
`DRIVER_STOP_DEPARTED_WITH_PENDING`. Task 24.0 and Tasks 24.1–24.10 do not edit DDL, EF snapshots,
or Prisma migrations.

### 8.11 OperatorVoucherConsent

```
PENDING ─→ ACCEPTED | REJECTED
ACCEPTED ─→ REJECTED  (revoke after accept — voucher inactive cho operator từ thời điểm đó; booking đã CONFIRMED giữ nguyên discount)
```

---

## 9. Cross-cutting Concerns

### 9.1 Logging

**.NET (Serilog):**

```json
{
  "@t": "2026-05-25T14:30:00.123Z",
  "@l": "Information",
  "@m": "Booking confirmed: {BookingCode}",
  "BookingCode": "VR-20260518-ABCD1234",
  "BookingId": "uuid",
  "UserId": "uuid",
  "RequestId": "uuid",
  "TraceId": "uuid",
  "Service": "booking",
  "Env": "production"
}
```

Sinks: Console (Docker stdout) + File (rolling `/logs/<service>-yyyymmdd.log` 7-day retention).

**NestJS (Winston):** identical JSON shape.

**Required fields per log entry:** `service`, `requestId`, `userId?`, `operatorId?`, `traceId?`. Use structured logging (placeholder), KHÔNG concat string.

**Sentry integration:** mọi unhandled exception + log level Error → ship Sentry. Configure DSN per service qua env `SENTRY_DSN_<SERVICE>`.

### 9.2 Exception filter / handler

**.NET:** Global `ExceptionFilter` middleware:

```
DomainException        → 400/422 (per type) với errorCode từ exception
ValidationException    → 422 với errors[] field-level
NotFoundException      → 404
ForbiddenException     → 403
ConflictException      → 409
HttpRequestException   → 502 PAYMENT_VNPAY_ERROR / external service errors
Unhandled              → 500 INTERNAL_ERROR + Sentry capture + log full stack
```

Mọi response error → ADR 0004 `ApiResponse` error envelope (Section 5.5).

**NestJS:** Global `HttpExceptionFilter` + custom exception classes (`BookingException`, `ValidationException`, etc.) mapping tương tự.

### 9.3 Validation pipeline

**.NET (FluentValidation + MediatR ValidationBehavior):**

```csharp
// Application/Common/Behaviors/ValidationBehavior.cs
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    // Run all IValidator<TRequest> → aggregate ValidationFailure → throw ValidationException
}
```

Validator class per Command:

```csharp
public class CreateBookingCommandValidator : AbstractValidator<CreateBookingCommand>
{
    public CreateBookingCommandValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.Seats).NotEmpty().Must(s => s.Count <= 5)
            .WithErrorCode("BOOKING_MAX_SEATS_EXCEEDED");
        // ...
    }
}
```

**NestJS (Zod + custom ZodValidationPipe):**

```ts
const CreateBookingSchema = z.object({
  tripId: z.string().uuid(),
  seats: z.array(z.object({ seatNumber: z.string() })).max(5),
  // ...
});

@Post() create(@Body(new ZodValidationPipe(CreateBookingSchema)) dto: CreateBookingDto) {}
```

### 9.4 Timezone

- **Canonical ADR:** [ADR 0005](docs/adr/0005-utc-instants-vietnam-business-calendar.md).
- **Instant:** PostgreSQL `TIMESTAMPTZ`, internal HTTP, Redis, Outbox và RabbitMQ event dùng UTC, serialize kết thúc bằng `Z`. FE-facing `/v1/*` JSON HTTP và Tracking WebSocket serialize cùng instant qua IANA `Asia/Ho_Chi_Minh`, kết thúc bằng `+07:00`. Input timestamp bắt buộc có `Z`/offset và normalize UTC.
- **Business calendar:** chỉ một IANA timezone `Asia/Ho_Chi_Minh`. `DateOnly`, `TimeOnly`, `dayOfWeek`, search/report date và schedule dùng calendar này. Identifier được khai báo bằng system constant trong code, không thành cột theo từng record; không dùng fixed offset làm timezone identifier.
- **Date range:** ngày Việt Nam inclusive phải đổi thành UTC half-open `[fromUtc, toUtcExclusive)` và query `>= fromUtc && < toUtcExclusive`. Ví dụ `2026-08-10` → `[2026-08-09T17:00:00Z, 2026-08-10T17:00:00Z)`. Không cast `TIMESTAMPTZ` sang `date` theo session timezone.
- **`departureTime TIME`:** lưu local `Asia/Ho_Chi_Minh` (không có TZ component), combine với service date + named timezone để derive UTC instant. Schedule response trả additive `timeZone: "Asia/Ho_Chi_Minh"`; không thêm DB column.
- **Human text:** Notification/email format UTC instant sang `Asia/Ho_Chi_Minh`; persisted/event data giữ UTC `Z`, còn Notification public HTTP response vẫn theo boundary `+07:00`.
- **Runtime/jobs:** PostgreSQL và container chạy UTC. Hangfire cron dùng `TimeZoneInfo.Utc` explicitly; comment ghi giờ Việt Nam tương ứng. SQL bucket calendar dùng `AT TIME ZONE 'Asia/Ho_Chi_Minh'` rõ ràng.
- **Provider exception:** adapter như VNPay parse local provider format bằng `Asia/Ho_Chi_Minh`, rồi normalize UTC ngay tại boundary.

### 9.5 Money type & rounding

- **DB:** `BIGINT` (đơn vị đồng VND).
- **.NET in-process:** custom `Money` struct (wrap `long`) — KHÔNG dùng `decimal`/`float`/`double` cho VND.
- **TypeScript in-process:** `number` (safe < 2^53; VietRide max booking ~5,000,000 VND, < 2^53 ổn).
- **Rounding (v1.11.0):** giữ đến đơn vị ĐỒNG — KHÔNG floor về 1,000; `Money.FromRaw` pass-through, kết quả phép tính lẻ (giảm giá %, hoa hồng) làm tròn đến đồng gần nhất:
  ```csharp
  Money.FromDecimal(v) // Math.Round(v, 0, MidpointRounding.AwayFromZero)
  ```
- **API JSON:** `number` (BIGINT VND), KHÔNG dùng string.

### 9.6 Soft delete

Áp dụng cho: `Operator`, `User`, `Station`, `Stop`, `Route`, `Vehicle`.

**Pattern:** `deleted_at timestamptz NULL` — đây là canonical soft-delete marker duy nhất (xem ADR 0003). Implement `ISoftDeletable` (getter-only `DeletedAt`). `is_active` **không phải** thành phần của soft-delete.

EF Core global query filter:

```csharp
modelBuilder.Entity<User>().HasQueryFilter(u => u.DeletedAt == null);
```

Prisma: tự manual filter trong repository hoặc dùng `@DeleteDateColumn`.

Partial unique index cho field tái sử dụng được sau delete:

```sql
CREATE UNIQUE INDEX uq_users_email ON users (email) WHERE deleted_at IS NULL;
```

**Activation flag (separate concern):** `is_active boolean` là flag enable/disable độc lập với soft-delete. Chỉ Operator, Station, Stop, Route, Vehicle có `is_active`; `User` không có (dùng `status` enum cho activation axis). Entities cần activation flag implement `IActivatable` (getter-only `IsActive`). Xem ADR 0003 (`docs/adr/0003-soft-delete-marker-vs-activation-flag.md`).

### 9.7 Optimistic concurrency

Applied to: `wallets`, `platform_wallets`, `operator_wallets`, `operator_trip_settlements`.

**.NET:** EF Core `[ConcurrencyCheck]` trên `RowVersion INT` column. UPDATE generates `WHERE row_version = :old`. Conflict → `DbUpdateConcurrencyException` → retry tối đa 3 lần (Polly) hoặc trả `409 RESOURCE_CONFLICT`.

**Wallet/PlatformWallet balance update pattern:**

```csharp
// In single transaction:
UPDATE wallets SET balance = balance - :amount, row_version = row_version + 1
WHERE user_id = :uid AND row_version = :oldRowVersion AND balance >= :amount
RETURNING balance;
-- If 0 rows: throw WALLET_INSUFFICIENT_BALANCE or retry
INSERT wallet_transactions (...);
```

### 9.8 Idempotency (Redis-backed)

**Key patterns:**

- `<service>:idem:v2:response:{SHA256(idempotencyKey)}` — completed response, TTL 24 giờ.
- `<service>:idem:v2:processing:{SHA256(idempotencyKey)}` — owner processing lock, TTL 120 giây.
- `<service>:idem:{idempotencyKey}` — legacy read-only detection trong rollout, không replay.

**Pseudocode:**

```
keyHash = SHA256(idempotencyKey)
legacyKey = `${service}:idem:${idempotencyKey}`
responseKey = `${service}:idem:v2:response:${keyHash}`
processingKey = `${service}:idem:v2:processing:${keyHash}`
fingerprint = SHA256(frame(sub, UPPER(method), pathBase + path, canonicalQuery, rawBody))

if redis.EXISTS(legacyKey): throw IDEMPOTENCY_KEY_MISMATCH
if response = redis.GET(responseKey):
   require response.requestFingerprint == fingerprint
   return exact(response.status, response.contentType, response.bytes)

owner = randomToken()
if not redis.SET(processingKey, { fingerprint, owner }, NX, EX=120):
   if response vừa xuất hiện: replay response
   lock = redis.GET(processingKey)
   if lock.fingerprint != fingerprint: throw IDEMPOTENCY_KEY_MISMATCH
   throw IDEMPOTENCY_REQUEST_PENDING

result = handler.execute()
if exception or result.status >= 500:
   compareOwnerAndDelete(processingKey, owner)
   return/throw result

compareOwnerThenAtomicallySetResponseAndDeleteLock(
   processingKey, owner, responseKey, result, EX=86400)
return result
```

Fingerprint normalization follows §5.6. Query keys are ordinal-sorted, absent differs from empty,
decoded repeated values preserve their order, and no query parameter is omitted.

### 9.9 Redis namespace conventions

Mọi key dùng pattern `<service>:<purpose>:<id>` để namespace per service. Cùng 1 Redis instance dùng chung (no DB number isolation). Mỗi service chỉ read/write key của riêng mình **trừ** `identity:jwks_cache` (Gateway + Tracking đọc read-only).

| Key pattern | Owner | Purpose | TTL |
|---|---|---|---|
| `identity:otp_rate:{email}` | Identity | Rate limit OTP (max 3/giờ) | 1h |
| `identity:pwd_reset_rate:{email}` | Identity | Rate limit password reset (max 3/giờ) | 1h |
| `identity:jwks_cache` | Identity (writer); Gateway, Tracking (reader) | JWKS public key cache | 1h |
| `identity:login_lockout:{userId}` | Identity | Failed login counter (window 15p) | 15p |
| `gateway:rate_limit:{ip}:{route}` | Gateway | API rate limit | 1p |
| `gateway:internal_jwt:{kid}` | Gateway | Internal JWT signing key cache (nếu rotate) | 1h |
| `seat_lock:{tripId}:{seatNumber}` | Trip | Seat hold trong checkout | 10p |
| `<service>:idem:v2:response:{sha256(key)}` | Business service | Idempotency completed-response cache | 24h |
| `<service>:idem:v2:processing:{sha256(key)}` | Business service | Owner-safe in-flight processing lock | 120s |
| `<service>:idem:{key}` | Business service | Legacy cache detection, fail closed; không tạo entry mới | tối đa 24h |
| `payment:vnpay_ipn:{vnpTxnRef}` | Payment | Dedupe IPN callback | 24h |
| `payment:invoice_download:{userId}:{invoiceId}` | Payment | Invoice signed-URL endpoint rate limit | 1p |
| `payment:settlement_insufficient:{settlementId}` | Payment | Insufficient PlatformWallet alert dedupe | 24h |
| `tracking:latest:{tripId}` | Tracking | Last known GPS position | 5p |
| `tracking:gps_buffer:{tripId}` | Tracking | GPS trail buffer (list) | đến flush |
| `tracking:eta:{tripId}:{targetId}` | Tracking | Dynamic ETA per remaining STOP or destination STATION | 60s |
| `tracking:eta_batch_lock:{tripId}` | Tracking | Owner-safe atomic ETA batch calculation lock | 10s |
| `tracking:trip_delay_state:{tripId}:{stopId}` | Tracking | Evaluated delay state (`stopId`, `delayStatus`, `delayMinutes`, `evaluatedAt`); reads legacy trip-level key during rolling deploy | 24h |
| `tracking:trip_delay_lock:{tripId}` | Tracking | Owner-safe delay state evaluation lock | 10s |
| `tracking:off_route_since:{tripId}` | Tracking | Off-route timer start | đến clear |
| `tracking:active_trips` | Tracking | Set of active tripIds | — |
| `tracking:approaching_notified:{tripId}:{bookingId}:w{1\|2}` | Tracking | Dedupe approaching alert | đến hết chuyến |
| `notification:fcm_token_blacklist:{token}` | Notification | Cache invalid FCM tokens | 1d |
| `rag:embed_cache:{queryHash}` | RAG AI | Cache query embedding | 1d |

### 9.10 ID format

- **PK:** UUID v4 (`gen_random_uuid()` server-side default).
- **Booking code:** `VR-yyyyMMdd-XXXXXXXX` (date prefix + 8-char base32 uppercase, e.g. `VR-20260518-ABCD1234`) user-facing, lưu kèm trong `bookings.code UNIQUE`. Generated server-side at create.
- **Parcel code:** `VRP-<6 char base32>` similar.
- **Voucher code:** uppercase base32, 6–12 ký tự, admin nhập thủ công hoặc auto-gen 8 char.
- **Invoice number:** `VR-INV-yyyyMM-XXXXXX` (6-digit sequential per month).
- **VNPay `vnp_TxnRef`:** UUID v4 (Payment Service gen).

### 9.11 Multi-tenancy enforcement

Mọi entity gắn `operator_id` BẮT BUỘC filter `WHERE operator_id = :claim` từ Internal JWT (trừ SYSTEM_ADMIN). Implement qua:

- **.NET:** custom `OperatorTenantInterceptor` (EF Core SaveChanges interceptor + IQueryable extension) hoặc Repository base class auto-inject.
- **NestJS:** Guard inject `operatorId` vào request context + decorator `@OperatorScoped()` trên endpoint.

Mọi WRITE endpoint check `dto.operatorId === claim.operatorId` (hoặc dùng claim trực tiếp, không nhận từ client).

### 9.12 Health & readiness

- `GET /health` → 200 nếu process alive. Always returns immediately. Không check DB.
- `GET /ready` → 200 nếu DB + Redis + RabbitMQ reachable. Trả 503 + body `{ checks: { db: 'down', redis: 'up', rabbitmq: 'up' } }` nếu fail.

Nginx + Docker healthcheck dùng `/health`. UptimeRobot ping `/ready` mỗi 5 phút.

### 9.13 Observability minimal stack

| Tool | Vai trò | Setup |
|---|---|---|
| **Sentry** | Exception tracking | DSN env, init ở Program.cs / main.ts |
| **UptimeRobot** | Uptime ping | External, ping `/ready` |
| **Serilog/Winston** | Structured logging | Stdout (Docker capture) + rolling file |

KHÔNG dùng Prometheus/Grafana/Jaeger/Loki cho v1 (xem technical_context 3.5).

---

## 10. Background Jobs Registry

### 10.1 Hangfire jobs (.NET services)

> Schema `hangfire.*` trong DB của service đó. Mỗi job có `JobId` + idempotent execution.

#### Identity & User

| Job | Type | Trigger | Notes |
|---|---|---|---|
| `identity.subscription-expire-active` (`SubscriptionLifecycleJob.ExpireActiveAsync`) | Recurring | Daily 00:30 `Asia/Ho_Chi_Minh`; UTC cron `30 17 * * *` | Sets overdue ACTIVE subscriptions to EXPIRED; read access remains available |
| `identity.subscription-trial-expiring-warn` (`SubscriptionLifecycleJob.SendWarningsAsync`) | Recurring | Daily 09:00 `Asia/Ho_Chi_Minh`; UTC cron `0 2 * * *` | Sends one T-3 expiry warning per subscription |
| `identity.subscription-auto-revert` (`SubscriptionLifecycleJob.AutoRevertAsync`) | Recurring | Every minute; UTC cron `* * * * *` | Reconciles pending upgrade attempts, late success, expiry and durable quota allocations |
| `identity.subscription-monthly-trip-reset` (`SubscriptionLifecycleJob.ResetMonthlyTripUsageAsync`) | Recurring | Daily 00:01 `Asia/Ho_Chi_Minh`; UTC cron `1 17 * * *` | Month-boundary guard makes non-boundary daily invocations no-op |
| `identity.operator-wallet-backfill` (`OperatorWalletBackfillJob`) | Recurring | Every minute; UTC cron `* * * * *` | Backfills missing operator wallets idempotently |

#### Trip-Route-Vehicle

| Job | Type | Trigger | Notes |
|---|---|---|---|
| `GenerateTripsFromScheduleJob` | Recurring | Weekly Sun 23:00 Asia/Ho_Chi_Minh + immediate on DriverSchedule create/activate | Generate Trip 14 ngày kế tiếp. Idempotent (driverId + departureDateTime) |
| `AutoBoardingJob` | Recurring | Every 15 phút | Set SCHEDULED Trips to BOARDING only when `departureDateTime <= now + 30 phút`; publish `trip.trip.boarding_started` |
| `AutoStartFallbackJob` | Recurring | Every 5 phút | Set BOARDING Trips to IN_PROGRESS only when `departureDateTime < now - 30 phút`; capture `actualDepartureTime`; publish `trip.trip.started` |
| `AutoCompletedFallbackJob` | Recurring | Every 15 phút | Set IN_PROGRESS Trips to COMPLETED only when `estimatedArrivalTime < now - 30 phút`; publish `trip.trip.completed` |

#### Booking

| Job | Type | Trigger | Notes |
|---|---|---|---|
| `SeatReleaseTimeoutJob` | Scheduled (per Booking) | Authoritative Payment deadline | Release seat + Booking → EXPIRED; round-trip uses earlier leg |
| `ScheduleChangeAutoAcceptJob` | Scheduled (per BookingPendingAction) | `initialDeadline + 1s`, then optional `terminalDeadline + 1s` | MEDIUM finalizes at initial; MAJOR with `initialDeadline < terminalDeadline` may emit one distinct initial-phase re-alert then finalizes at terminal; lag/direct execution past terminal skips the optional phase; `initialDeadline >= terminalDeadline` finalizes strictly after initial; each intended phase is at most once and jobs never refund or cancel |
| `PendingActionRealertJob` | Scheduled (logical key `pendingActionId`) | action occurrence + 2h | unchanged Day-22 scope: unresolved `PENDING_SEAT_ASSIGNMENT` plus MEDIUM/MAJOR `SCHEDULE_CHANGE`; at most once for this intended T+2 phase |
| `StopDisabledAutoFallbackJob` | Recurring | Every 5 phút | Select only unresolved `STOP_DISABLED` actions with strict `deadline < now`; equality remains passenger-action eligible, then atomically terminal-fallback and emit one `booking.booking.stop_disabled_auto_fallback_applied` per action. |
| `NoShowDetectionJob` | Recurring | Every 5 phút | Separate strict anchors: `TripStop.actualArrivalTime + 15m < now` for along-route and `Trip.actualDepartureTime + 15m < now` for terminal. Fail closed on missing snapshot, lock/recheck, mark PENDING passengers, set `NO_SHOW`/`PARTIAL_NO_SHOW`, append `MARK_NO_SHOW`, and emit one passenger-no-show fact per transition. |

Booking hosts its own PostgreSQL-backed Hangfire storage/schema `hangfire`, queue `booking`, server
`vietride-booking`, using only the approved centrally pinned `Hangfire.AspNetCore` and
`Hangfire.PostgreSql`. Broker retry/redelivery may create multiple physical jobs; correctness uses
logical identities and locked state rechecks, never an exact physical-job-count guarantee.

The unchanged Day-22 T+2 execution locks and rechecks action existence, unresolved state, and
`now < deadline`. Its deterministic identity derives only from `pendingActionId`; Outbox
uniqueness permits one persisted side effect and duplicate physical jobs no-op. It
continues to cover both reasons and both MEDIUM/MAJOR schedule severities above.

The separate Day-23 state machine schedules the initial job for cutoff `+1s`, so a passenger call
at equality remains eligible. MEDIUM atomically resolves `ACCEPTED` and enqueues
`booking.booking.pending_action_auto_resolved` once. MAJOR with initial before terminal may enqueue
one `booking.booking.pending_action_realerted` using identity derived from action plus
`MAJOR_INITIAL_PHASE`, distinct from T+2, and then ensures the terminal job. If execution is already
past terminal it accepts directly without that optional fact. Terminal/direct acceptance uses a
deterministic action+outcome identity and enqueues auto-resolved once. `initialDeadline >=
terminalDeadline` has no optional phase and accepts only strictly after initial. Passenger/job
races lock/recheck so exactly one terminal outcome wins; no scheduled path changes Booking status
or emits cancellation/refund.

Consumer order is Booking DB commit → ensure schedule → Rabbit ACK. Crash after commit/before
ensure or ACK is repaired by broker/DLQ replay, which finds the existing
`(bookingId,sourceEventId)` action, emits no duplicate initial event, ensures scheduling, and then
ACKs. Schedule failure after commit is repairable; rollback removes action/state and Outbox
together. No extra table, column, migration, `realerted_at`, custom poller, or dependency is
permitted.

#### Parcel

| Job | Type | Trigger | Notes |
|---|---|---|---|
| `UndoRejectWindowJob` | Scheduled (per Parcel) | DELIVERY_REJECTED + 15 phút | DELIVERY_REJECTED → RETURN_INITIATED |
| `AutoRejectExtraLargeJob` | Scheduled (legacy records) | PENDING_OPERATOR_REVIEW + 24h | `CANCELLED`, reason `OPERATOR_REVIEW_TIMEOUT`; không áp dụng cho Parcel mới |
| `ParcelSettlementTimeoutJob` | Recurring | Every 5 phút | Xử lý cả `RESERVED → REJECTED` khi quá `latestCheckInAt` và `PENDING_FINAL_PAYMENT → REJECTED` khi `finalPaymentDeadline <= now`; forfeiture toàn bộ cọc + release cargo; callback on-time đến sau phải recovery theo invariant §8.3 |
| `PendingTransferConfirmEscalationJob` | Scheduled (per Parcel) | `now >= transferRequestedAt + 30 minutes` | CAS `PENDING_TRANSFER_CONFIRM -> TRANSFER_ESCALATED` only when `transfer_confirmation_claim_id IS NULL`. Crew claim is allowed only strictly before deadline. |
| `PendingTransferClaimRecoveryJob` | Recurring every 5 minutes | `PENDING_TRANSFER_CONFIRM`, non-null claim, `transfer_confirmation_claimed_at <= now - 5 minutes` | Replay Trip cargo transfer with the persisted claim id and persisted target; never mint a new key and never escalate a claimed row. A successful replay CAS-completes Parcel, a domain 4xx clears the claim for operator correction, and unknown/503 retains it for the next scan. |
| `PendingCargoRecoveryOperationJob` | Recurring every 5 minutes | `parcel_cargo_recovery_operations.status=PENDING`, `claimed_at <= now - 5 minutes` | Replay Day-32 `TRANSFER|RETURN` with the persisted UUID-v4 operation id and frozen payload. Success finalizes Parcel + operation + Outbox/stats in one local transaction; definitive 4xx marks `FAILED`; unknown/503 remains `PENDING`. |
| `PendingOperatorActionReAlertJob` | Scheduled (per Parcel) | PENDING_OPERATOR_ACTION + 2h | Emit replay-safe `parcel.parcel.pending_operator_action_realerted`; status unchanged |
| `DeliveryConfirmationReminderJob` | Recurring | Active confirmation token expired at least 7 days | Emit `parcel.parcel.delivery_confirmation_realerted` for the operator; never rotate the token or change Parcel status |

#### Payment & Wallet

| Job | Type | Trigger | Notes |
|---|---|---|---|
| `PaymentExpiredJob` | Recurring scan | `PENDING_REDIRECT AND (due_at ?? created_at + 15m) <= now` | Atomic CAS to EXPIRED + Outbox; persisted future deadlines are not expired by age |
| `TopUpExpiredJob` | Recurring scan | PENDING + 15 phút | UPDATE status = EXPIRED |
| `TripSettlementEligibilityFlagJob` | Recurring | Daily 02:00 Asia/Ho_Chi_Minh | Set OperatorTripSettlement.status = ELIGIBLE WHERE eligibleAt <= now |
| `TripSettlementWeeklyAutoSettleJob` | Recurring | Weekly Mon 09:00 Asia/Ho_Chi_Minh | Debit PlatformWallet + credit OperatorWallet cho mọi settlement ELIGIBLE |
| `InvoicePdfRetryJob` | Triggered (retry) | Post-payment-success event | Generate PDF, retry max 5 nếu fail |
| `RefundFailureRetryJob` | Recurring | Every 10 phút | Retry refund từ RefundFailureLog, max 5 lần |

### 10.2 BullMQ jobs (NestJS services)

> Queue name: `<service>:<purpose>`. Redis-backed.

#### Tracking

| Queue / Job | Trigger | Worker logic |
|---|---|---|
| `tracking:gps-batch-write` | Repeatable every 5 minutes | Flush Redis `tracking:gps_buffer:*` → batch INSERT GpsTrail |
| `tracking:outbox-publisher` | Repeatable every 5s | Read `outbox_events` PENDING → publish RabbitMQ |
| `tracking:eta-recalculate` | On GPS update event (conditional) | When next stop changes, distance >500m, or next ETA <15p: calculate every remaining PENDING stop plus destination, write all Redis targets atomically, and emit legacy `eta:update` plus `eta:batch:update`. Google partial/error discards the whole provider batch and uses one consistent route/speed fallback. |

#### Notification

| Queue / Job | Trigger | Worker logic |
|---|---|---|
| `notification:fcm-push` | Enqueued by RabbitMQ consumer | Call Firebase Admin SDK; retry 5s → 30s → 5m; DLQ sau exhausted |
| `notification:email-send` | Enqueued by RabbitMQ consumer (OTP / parcel link) | Call SendGrid; retry similar |

#### RAG AI

| Queue / Job | Trigger | Worker logic |
|---|---|---|
| `rag:document-ingest` | Enqueued on KnowledgeDocument APPROVED | Download Cloudinary raw asset → parse → chunk → call OpenRouter embedding model `nvidia/llama-nemotron-embed-vl-1b-v2:free` → validate 2.048 dimensions → INSERT `halfvec(2048)` KnowledgeChunk |
| `rag:outbox-publisher` | Repeatable every 5s | Outbox poll |

### 10.3 Retry & DLQ conventions

| Aspect | Standard |
|---|---|
| Hangfire retry | Default Hangfire policy + custom `AutomaticRetry(Attempts = 5)` cho job critical |
| BullMQ retry | `{ attempts: 5, backoff: { type: 'exponential', delay: 5000 } }` |
| RabbitMQ consumer | Manual ack; nack → DLQ `vietride.events.dlq` sau N retries; alert Sentry |
| Shared.Messaging transient consumer retry | Retry is opt-in only when a handler throws `TransientIntegrationEventException`. Publish to the queue-specific durable retry topology with per-message TTL, increment the application-owned `vietride-retry-count` header, and route to the terminal DLQ after the configured retry count is exhausted. Retry publishing uses `mandatory=true` plus publisher confirms; an unroutable or unconfirmed retry is not ACKed as successful. |
| Outbox publisher | `retry_count` field; sau 10 lần FAILED → leave + Sentry alert (không drop) |

---

## 11. Environment & Configuration

### 11.1 ENV var convention

- File: `.env.example` (root) check vào git với placeholder values + comments.
- File: `.env` per environment, **KHÔNG check vào git**.
- Validate ENV ở startup — fail fast nếu thiếu required var.
- **.NET:** `IOptions<T>` + `services.AddOptions<XOptions>().BindConfiguration("X").ValidateDataAnnotations().ValidateOnStart()`.
- **NestJS:** `ConfigModule.forRoot({ validationSchema: zod schema })`.

### 11.2 Common ENV vars (all services)

| Var | Example | Description |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` / `NODE_ENV` | `Development` / `Production` | |
| `SERVICE_NAME` | `booking` | For log tagging |
| `LOG_LEVEL` | `Information` / `info` | |
| `SENTRY_DSN` | `https://xxx@sentry.io/yyy` | |
| `INTERNAL_JWT_SECRET` | (32+ byte base64) | Shared by Gateway + business services |
| `INTERNAL_JWT_TTL_SECONDS` | `120` | |
| `REDIS_URL` | `redis://redis:6379` | |
| `RABBITMQ_URL` | `amqp://user:pass@rabbitmq:5672/` | |

### 11.3 Per-service ENV (key vars)

#### API Gateway

```
GATEWAY_PORT=3000
USER_JWT_JWKS_URL=http://identity:5001/v1/.well-known/jwks.json
USER_JWT_JWKS_CACHE_TTL_SECONDS=3600
INTERNAL_JWT_SECRET=...
RATE_LIMIT_DEFAULT_PER_MIN=120
APP_DEEP_LINK=vietride://payments/return
ANDROID_PACKAGE=com.vietride.passenger
DEEPLINK_ANDROID_SHA256_FINGERPRINTS=       # release fingerprint; blank until Passenger signs release

IDENTITY_BASE_URL=http://identity:5001
BOOKING_BASE_URL=http://booking:5003
TRIP_BASE_URL=http://trip:5002
PAYMENT_BASE_URL=http://payment:5004
PARCEL_BASE_URL=http://parcel:5005
NOTIFICATION_BASE_URL=http://notification:3002
RAG_BASE_URL=http://rag:3003
TRACKING_BASE_URL=http://tracking:3001
```

#### Identity & User

```
IDENTITY_PORT=5001
DB_CONNECTION=Host=pgbouncer;Port=6432;Database=vietride_identity;Username=...;Password=...
USER_JWT_PRIVATE_KEY=...    # PEM
USER_JWT_PUBLIC_KEY=...     # PEM (only for local dev; prod fetches via JWKS)
USER_JWT_ACCESS_TTL_MINUTES=15
USER_JWT_REFRESH_TTL_DAYS=30
USER_JWT_KID=2026-05
SYSTEM_ADMIN_BOOTSTRAP_EMAIL=admin@vietride.app
SYSTEM_ADMIN_BOOTSTRAP_PASSWORD=...     # only first deploy
GOOGLE_OAUTH_CLIENT_ID=...
GOOGLE_OAUTH_CLIENT_SECRET=...
FIREBASE_PROJECT_ID=...
FIREBASE_CLIENT_EMAIL=...
FIREBASE_PRIVATE_KEY=...                  # PEM; literal \n accepted and normalized in-process
FIREBASE_WEB_STORAGE_BUCKET=...           # exact Firebase Storage bucket used by client URLs
EMAIL_SERVICE_BASE_URL=http://notification:3002
PASSWORD_HASH_COST=12
PUBLIC_APP_URL=https://app.vietride.app
```

#### Booking

```
BOOKING_PORT=5003
DB_CONNECTION=...vietride_booking...
SEAT_LOCK_TTL_MINUTES=10        # Booking-side/client default only; Trip-owned TTL registry is under Trip-Route-Vehicle.
VNPAY_PAYMENT_TIMEOUT_MINUTES=15
EDIT_CUTOFF_HOURS=2
MAX_SEATS_PER_BOOKING=5
TRIP_BASE_URL=http://trip:5002
PAYMENT_BASE_URL=http://payment:5004
IDENTITY_BASE_URL=http://identity:5001
```

#### Trip-Route-Vehicle

```
TRIP_PORT=5002
DB_CONNECTION=...vietride_trip...
SEAT_LOCK_TTL_MINUTES=10        # Trip-owned source for SeatLock:TtlMinutes / lock-seats ttlSeconds default 600s.
GOOGLE_MAPS_API_KEY=...
GOOGLE_ROUTES_ENABLED=false
GOOGLE_ROUTES_API_KEY=...
TRIP_PLANNED_ETA_TIMEOUT_MS=3000
TRIP_STOP_DWELL_MINUTES=20
HANGFIRE_DASHBOARD_USER=admin
HANGFIRE_DASHBOARD_PASSWORD=...
IDENTITY_BASE_URL=http://identity:5001
FIREBASE_WEB_STORAGE_BUCKET=...
```

#### Payment & Wallet

```
PAYMENT_PORT=5004
DB_CONNECTION=...vietride_payment...
VNPAY_TMN_CODE=...
VNPAY_HASH_SECRET=...
VNPAY_BASE_URL=https://sandbox.vnpayment.vn/paymentv2/vpcpay.html
VNPAY_RETURN_URL=https://app.vietride.online/payments/return
VNPAY_IPN_URL=https://api.vietride.online/v1/payments/vnpay-ipn
VNPAY_PAYMENT_TIMEOUT_MINUTES=15
SUBSCRIPTION_TRIAL_DAYS=30
SETTLEMENT_HOLD_DAYS=7
WALLET_TOP_UP_MIN_VND=10000
IDENTITY_BASE_URL=http://identity:5001
TRIP_BASE_URL=http://trip:5002
```

#### Parcel

```
PARCEL_PORT=5005
DB_CONNECTION=...vietride_parcel...
DELIVERY_TOKEN_TTL_HOURS=48
PUBLIC_APP_URL=https://app.vietride.app
TRIP_BASE_URL=http://trip:5002
PAYMENT_BASE_URL=http://payment:5004
IDENTITY_BASE_URL=http://identity:5001
NOTIFICATION_BASE_URL=http://notification:3002
FIREBASE_WEB_STORAGE_BUCKET=...
```

#### Tracking

```
TRACKING_PORT=3001
DB_HOST=pgbouncer
DB_PORT=6432
DB_DATABASE=vietride_tracking
DB_USER=...
DB_PASSWORD=...
USER_JWT_JWKS_URL=http://identity:5001/v1/.well-known/jwks.json
GPS_BATCH_INTERVAL_MINUTES=5
ETA_RECALC_DISTANCE_THRESHOLD_METERS=500
ETA_RECALC_HIGH_FREQ_THRESHOLD_MINUTES=15
TRIP_STOP_DWELL_MINUTES=20
GOOGLE_ROUTES_ENABLED=false
GOOGLE_ROUTES_API_KEY=...
BOOKING_BASE_URL=http://booking:5003
TRIP_BASE_URL=http://trip:5002
PARCEL_BASE_URL=http://parcel:5005
```

#### Notification

```
NOTIFICATION_PORT=3002
DB_HOST=pgbouncer
DB_DATABASE=vietride_notification
FIREBASE_PROJECT_ID=...
FIREBASE_PRIVATE_KEY=...
FIREBASE_CLIENT_EMAIL=...
SENDGRID_API_KEY=...
SENDGRID_FROM_EMAIL=noreply@vietride.app
EMAIL_PROVIDER=SENDGRID    # SENDGRID | SMTP
SMTP_HOST=...              # if EMAIL_PROVIDER=SMTP
SMTP_PORT=587
SMTP_USER=...
SMTP_PASSWORD=...
IDENTITY_BASE_URL=http://identity:5001
```

#### RAG AI

```
PORT=3003
DATABASE_URL=postgresql://...
REDIS_URL=redis://...
RABBITMQ_URL=amqp://...
OPENROUTER_API_KEY=...
OPENROUTER_BASE_URL=https://openrouter.ai/api/v1
OPENROUTER_CHAT_MODEL=nvidia/nemotron-3-ultra-550b-a55b:free
OPENROUTER_EMBEDDING_MODEL=nvidia/llama-nemotron-embed-vl-1b-v2:free
RAG_EMBEDDING_DIMENSIONS=auto
CLOUDINARY_CLOUD_NAME=...
CLOUDINARY_API_KEY=...
CLOUDINARY_API_SECRET=...
CLOUDINARY_RAG_FOLDER=rag/documents
IDENTITY_INTERNAL_BASE_URL=http://identity:5001
```

### 11.4 Secrets management

- **Local dev:** `.env` file (gitignored).
- **Production:** Docker secrets hoặc environment variables injected qua deployment platform. Khuyến nghị Vault/AWS Secrets Manager nếu deploy production scale.
- **NEVER commit:** `INTERNAL_JWT_SECRET`, `USER_JWT_PRIVATE_KEY`, `VNPAY_HASH_SECRET`, `FIREBASE_PRIVATE_KEY`, `OPENROUTER_API_KEY`, `CLOUDINARY_API_SECRET`, `SENDGRID_API_KEY`, `SYSTEM_ADMIN_BOOTSTRAP_PASSWORD`.
- **Rotation:** xem 6.2 (JWKS key rotation). Internal JWT secret rotation cần coordinate restart toàn bộ services (downtime ~30s).

### 11.5 docker-compose.yml essentials

```yaml
services:
  postgres:
    image: postgres:16
    environment:
      POSTGRES_USER: vietride
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
    volumes:
      - postgres-data:/var/lib/postgresql/data
      - ./db-schema/_global/init.sql:/docker-entrypoint-initdb.d/01-init.sql:ro

  pgbouncer:
    image: edoburu/pgbouncer
    depends_on: [postgres]
    environment:
      POOL_MODE: transaction
      DEFAULT_POOL_SIZE: 15

  redis:
    image: redis:7
    command: redis-server --appendonly yes

  rabbitmq:
    image: rabbitmq:3-management
    ports: ["15672:15672"]   # mgmt UI

  identity:
    build: ./apps/identity
    env_file: .env
    depends_on: [pgbouncer, redis, rabbitmq]

  # ... (trip, booking, payment, parcel, tracking, notification, rag, gateway)

  nginx:
    image: nginx:1.25
    volumes:
      - ./infra/nginx/nginx.conf:/etc/nginx/nginx.conf:ro
    ports: ["80:80", "443:443"]
    depends_on: [gateway, tracking]

volumes:
  postgres-data:
```

### 11.6 Nginx routing essentials

```nginx
upstream gateway_upstream { server gateway:3000; }
upstream tracking_upstream { server tracking:3001; }

server {
  listen 443 ssl http2;
  server_name api.vietride.app;

  location /v1/payments/vnpay-ipn {
    allow <vnpay-ip-1>;
    allow <vnpay-ip-2>;
    deny all;
    proxy_pass http://gateway_upstream;
  }

  location /tracking/socket.io/ {
    proxy_pass http://tracking_upstream;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade";
    proxy_read_timeout 86400;
    limit_conn perip 5;
  }

  location / {
    proxy_pass http://gateway_upstream;
  }
}
```

---

## 12. Testing Conventions

### 12.1 Test layout

> **Per-app tests** sống cùng app trong `apps/<service>/`. **Cross-service / E2E tests** sống ở `tests/` root.

**.NET (per app):**

```
apps/<service>/tests/
├── VietRide.<Service>.UnitTests/
│   ├── Domain/Entities/BookingTests.cs                    Entity invariant tests (pure POCO, no EF)
│   ├── Application/Features/<Aggregate>/<UseCase>Tests.cs Handler tests — 1 file/handler
│   ├── Application/Behaviors/ValidationBehaviorTests.cs
│   └── VietRide.<Service>.UnitTests.csproj
└── VietRide.<Service>.IntegrationTests/
    ├── Fixtures/
    │   ├── PostgresFixture.cs                              Testcontainers PG (collection fixture)
    │   └── RedisFixture.cs                                 Testcontainers Redis
    ├── Api/<Aggregate>/Create<Aggregate>EndpointTests.cs   HTTP test via WebApplicationFactory
    └── VietRide.<Service>.IntegrationTests.csproj
```

**NestJS (per app — Nx convention):**

```
apps/<service>/src/**/*.spec.ts                              Unit tests co-located với source (Jest)
apps/<service>-e2e/                                          ⭐ Nx-generated sister project
└── src/<feature>/<feature>.e2e-spec.ts                      E2E qua supertest (real DB qua Testcontainers)
```

**Cross-service / E2E xuyên qua nhiều services:**

```
tests/
├── e2e/                                                     Playwright (mobile/web flow) hoặc Supertest (BE flow)
│   ├── booking-happy-path.e2e-spec.ts                       Passenger book → pay → board → complete
│   ├── parcel-delivery.e2e-spec.ts
│   └── trip-disruption.e2e-spec.ts
└── load/                                                    k6 / Artillery scripts
    └── booking-concurrent.js
```

### 12.2 Stack

| Layer | .NET | NestJS |
|---|---|---|
| Test framework | xUnit | Jest |
| Assertion | FluentAssertions | jest-extended |
| Mocking | NSubstitute | jest.mock + ts-jest |
| Integration: PG | Testcontainers (PostgreSQL) | Testcontainers |
| Integration: Redis | Testcontainers (Redis) | Testcontainers |
| HTTP test | WebApplicationFactory | supertest |
| Data factory | Bogus | @faker-js/faker |

### 12.3 Naming

| Layer | Class / file name | Method name |
|---|---|---|
| .NET unit | `<TargetClass>Tests` | `<MethodUnderTest>_<Scenario>_<ExpectedOutcome>` |
| .NET integration | `<Endpoint>EndpointTests` | `Should<ExpectedOutcome>_When<Scenario>` |
| NestJS unit | `<target>.service.spec.ts` | `describe('<MethodName>', () => { it('should ... when ...') })` |

### 12.4 Test data

- Per-test fresh DB schema (Testcontainers spin up new PG per test class hoặc reset migrations).
- Seed minimal data per test — KHÔNG shared seed file giữa nhiều tests.
- Use `IdempotencyKey: new UUID per test` để tránh collision.

### 12.5 Coverage target

- Unit tests: **≥70% line coverage** cho Application + Domain layer.
- Integration tests: cover all happy-path endpoint + critical error paths.
- KHÔNG enforce coverage cho Infrastructure (EF Core mappings, etc.) — integration tests cover instead.

### 12.6 Mocking dependencies

Mock qua interface đã định nghĩa ở `Application/Abstractions/`:

| Dependency | Interface | Mock approach |
|---|---|---|
| Per-aggregate repository | `IBookingRepository`, `IUserRepository`, ... | NSubstitute mock — unit test handler không cần spin up DB |
| Per-aggregate service | `IBookingService`, `IBookingPricingService`, ... | NSubstitute mock — khi test Handler dùng Service |
| Unit of Work (nếu có) | `IUnitOfWork` | NSubstitute mock |
| Event publisher | `IEventPublisher` | NSubstitute mock — verify event published với expected payload |
| VNPay | `IVnPayClient` | NSubstitute mock; integration dùng WireMock.NET stub HTTP |
| Firebase FCM | `IFcmPushClient` (Notification) | NSubstitute mock |
| SendGrid | `ISendGridEmailClient` (Notification) | NSubstitute mock |
| Google Maps Directions | `IGoogleDirectionsClient` (Tracking) | NSubstitute mock |
| OpenRouter chat / embedding | `ChatCompletionProvider`, `EmbeddingProvider` (RAG) | Jest mock `fetch` hoặc provider test double; assert model ID, numeric vector và đúng 2.048 dimensions |
| RabbitMQ broker | `IEventPublisher` (Outbox-aware) | Unit: mock; Integration: Testcontainers real broker |
| Inter-service HTTP | `ITripServiceClient`, `IIdentityServiceClient`, ... | NSubstitute mock; integration WireMock.NET hoặc real service |
| Clock | `IClock` | NSubstitute return fixed UTC |

> **Integration test:** dùng Testcontainers Postgres + Redis + RabbitMQ thật. Repository không cần mock — test repository qua DbContext thật.

### 12.7 CI gate

GitHub Actions per PR — leverage Nx affected để chỉ build/test app đã đổi:

```
1. nx affected -t lint --base=origin/main
2. nx affected -t build --base=origin/main         (covers cả .NET via @nx-dotnet executor)
3. nx affected -t test --base=origin/main          (unit + integration)
4. nx affected -t e2e --base=origin/main           (Nest e2e + cross-service e2e)
5. Migration smoke: run all migrations on fresh PG container → assert no error
6. Architecture test (NetArchTest in UnitTests):
   - Domain KHÔNG ref Application/Infrastructure
   - Application KHÔNG ref Infrastructure
   - Controllers KHÔNG inject IRepository/I<Aggregate>Service trực tiếp (phải qua MediatR.Send)
   - Repository impl KHÔNG chứa business logic (chỉ EF Core query/mutation)
7. File size lint (warn only — KHÔNG fail build, để review thảo luận):
   - Cảnh báo file C# > 500 dòng
   - Cảnh báo file TS > 400 dòng
   - Cảnh báo handler class > 200 dòng
   ⚠️ Threshold là cờ "cần review" KHÔNG phải reject auto. File lớn vẫn OK nếu là một concern liền mạch. Reviewer judge.
8. .NET nullable warnings as errors (enforce qua Directory.Build.props)
9. SonarQube/ReSharper inspection (optional v1)
```

PR fail nếu bất kỳ step nào fail.

---

## 13. Changelog

> Mỗi thay đổi convention backend → append 1 entry. Bump version theo SemVer.

| Version | Date | Author | Change |
|---|---|---|---|
| **1.63.2** | 2026-08-10 | Codex | **PATCH** — Remove the ambiguous legacy three-letter timezone shorthand from code, tests, scripts, schema comments and documentation. The only business timezone identifier is `Asia/Ho_Chi_Minh`; public instants serialize with `+07:00`, while persisted/internal instants remain UTC `Z`. No runtime instant, schema or event contract changes. |
| **1.63.1** | 2026-08-10 | Codex | **PATCH** — Reconcile §9.4, response examples and job registry with ADR 0005: public JSON/WebSocket uses `Asia/Ho_Chi_Minh +07:00`; internal HTTP, Redis, Outbox and RabbitMQ use UTC `Z`; Hangfire jobs declare UTC explicitly. |
| **1.63.0** | 2026-08-10 | Codex | **MINOR** — Amend ADR 0005 presentation: persistence, jobs, internal HTTP, Redis/Outbox/RabbitMQ remain UTC `Z`, while every FE-facing JSON HTTP/WebSocket instant is serialized through IANA `Asia/Ho_Chi_Minh` as `+07:00`. Requests still require an explicit offset and normalize UTC; no schema, stored timestamp, cache namespace, job, search range, or event contract changes. |
| **1.62.0** | 2026-08-09 | Codex | **MINOR** — Accept ADR 0005 and standardize all instants as UTC `Z`, Vietnam calendar values as `Asia/Ho_Chi_Minh`, half-open UTC date ranges, UTC Hangfire/PostgreSQL runtime, additive schedule `timeZone`, strict offset-bearing timestamp input, and Vietnam-formatted human notification text. No timestamp data or schema migration. |
| **1.61.0** | 2026-08-09 | Codex | **MINOR** — Add backend-owned intercity ETA for every stop and destination. Trip persists Google Routes versus Route-baseline planned source while exposing only `plannedEtaQuality`; Tracking calculates one ordered batch for all remaining targets, writes 60-second Redis entries atomically, exposes `GET /v1/tracking/trips/{tripId}/etas`, and emits additive `eta:batch:update` while preserving legacy `/eta` and `eta:update`. Adds reversible `planned_eta_source` migration and default 20-minute dwell configuration; no dependency or integration event added. |
| **1.60.0** | 2026-08-08 | Codex | **MINOR** — Reconcile the current RAG contract with runtime and physical DDL (Cloudinary raw document storage, OpenRouter chat/embedding, canonical `nvidia/llama-nemotron-embed-vl-1b-v2:free`, `halfvec(2048)`, HNSW cosine indexing) and standardize Payment as the financial source of truth for Dashboard/Analytics/Platform Report/export with canonical revenue predicates, typed adjustment taxonomy, separated settlement, Asia/Ho_Chi_Minh half-open ranges, 60-second cache, and fail-closed 503 financial consumers. No physical DDL, migration, dependency, provider secret, or integration-event change. |
| **1.59.0** | 2026-08-07 | Codex | **MINOR** — Freeze Mobile gap contracts: passenger Trip search exposes only `SCHEDULED`; round-trip route identity and leg-scoped seat conflicts; effective-route geometry/ETag; STOP/STATION ETA and passenger-history tracking targets; atomic notification read-all plus snapshot cursor pagination; System Admin `SUSPENDED -> APPROVED` operator reactivation with ActivityLog and unchanged subscription; RAG 429 documents `RAG_RATE_LIMIT_EXCEEDED`. Adds two UUID-v4-required mutations, raising the executable inventory to 190/173/17. No new dependency or integration event. |
| **1.58.0** | 2026-08-06 | Codex | **MINOR** — Hoàn thiện Manager Web Trip gaps: immutable Trip seat-layout snapshot, canonical usable passenger capacity, case-insensitive seat validation, operator-admin TripSeat disable/enable with row locking/audit/idempotency, operator shuttle history, batched pending-shuttle passenger enrichment, and method-aware Gateway routing. |
| **1.57.0** | 2026-08-05 | Codex | **MINOR** — Hoàn thiện Tracking Phase 12: giữ fallback `STOPS_ONLY` cho Route thiếu polyline, làm rõ Google/Local ETA fallback và UNKNOWN, khóa quy tắc chọn stop/recalculate, bổ sung delay state 24h, Outbox `dedupeKey` unique, transition `DELAYED`/`DELAY_CLEARED` và contract ETA additive tương thích ngược. |
| **1.56.0** | 2026-08-05 | Codex | **MINOR** — Bổ sung event `booking.booking.created` cho Tracking/Notification, payload crew-facing strict và phát Outbox nguyên tử cùng chuyển Booking sang CONFIRMED; giữ nguyên event `booking.booking.confirmed` tương thích ngược. |
| **1.55.0** | 2026-08-04 | Codex | **MINOR** — Hoàn thiện Shuttle hai chiều inbound/outbound, snapshot road distance Google Routes tối đa 5 km, internal distance contract, manifest/lifecycle event registry, operator subscription guard và fail-safe migration rollback. |
| **1.55.1** | 2026-08-04 | Codex | **PATCH** — Freeze the route-change proposal transaction lock protocol as source advisory lock → Trip → pending proposal UUIDs → dependency rows, and require proposal creation, audit, and Outbox persistence in that transaction. |
| **1.56.0** | 2026-08-05 | Codex | **MINOR** — Add Station two-level `city`/`ward` addressing, map-ready Route detail/composite writes and server polyline metrics, duplicate Route serialization, operator fleet REST/realtime tracking, optional next-stop ETA, DriverSchedule deactivate/delete, source schedule projection, and crew-aware route-proposal push/realtime delivery. |
| **1.55.0** | 2026-08-04 | Codex | **MINOR** — Add assigned Driver/Assistant EXISTING/CUSTOM route-change proposal snapshots, tenant-scoped Operator Admin decision flow, approval promotion/supersession/expiry semantics, five Trip lifecycle facts, four canonical errors and five audit actions. Notification consumes all five facts with active-admin fan-out for create, proposer delivery for terminal outcomes, five dedicated types and EventId/MessageId dedupe. Preserve direct admin route change and existing passenger impact, retire the global AlternativeRoute active-count cap and `ALTERNATIVE_ROUTE_LIMIT_EXCEEDED`, and add three UUID-v4-required mutations (188/171/17). |
| **1.54.0** | 2026-08-03 | Codex | **MINOR** — Add System-Admin operator detail projection and Trip-owned operator holiday fare surcharge settings/periods. Inclusive Asia/Ho_Chi_Minh departure dates, active-window overlap protection, pre-voucher nearest-VND adjustment, additive search/detail breakdown and Booking-time snapshot semantics are canonical. Adds four UUID-v4-required mutation surfaces, raising the inventory to 185/168/17; no dependency, integration event or background job. |
| **1.53.0** | 2026-08-02 | Codex | **MINOR** — Freeze public Tracking map context: authorized Trip route geometry with safe marker fallback, deterministic 1.000-point cap/ETag, passenger-only Shuttle context without foreign pickup leakage, post-pickup access continuity, and additive TripStop status for terminal-stop ETA selection. No migration, dependency, integration event, Gateway family, or Google configuration change. |
| **1.52.1** | 2026-08-02 | BE lead (Vũ) | **PATCH** — Reconciles the v1.54 Shuttle pickup merge with the system-wide idempotency inventory: `POST /v1/driver/shuttle-trips/{shuttleTripId}/stops/{pickupOrder}/pickup` is UUID-v4-required, raising the executable baseline to 181 HTTP mutation surfaces / 164 required / exactly 17 exemptions. Runtime metadata and API Contract were already required; no dependency, schema, migration or additional endpoint change. |
| **1.52.0** | 2026-08-02 | BE lead (Vũ) | **MINOR** — Freezes the Day-43 system-wide idempotency convention at 180 HTTP mutation surfaces: 163 UUID-v4-required actions and exactly 17 named exemptions. Reconciles the two post-merge read-only Trip batch POSTs and the higher-contract no-key DriverSchedule create/activate actions with auditable runtime metadata; preserves v2 replay/mismatch/pending/5xx semantics and does not rewrite historical Git metadata. No dependency, schema, migration or public endpoint change. |
| **1.51.0** | 2026-08-01 | BE lead (Vũ) | **MINOR** — Reconciles the Days 30–43 repair boundary: defers the unregistered manual Trip-create API; keeps `activePlanId` entitlements during `PENDING_PAYMENT`; fixes the trial warning at daily 09:00 Asia/Ho_Chi_Minh; preserves one zero-net settlement marker that terminates `CANCELLED` without wallet/event side effects; and makes Day-39 Parcel unload depend on a synchronous Trip snapshot with no Parcel arrival consumer/projection. No endpoint implementation, event payload, schema or migration change. |
| **1.50.0** | 2026-07-31 | BE lead (Vũ) | **MINOR** — Ratifies exact Booking refund correlation and zero-net group reconciliation: `payment.wallet.credited.paymentId?` is Booking-refund-only and backward-compatible, captured-payment retry rows use `BOOKING_REFUND_PAYMENT` with `referenceId=paymentId` and may carry amount zero, and Shared.Messaging transient retries are explicit, durable, TTL/header bounded, mandatory, and publisher-confirmed. No physical schema migration or index. |
| **1.49.0** | 2026-07-31 | BE lead (Vũ) | **MINOR** — Reopens Day 36/43 and ratifies the payment/history/auth repair contract: Booking VNPay deadlines follow Trip seat-lock expiry, legacy null `DueAt` falls back to 15 minutes, late capture never resurrects an expired Booking and uses idempotent allocation refund, `booking.payment_refund.requested` and `PAYMENT_DEADLINE_PASSED` are registered, internal latest-attempt redirect lookup is strict/no-store, Booking and Passenger history gain fail-open `paymentRedirectUrl`, and Google login returns stored avatar without provider overwrite. No schema migration or index. |
| **1.48.0** | 2026-07-31 | Codex | **MINOR** — Freeze the backend UI-gap ownership, compatibility, projection/backfill, Policy audit, Parcel history, Dashboard and Revenue semantics; remove stale Admin Operator, Trip code/index and fare-history scope; correct Platform Report ownership to Booking. |
| **1.47.0** | 2026-07-30 | BE lead (Vũ) | **MINOR** — Closes the Day-32 Parcel recovery crash/race gate with a dedicated persistent `TRANSFER|RETURN` cargo-recovery operation, one active claim per Parcel, frozen refund/target facts, stable Trip idempotency identity, five-minute stale replay, and atomic Parcel-local finalization of state, operation, Outbox and stats. |
| **1.46.0** | 2026-07-30 | BE lead (Vũ) | **MINOR** — Completes the implementation gate for Days 31/32/35/37/42: public Parcel delivery routing and hash-only rate limiting; encrypted Notification queue payloads; durable vehicle-transfer claim schema/recovery; exact outstanding Parcel refund identity; fail-closed per-Booking disruption snapshots; the approved `DISRUPT_ON_TRIP_DISRUPTED` timeline writer with null event actor; general-versus-Shuttle subscription guards; and exact 29/92-day report acceptance. Corrects legacy cargo formulas, status compatibility, schema/ERD inventory, error contexts, and Notification configuration ownership. |
| **1.45.0** | 2026-07-30 | BE lead (Vũ) | **MINOR** — Reconciles Days 31/32/35 contracts: Parcel delivery confirmation stores only SHA-256 token history and sends sensitive links directly through Notification internal email; Trip cancellation and disruption use canonical routing keys plus service-local refund/cargo recovery; Trip owns an atomic source-to-target cargo-transfer API; no-substitution refunds are computed per Booking with nearest-VND `AwayFromZero`; and Parcel vehicle-substitution transfer uses crew confirmation with a replay-safe 30-minute CAS escalation. |
| **1.44.0** | 2026-07-25 | BE lead (Vũ) | **MINOR** — Day-34 / SCV-114 freezes Vehicle Substitution: adds substitution-only `409 TRIP_NOT_SUBSTITUTABLE` while preserving existing `422 TRIP_NOT_IN_PROGRESS`, both public HTTP contracts and the exact Booking impact seam without `seatType` (Trip derives preferred type from the old TripSeat and deterministically falls back when absent), canonical Trip/Booking event payloads and Outbox identity, nullable seat history plus BookingTransfer confirmation persistence, and Notification recipient/suppression rules. Parcel transfer behavior remains deferred to Day 35. |
| **1.43.1** | 2026-07-24 | BE lead (Vũ) | **PATCH** - Register the signed, read-only `GET /v1/payments/vnpay-return-status` browser-return poll endpoint. The HTTPS bridge can display persisted Payment status and open the Passenger deep link, while VNPay IPN remains the only source allowed to mutate Payment and publish downstream confirmation events. |
| **1.43.0** | 2026-07-23 | BE lead (Vũ) | **MINOR** - Align Day-33 ROUTE_CHANGE timeout with technical context §6.4: persist frozen shuttle-fallback metadata, keep Booking CONFIRMED without automatic refund, publish `booking.booking.route_change_auto_fallback_applied` for Notification, and preserve the recurring five-attempt `RefundFailureRetryJob` lifecycle by persisting the initial consumer failure at `retryCount=0`. |
| **1.42.0** | 2026-07-23 | BE lead (Vũ) | **MINOR** - Correct the Day-33 `trip.trip.route_changed` contract to carry lifecycle status and immutable per-booking candidate-stop snapshots, remove duplicate `affectedBookingIds`, and freeze Booking ROUTE_CHANGE creation/resolution without synchronous Trip lookup. |
| **1.41.0** | 2026-07-23 | BE lead (Vũ) | **MINOR** — Complete VNPay HTTPS return bridge and Passenger deep-link/App Links configuration; keep IPN as the only payment state transition source with probe/signature diagnostics; expand Firebase client-token purposes and owner-scoped Storage Rules to vehicle, operator logo, parcel, incident, and avatar uploads; add avatar persistence endpoint and Firebase URL ownership validation. |
| **1.40.0** | 2026-07-23 | BE lead (Vũ) | **MINOR** - Day-33 ratifies operator Trip cancellation preview/confirm and AlternativeRoute disruption contracts, completes the `trip.trip.route_changed` event identity and producer/consumer registry, and documents nullable Trip-to-AlternativeRoute storage. Payment refunds remain driven only by `booking.booking.cancelled`. |
| **1.39.0** | 2026-07-22 | BE lead (Vũ) | **MINOR** — Day-29 freezes the assistant Parcel load HTTP contract, registers `trip.cargo.threshold_crossed`, and reconciles Parcel `loaded` direct recipients plus `auto_rejected` sender identity. No schema or migration change. |
| **1.38.1** | 2026-07-21 | Codex | **PATCH** - Expose persisted TripStop `status`/`actualArrivalTime` and Trip `destinationArrivedAt` through the protected public Trip detail projection; no schema, event, Gateway, or lifecycle change. |
| **1.38.0** | 2026-07-20 | BE lead (Vũ) | **MINOR** — Add Identity-owned Firebase Custom Token issuance for active `OPERATOR_ADMIN` users under active approved operators, transactional lock/suspend Firebase-session revocation, vehicle-image Storage Rules and credential registry; add owner-scoped Booking history with Ticket summaries, sender-only Parcel history, and the branch-selective Parcel-owned `GET /v1/passenger/history?type=TICKET\|PARCEL` facade. No schema or migration change. |
| **1.37.0** | 2026-07-18 | BE lead (Vũ) | **MINOR** — Freeze Day-24 stop-disable, passenger STOP_DISABLED choices, strict deadline fallback, Trip snapshot/pending-count/departure seams, no-show anchors/history source, event identity/consumer facts, and the two migration ownership rows. DELETE is the sole disable route; legacy synchronous `STOP_DISABLED_BOOKING_AFFECTED` warning/count behavior is deprecated for that route. |
| **1.36.0** | 2026-07-18 | BE lead (Vũ) | **MINOR** — Day-23 schedule-change contract, projection, errors, events, and jobs; merged into the Day-40 baseline while preserving the Admin Users, Station Cleanup, and Platform Reports contracts. |
| **1.35.0** | 2026-07-16 | Senior Backend Engineer | **MINOR** - Freeze Day 40 Admin Users + Station Cleanup + Platform Reports: shared-idempotent lock/unlock với PostgreSQL per-user serialization và `locked_from_status`; immutable ActivityLog; atomic Station normalize/merge cùng canonical redirects và Booking advisory-lock relink protocol; `trip.station.merged`/`normalized`; live UTC earned-report internal sources và Payment orchestration; đăng ký `STATION_MERGE_CONFLICT`/`REPORT_VALUE_OVERFLOW`; report cache/Stats/Excel và advanced analytics defer Day 42. |
| **1.34.1** | 2026-07-16 | BE lead (Vu) | **PATCH** - Merge the Day-22 Trip edit/effective-pricing/DriverSchedule cascade contracts into the current Day-39 baseline. Preserve trusted Booking impact, immutable Booking pricing/refund snapshots, fare-source overlap guard, Trip/schedule audits, Booking-owned passenger impact and Hangfire re-alert semantics; reconcile them with idempotency v2 and Day-38 Payment terminal-settlement consumption. |
| **1.34.0** | 2026-07-16 | Senior Backend Engineer | **MINOR** - Day 39 Incident vertical slice: thêm canonical assigned Driver/Assistant Incident API, persistence + transactional `trip.incident.reported` Outbox, validation/normalization category-description-photo-GPS; Notification resolve active `OPERATOR_ADMIN` cùng operator, fan-out in-app/push với retry, payload-event dedupe và PII-safe logging. |
| **1.33.0** | 2026-07-15 | Senior Backend Engineer | **MINOR** - Day 39 Parcel delivery hardening: tách canonical `IN_TRANSIT -> UNLOADED` và `UNLOADED -> DELIVERED_PENDING_CONFIRM`; terminal-bound dùng `destinationArrivedAt`, stop-bound dùng đúng matching arrived stop; token chỉ sinh ở deliver, cargo release chỉ ở unload; CAS loser không phát Outbox hoặc release lần hai; chuẩn hóa endpoint, error và event contracts. |
| **1.32.0** | 2026-07-15 | Senior Backend Engineer | **MINOR** - Day 39 Driver arrival hardening: chuyển stop-arrival sang canonical Driver/Assistant route, assignment authorization, ambient transaction và lock order `Trip -> TripStop`; thêm one-shot destination-terminal anchor độc lập Trip completion, internal snapshot field, typed `trip.stop.arrived`/`trip.destination.arrived` Outbox contracts và migration reversible. |
| **1.31.0** | 2026-07-15 | Senior Backend Engineer | **MINOR** - Day 39 idempotency v2 baseline: fingerprint `sub + method + PathBase/Path + canonical query + raw body` bằng length-prefix framing; tách hashed response/processing namespace; processing lock `SET NX EX` 120 giây và owner-safe complete/release; replay giữ status/body/content type; legacy cache fail closed; đăng ký exact `IDEMPOTENCY_KEY_REQUIRED`. |
| **1.30.0** | 2026-07-14 | Senior Backend Architect | **MINOR** - Freeze Day 38 Revision 6 contracts: trusted Payment context and legacy phased backfill; Driver/Assistant Trip completion; OperatorWallet subscription debit; per-Trip settlement failure/recovery; Invoice number/PDF retry/download; operator/admin APIs; canonical Invoice/settlement/terminal event payloads and new error codes. No bank withdrawal, e-invoice provider or booking/parcel platform fee. |
| **1.29.0** | 2026-07-14 | BE lead (Vu) | **MINOR** - Freeze Day-21 Trip lifecycle contracts: no-body/idempotent Driver start and Driver/Assistant manual complete endpoints with exact ADR-0004 DTOs and assignment authorization; recurring 15/5/15-minute boarding/start/complete jobs with T-30/departure+30/ETA+30 thresholds and no GPS-primary trigger; retain `trip.trip.started`/`trip.trip.completed` payloads; register `TRIP_INVALID_TRANSITION`, Booking history source `COMPLETE_ON_TRIP_COMPLETED`, and append-only Trip-local manual-completion audit schema/action/atomicity. No implementation, dependency, Gateway, migration, or event-key change. |
| **1.28.0** | 2026-07-14 | BE lead (Vu) | **MINOR** - Add operator/admin Station and Stop update-disable APIs, Stop-disable Trip Outbox to Booking pending-action and enriched Notification flow, enriched Trip detail Stop projection, PII-free booking seat requests, VNPay GET IPN and ready-to-fill sandbox configuration, 10-minute payment timeout, and VNPay `BOOKING_GROUP` confirmation/expiration support. |
| **1.27.0** | 2026-07-13 | Senior Backend Architect | **MINOR** - Day 36 Shuttle Backend v1: đăng ký REST/event/error contracts, Booking shuttle intent và cutoff T-30, operator subset dispatch, warning T-120/T-60, auto-cutoff, notification và Tracking Phase 11. Thêm ba bảng shuttle Trip, một bảng intent Booking, ba notification types và real-stack E2E acceptance. |
| **1.26.0** | 2026-07-12 | BE lead (Vu) | **MINOR** - Add the assignment-scoped Driver/Assistant Route geometry read `GET /v1/driver/trips/{tripId}/route`. The endpoint returns the main Route's nullable Google precision-5 `pathPolyline`, origin/destination Station coordinates, and ordered TripStop coordinates only when JWT `sub` is assigned as the Trip driver or assistant. Reuses existing `TRIP_NOT_FOUND`, `FORBIDDEN`, `VALIDATION_ERROR`, `/v1/driver` Gateway role gate, and existing schema; no migration, dependency, event, or operator-route permission change. |
| **1.25.1** | 2026-07-11 | BE lead (Vu) | **PATCH** - Day-19 shared validation-policy correction: model-binding failures (malformed JSON, missing non-nullable body field, type mismatch) now return the ADR 0004 `ApiResponse` error envelope with `422 VALIDATION_ERROR`, matching FluentValidation failures; they are no longer documented as HTTP 400. |
| **1.25.0** | 2026-07-11 | BE lead (Vu) | **MINOR** - Freeze the Day-19 tenant-scoped operator booking-monitor contract. Register the exact Identity raw phone-to-user lookup and exhaustive Booking error/retry boundary; broaden existing `UPSTREAM_UNAVAILABLE` to generic downstream/inter-service unavailability without adding an error code; replace the proposed Outbox-audit timeline with authoritative append-only `booking_status_history`, including schema, six current source constants, actor/reason rules, atomic writer/no-op semantics, no backfill/event, and deterministic ordering. |
| **1.24.0** | 2026-07-10 | BE lead (Vu) | **MINOR** - Allow `PASSENGER` accounts in `PENDING_EMAIL_VERIFICATION` to login and receive a normal `TokenBundleDto` for the mobile restricted session; FE gates features using `data.user.status`. Non-passenger pending-email users still fail with `AUTH_EMAIL_NOT_VERIFIED`. Gateway explicitly exposes `POST /v1/auth/resend-verification-email` as public alongside the existing public forgot/reset password endpoints. No DDL, dependency, migration, or event-key change. |
| **1.23.0** | 2026-07-09 | BE lead (Vu) | **MINOR** - Add public Identity password reset for all `ACTIVE` user roles. `POST /v1/auth/forgot-password` issues a generic response and sends a `PASSWORD_RESET` OTP only for eligible accounts; `POST /v1/auth/reset-password` consumes the OTP, hashes the new password, and revokes active refresh tokens with `PASSWORD_RESET`. No DDL, dependency, or event-key change; reuses `email_verification_tokens`, `identity.otp.requested`, and Redis `identity:pwd_reset_rate:{email}`. |
| **1.22.0** | 2026-07-09 | BE lead (Vu) | **MINOR** - Add operator-managed Google precision-5 path geometry for Route and AlternativeRoute. Register two `PUT .../geometry` endpoints, nullable `path_polyline` storage, validation/error codes, safe invalidation after route-shape edits, and Trip internal route-geometry preference with TripStop fallback. No new event, dependency, Gateway prefix, or Idempotency-Key requirement. |
| **1.21.1** | 2026-07-09 | BE lead (Vũ) | **PATCH** — Voucher list ownership split. `GET /v1/admin/vouchers` is no longer an all-voucher/operator-oversight list: it returns platform vouchers only (`owner_operator_id IS NULL`) and ignores/does not expose `ownerOperatorId` as a client filter; it keeps `fundingType`, `isActive`, paging and sort filters. Add `GET /v1/operator/vouchers` under the existing Booking/Gateway operator-voucher prefix for `OPERATOR_ADMIN`; Booking takes `operatorId` from the JWT claim and returns only `owner_operator_id = caller.operatorId`, with `isActive`, paging and sort filters. Both management list endpoints return voucher applicability config (`minOrderAmount`, `maxDiscountAmount`, limits, `newUserOnly`, `applicableServices`, `applicablePaymentMethods`, `applicableOperatorIds`, `applicableRouteIds`) so FE can distinguish `BOOKING` vs `PARCEL` vouchers. Admin-created `OPERATOR_FUNDED` vouchers remain platform-owned (`owner_operator_id IS NULL`) and continue the consent fan-out semantics. No DB schema or Gateway route-table change. |
| **1.21.0** | 2026-07-08 | BE lead (Vu) | **MINOR** - Add Trip-owned `Location` catalog for FE origin/destination search. Public `GET /v1/locations` supplies cacheable active locations; `SYSTEM_ADMIN` manages `/v1/admin/locations`; `GET /v1/trips/search` supports `originLocationCode`/`destinationLocationCode` while keeping station-id search; Station/Stop create accept `locationId` or `locationCode`; `GET /v1/stations/search` is public for passenger/FE autocomplete. Register Location error codes `LOCATION_NOT_FOUND` and `LOCATION_CODE_CONFLICT`. |
| **1.20.0** | 2026-07-06 | BE lead (Vu) | **MINOR** - Split Booking order from per-seat Ticket. Booking remains the order/history aggregate; Ticket is the proof of travel and QR identity (`ticketCode` format `VT-yyyyMMdd-XXXXXXXX`), linked 1:1 with Passenger boarding records. Register Booking internal snapshot `GET /internal/v1/bookings/{id}` for Parcel with active ticket count, extend booking integration event payloads with optional `bookingCode`, `ticketCodes`, and `ticketCount`, and add boarding errors `TICKET_NOT_FOUND` / `TICKET_NOT_BOARDABLE`. |
| **1.19.0** | 2026-06-30 | BE lead (Vũ) | **MINOR** — Freeze the Day-18 boarding-warning integration-event contract: register `trip.stop.departed_with_pending` with payload `{ eventId: Guid, occurredAt: DateTime (UTC), eventType: "trip.stop.departed_with_pending", tripId: Guid, stopId: Guid, stopName: string, pendingPassengerCount: int (> 0), driverUserId: Guid, assistantUserId: Guid?, departedAt: DateTimeOffset (UTC ISO-8601) }` for the Notification-owned Driver App alert consumer. `eventType` is the constant routing key; `occurredAt` matches `IntegrationEventBase` serialization, while `departedAt` is serialized as UTC. Day 18 is registry/contract only; the Trip Outbox emitter and the Day-24 `NO_SHOW` detection flow remain explicitly deferred to Day 24. No service code, handler wiring, test, or DDL change. |
| **1.18.0** | 2026-06-30 | BE lead (Vũ) | **MINOR** — Day-18 additive extension of the FROZEN Trip→Booking `TripSnapshot` inter-service DTO: append nullable `DriverUserId` and `AssistantUserId` (`Guid?`) without removing, reordering, or retyping existing fields. The mirrored `GET /internal/v1/trips/{tripId}` raw-DTO contract now exposes `driverUserId`/`assistantUserId` as logical user keys for downstream trip-assignment authorization; no cross-database FK or EF relationship is introduced. |
| **1.17.0** | 2026-06-26 | BE lead (Vũ) | **MINOR** - Day-17 BookingStats consumer ownership correction + event-driven counters. Booking self-consumes `booking.booking.confirmed`/`.cancelled`/`.refunded` to maintain BookingStats, so the stale `Trip (BookingStats counter)` consumer entry is corrected to Booking ownership. `booking.booking.cancelled` payload registry now includes `userId`; `booking.booking.refunded` registry now matches the emitted shape `{ bookingId, userId, amount }` (the money field is `amount`, not `refundAmount`). BookingStats consumers use a durable `booking_stats_processed_events` marker keyed by `(event_type, booking_id)` in the same local transaction as the stats UPSERT. |
| **1.16.0** | 2026-06-24 | BE lead (Vũ) | **MINOR** — Day-16 booking-payment + refund implementation notes (no new error codes — `PAYMENT_INSUFFICIENT_WALLET`/`PAYMENT_ALREADY_PROCESSED`/`PLATFORM_WALLET_INSUFFICIENT_BALANCE`/`REFUND_FAILURE_PERSISTED`/`REFUND_RETRY_EXHAUSTED` already §5.9; no new event keys — `payment.payment.succeeded`/`.failed`/`.expired`/`payment.wallet.credited` already §7.3). (1) **Option A+/Hybrid WALLET confirm** (human decision, anh Vũ): the WALLET booking charge path (`POST /internal/v1/payments/charge`) ALSO publishes `payment.payment.succeeded` via Outbox in the same charge transaction, making the event the canonical/recovery channel; the Booking-side `payment.payment.succeeded` consumer is the recovery path for BOTH WALLET (charged-but-not-confirmed crash window) and VNPay (normal async confirm), idempotently confirming any BOOKING still PENDING_PAYMENT. This now substantially **aligns** with §8.1 (CONFIRMED driven by the event for all methods); the only residual divergence is that the WALLET happy-path confirm stays synchronous in-request for UX (FE gets `status=CONFIRMED` in the 201 via the Day-13 saga). (2) **`PaymentExpiredJob` is a recurring Hangfire scan**, not the §10.1-labelled `Scheduled (per Payment)` per-creation delayed job — it scans PENDING_REDIRECT VNPAY BOOKING payments older than 15 min and emits `payment.payment.expired`, mirroring the shipped Day-15 `TopUpExpiredJob` recurring pattern. (3) **db-schema/payment-wallet/schema.sql `refund_failure_logs`** extended with retry-payload columns `user_id`/`amount`/`reference_type`/`reference_id` + partial index `idx_refund_failure_logs_reference` so `RefundFailureRetryJob` can re-invoke the wallet-refund path; the job scans only retriable rows (`resolved_at IS NULL AND retry_count < 5`) and leaves exhausted rows unresolved for Admin manual handling. (4) **§8.4 Payment→REFUNDED now implemented**: Payment consumes its own canonical `payment.wallet.credited` (queue `payment.payment-refunded`) and atomically drives the SUCCEEDED Payment row for `referenceType ∈ BOOKING_REFUND/PARCEL_REFUND` to REFUNDED (status-guarded, idempotent). No DB migration for this consumer (reuses existing `payments` columns). |
| **1.15.0** | 2026-06-22 | BE lead (Vũ) | **MINOR** — Identity → Notification transactional email goes live over synchronous internal HTTP. (1) **§7.2 Notification**: replace the stale "KHÔNG có internal endpoint" note with the `POST /internal/v1/emails` registry row (Internal-JWT only, `202`, ADR 0004 envelope; body `{notificationId?,toEmail,templateKey,templateData}`; OTP/set-password URL are sensitive — never persisted to `outbox_messages` or logged, scrubbed by the email-sensitive-data helper; OTP intentionally NOT routed through an Outbox event). (2) Confirm the two template keys consumed end-to-end — `AUTH_OTP` `{code (alias otpCode),purpose REGISTRATION\|PASSWORD_RESET,ttlMinutes}` and `SET_INITIAL_PASSWORD` `{userId,displayName,setInitialPasswordUrl (alias setPasswordUrl),expiresAt}`; both already exist in the Notification prisma `EmailTemplateKey` enum (no migration), renderer extended to read the Identity field names with backward-compatible aliases. (3) **New env** — Identity `EMAIL_PROVIDER` (`SENDGRID`=real delivery via Notification, container/prod default; `LOG`=log-only local-dev default) + `EMAIL_SERVICE_BASE_URL` (`http://notification:3002`, §3.5); added to `.env.example` + identity docker-compose. Identity adds NO SendGrid dependency (SendGrid stays Notification-only §1.2/§3.5); outbound Internal JWT reuses the per-service `InternalJwtTokenFactory` + shared `InternalJwtDelegatingHandler` (§5.3). The single `EMAIL_PROVIDER` DI swap covers all six Identity call-sites (Register, RegisterOperator, CreateOperatorUser, ResendInitialPassword, CreateAdminUser, CreateOperator). No new error codes, no event keys, no DDL change. |
| **1.14.0** | 2026-06-20 | BE lead (Vũ) | **MINOR** — Day-14 Task 14.2 addendum — add two §5.9 error codes `CONSENT_NOT_PENDING` (409) and `CONSENT_ALREADY_REJECTED` (409) for operator voucher-consent accept/reject state-machine preconditions (v7:665-683). No code/DDL change. |
| **1.13.0** | 2026-06-20 | BE lead (Vũ) | **MINOR** — Day-14 Task 14.0a canonical SOT edit (operator self-create vouchers, human-approved re-plan 2026-06-19). (1) **db-schema/booking/schema.sql `vouchers`**: add `name VARCHAR(120) NOT NULL`, `owner_operator_id UUID NULL` (logical FK identity.operators; NULL = platform admin voucher, NOT NULL = operator self-created tenant-scoped), `deleted_at TIMESTAMPTZ NULL` (soft-delete ADR 0003); add `chk_vouchers_operator_owned_funding CHECK (owner_operator_id IS NULL OR funding_type = 'OPERATOR_FUNDED'::voucher_funding_type)` (explicit `::voucher_funding_type` cast); change `uq_vouchers_code` to partial unique `WHERE deleted_at IS NULL` (soft-deleted code reusable); add `idx_vouchers_owner_operator ... WHERE owner_operator_id IS NOT NULL AND deleted_at IS NULL`; update `COMMENT ON COLUMN vouchers.created_by_user_id` to cover BOTH `SYSTEM_ADMIN` (platform) AND `OPERATOR_ADMIN` (operator-owned) + new COMMENT on `owner_operator_id` + `deleted_at`. (2) **technical_context_v7 sec 4.4** (v7:462, v7:573, v7:624, funding block v7:626-655): replace operator-cannot-create absolutes with operator self-create flow (operator-owned `OPERATOR_FUNDED`, full CRUD, self-consented — NO consent fan-out, NO event), document consent-skip + checkout applicability branch (Q8 canonical validation order — branch (a) `owner_operator_id == Trip.operatorId` bypasses consent + operator-scope only; branch (b) platform voucher uses VIETRIDE_FUNDED scope match OR OPERATOR_FUNDED ACCEPTED consent). Admin `OPERATOR_FUNDED` consent flow (v7:640-646) UNCHANGED. (3) **§5.9**: add 3 Voucher error codes — `VOUCHER_FORBIDDEN_FUNDING` (422), `VOUCHER_CODE_CONFLICT` (409), `VOUCHER_LOCKED` (409, Q6 freeze-on-first-use economic-field edit on used voucher). (4) **§5.6 idempotency**: add rows for `POST /v1/admin/vouchers` and `POST /v1/operator/vouchers` (POST creates only; PATCH/DELETE/activate/deactivate behavior-idempotent, no key). (5) **API Contract**: add admin + operator voucher endpoint section incl. `GET /v1/admin/vouchers` oversight list (SYSTEM_ADMIN, `?ownerOperatorId=`/`?fundingType=`/`?isActive=` filters, paged, read-only). No EF/migration/code in this task — SOT-only edit; downstream Tasks 14.0–14.5 implement code against this edited SOT. |
| **1.12.0** | 2026-06-15 | BE lead (Vũ) | **MINOR** — Day-13 round-trip checkout seams: (1) add Trip-owned internal `POST /internal/v1/trips/round-trip/lock-seats` to §7.2 for Booking round-trip checkout. The seam is Internal-JWT only, Idempotency-Key required, and locks outbound + return seat sets atomically in one Redis Lua script using Trip seat keys `seat_lock:{tripId}:{seatNumber}`; if either leg fails, no seat is held. Reuses existing error codes `TRIP_NOT_FOUND`, `BOOKING_TRIP_NOT_BOOKABLE`, and `BOOKING_SEAT_UNAVAILABLE`; no new event key. (2) add Payment-owned internal WALLET batch charge seam `POST /internal/v1/payments/batch-charge`; WALLET round-trip remains two per-booking Payment records (`payments.reference_type=BOOKING`) and two per-booking wallet debit ledger entries (`wallet_transactions.reference_type=BOOKING_PAYMENT`) committed all-or-nothing in one transaction; `BOOKING_GROUP` stays VNPay-only. Syncs §5.6, §7.2, `VietRide_API_Contract_v1.md`, and technical_context_v7 lines 1755-1757; no DB schema change. |
| **1.11.4** | 2026-06-15 | BE lead (Vũ) | **PATCH** — Day-11 audit closeout: register `IDEMPOTENCY_REQUEST_PENDING` (HTTP 409) in §5.9 for replay-safe seat-lock idempotency when the same key is still being processed. Also backfill the cumulative Postman/local harness Day-11 real-app E2E coverage separately (`postman:day11:local`); no new event keys and no DDL change. |
| **1.11.3** | 2026-06-13 | BE lead (Vũ) | **PATCH** — Day-11 Task 11.4 PLAN-REVIEW TTL config drift fix: add `SEAT_LOCK_TTL_MINUTES=10` to the Trip-Route-Vehicle env registry as the Trip-owned source for `SeatLock:TtlMinutes` / lock-seats `ttlSeconds` default 600s, while keeping the Booking-side line explicitly marked as client/default-only and not the Trip-owned registry. Docs-only; no code/DDL change. |
| **1.11.2** | 2026-06-13 | BE lead (Vũ) | **PATCH** — Day-11 Task 11.2-pre SOT/contract patch: document `PATCH /v1/operator/driver-schedules/{id}/activate` as an OPERATOR_ADMIN, no-body, no-Idempotency-Key, behavior-idempotent activation endpoint covered by the existing Gateway `/v1/operator/driver-schedules` prefix; activation-only scope excludes full DriverSchedule edit/cascade. Close the Day-9 carryover by changing DriverSchedule create/activation validation from deferred to Identity logical-FK role/operator validation: `driverUserId` must be `DRIVER` under caller operator and nullable `assistantUserId` must be `ASSISTANT` under caller operator, with mismatches mapped to `422 VALIDATION_ERROR`. Clarify internal `GET /internal/v1/users/{userId}` raw success DTO `{ id, role, operatorId, status }`, Internal-JWT-only, no Gateway exposure. No new error codes, no event keys, no DDL/code change. |
| **1.11.1** | 2026-06-12 | BE lead (Vũ) | **PATCH** — Identity operator-user list gap fill: add `GET /v1/operator/users` for `OPERATOR_ADMIN` only, scoped by caller `operatorId`, and `GET /v1/admin/operator-users` for `SYSTEM_ADMIN` across all operators. Both return ADR 0004 `ApiResponse<PagedResult<OperatorUserListItemDto>>` with items `{userId,email,phone,displayName,role,status,operatorId,createdAt,avatarUrl}`, only roles `DRIVER`/`ASSISTANT`/`OPERATOR_STAFF`, standard page/search/sort filters, no Idempotency-Key, no new error codes, no schema/migration change. Gateway adds `/v1/admin/operator-users` → Identity with `SYSTEM_ADMIN`; existing `/v1/operator/users` route remains `OPERATOR_ADMIN`. |
| **1.11.0** | 2026-06-12 | BE lead (Vũ) | **MINOR** — (1) **Money rounding rule change (human decision 2026-06-12):** bỏ floor về 1,000 VND — số tiền giữ đến đơn vị ĐỒNG; kết quả phép tính lẻ (giảm giá %, hoa hồng) làm tròn đến đồng gần nhất (`Money.FromDecimal`, MidpointRounding.AwayFromZero); `Money.FromRaw` pass-through. Sửa §9.5, §4.4 Money row, §3.1 tree comment, `libs/dotnet/VietRide.Shared.Kernel/ValueObjects/Money.cs` + tests. technical_context_v7 đã được patch in-place cùng đợt (10 chỗ floor-1000: dòng ~1944/2015/2991/3418/4080/4126-4153/4367/4539) + API Contract 2 chỗ (~2394/2561) — SOT hết mâu thuẫn. Không cần DB migration (BIGINT giữ nguyên). (2) **Edit-pickup policy change (Day-13 OQ2, human decision 2026-06-12):** v1 KHÔNG cho đổi điểm đón làm thay đổi giá — edit-pickup chỉ hợp lệ khi giá mới = giá cũ (fareDelta=0); mọi chênh lệch (tăng HOẶC giảm) → 409; muốn đổi giá thì hủy vé + đặt lại (loại bỏ hoàn toàn nhánh refund-on-downgrade của technical_context_v7 lines 1639-1656 — erratum, business owner override). §5.9: rename `BOOKING_EDIT_PICKUP_PRICE_INCREASE` → `BOOKING_EDIT_PICKUP_PRICE_CHANGED` (chưa có code/FE nào dùng code cũ). |
| **1.10.0** | 2026-06-11 | BE lead (Vũ) | **MINOR** — SOT reconciliation patches (Day-11 Q2 / Day-12 C1,C2,CO2 / Day-13 C5): (1) §9.9 Redis key `booking:seat_lock:{tripId}:{seatNumber}` owner Booking → key `seat_lock:{tripId}:{seatNumber}` owner Trip (source: BSOT 1.8.0 + API Contract §`lock-seats`). (2) §5.6 idempotency table: split combined row `POST /v1/bookings/{id}/edit-pickup-dropoff` into two separate rows `POST /v1/bookings/{id}/edit-pickup` and `POST /v1/bookings/{id}/edit-dropoff` (source: API Contract lines ~830-885 defines two separate endpoints, higher precedence). (3) §7.2 Payment seam: `POST /internal/v1/payments/wallet-charge` → `POST /internal/v1/payments/charge` (source: API Contract line ~1565). (4) §9.10 + §9.1 logging example: BookingCode short-form `VR-<4 char base32>` → `VR-yyyyMMdd-XXXXXXXX` (date + 8-char base32 uppercase) (source: db-schema/booking/schema.sql COMMENT + API Contract line ~713). No code/DDL change. |
| **1.9.0** | 2026-06-11 | BE lead (Vũ) | **MINOR** — Day-9 Trip vehicle/schedule contract + registry sync: add the VehicleType catalog read, operator-scoped Vehicle CRUD, exact `seatLayoutJson` BE/FE shape and v1 validation scope, and DriverSchedule create contract with an `Asia/Ho_Chi_Minh` local-time weekly recurrence, validity window, conflict handling via existing `TRIP_DRIVER_CONFLICT`, no Trip generation, and Day-11 deferred driver/assistant role validation. Add exactly two new §5.9 tenant/reference codes: `VEHICLE_NOT_FOUND` and `VEHICLE_TYPE_NOT_FOUND`. No code/DDL change. |
| **1.8.0** | 2026-06-11 | BE lead (Vũ) | **MINOR** — Trip↔Booking seam freeze (unblocks parallel Day-12 Booking work). Document the seat lifecycle as **synchronous internal HTTP** owned by Trip-Route-Vehicle: `TripSeat` lives in the `trip-route-vehicle` schema (NOT Booking — corrects the BE_TIMELINE Day-12 "TripSeat tables" note, which loses to technical_context §6.1/§6.10 + db-schema), generated by the Trip Hangfire job from `Vehicle.seatLayoutJson`; Booking drives `lock-seats` (AVAILABLE→HELD, Redis `seat_lock:{tripId}:{seatNumber}` TTL 10 min, all-or-nothing) → `book-seats` (HELD→BOOKED) → `release-seats` (HELD→AVAILABLE, idempotent compensation); no event on the seat path. Reconcile §7.2 endpoint name `confirm-seats` → **`book-seats`** to match API Contract (#2 > #3). Flesh out the API contract: add the missing `GET /internal/v1/trips/{tripId}` raw-DTO shape (operatorId/routeId/baseFare/stops[allowPickup,allowDropoff,orderIndex,fareFromThisStop]/seatSummary) already registered in §7.2, and add error responses to the three seat endpoints. **No new error codes** (`BOOKING_SEAT_UNAVAILABLE` 409, `BOOKING_TRIP_NOT_BOOKABLE` 409, `TRIP_NOT_FOUND` 404 already §5.9). No code/DDL change. |
| **1.7.0** | 2026-06-10 | BE lead (Vũ) | **MINOR** — Day-8 Trip route contract + registry sync: add the Route/RouteStop/FareTemplate/AlternativeRoute section to the API contract with ADR 0004 envelopes, method-level role matrix (WRITE = `OPERATOR_ADMIN` only; READ = `OPERATOR_ADMIN` + `OPERATOR_STAFF`), tenant-isolation `404 ROUTE_NOT_FOUND`, RouteStop hard-delete, AlternativeRoute soft-deactivate, fare-template `fareFromThisStop` and effective-window rules, and the app-layer preconditions for Route create. Historically added `ROUTE_STOP_ORDER_CONFLICT`, `ROUTE_STOP_FLAGS_INVALID`, and `ALTERNATIVE_ROUTE_LIMIT_EXCEEDED`; the last code was retired in 1.55.0. No code/DDL change. |
| **1.6.5** | 2026-06-10 | BE lead (Vũ) | **PATCH** — Day-10 Outbox + passenger-stub contract sync. Add two stub endpoints to the API contract + Postman: `GET /v1/passenger/me` (reuses the `/v1/users/me` `GetMeResponseDto` projection verbatim — `id,email,displayName,phone,role,operatorId,status,avatarUrl`; no passenger-specific fields) and `GET /v1/passenger/bookings` (empty `PagedResult` envelope `{items:[],page:1,pageSize:20,total:0}` — booking ITEM schema deferred to Sprint 3 / [SCV-76](https://hoangvutran088.atlassian.net/browse/SCV-76)), both marked `stub -- item schema finalized in Sprint 3 (SCV-76 / Booking)`; both require a user JWT (401 without). Add Gateway route `/v1/passenger/*` → identity (authRequired `user`). Implement the three already-registered §7.3 events transactionally from Identity handlers (`identity.user.created {userId,role,email,createdAt}`, `identity.operator.approved {operatorId,approvedAt}`, `identity.operator.suspended {operatorId,suspendedAt}`) via the string-based `IIntegrationEventOutbox` seam; wire `AddVietRideMessaging` into Identity + set the identity container's `RabbitMq__HostName=rabbitmq` so the Outbox publishes to `vietride.events`. Add the placeholder Redis `IdempotencyMiddleware` to Shared.Web (not wired). **No new event keys, no new error codes** (`IDEMPOTENCY_KEY_MISMATCH` already §5.9; events already §7.3). `staff.password_set` intentionally NOT emitted (Q2: no registry row, no consumer — registry §7.3 > timeline). No schema/migration change (reuse existing `outbox_events`). |
| **1.6.4** | 2026-06-08 | BE lead (Vũ) | **PATCH** — Day-7 Trip Station/Stop contract sync: reconcile station autocomplete to `GET /v1/stations/search?q=` as a targeted endpoint-specific exception to §5.8 `search=` because `technical_context_v7` line 523 has higher priority; `q` is required and blank/empty `q` maps to `422 VALIDATION_ERROR`; document accent-insensitive `unaccent` contains matching, `pg_trgm` placeholder-only compatibility, duplicate-nearby Station warning shape (`STATION_DUPLICATE_NEARBY` 200 without ApiMeta changes), single `POST /v1/operator/stations` link/create branch, Stop CRU under `/v1/operator/stops` (without Day-7 `sharedSuggestion`/`shared_suggestion` mutation), no Day-7 `Idempotency-Key` requirement, Trip->Identity logical-FK failures mapping to `422 VALIDATION_ERROR`, non-APPROVED/inactive operator writes mapping to `403 FORBIDDEN`, internal station/stop raw DTO lookup with coded 404 error envelopes, and existing coded 404 use cases for `STATION_NOT_FOUND`/`STOP_NOT_FOUND`. No new error codes, no event keys. |
| **1.6.3** | 2026-06-07 | BE lead (Vũ) | **PATCH** — Day-6 Operator contract baseline: sync API contract/BSOT for operator self-register, System Admin manual-create, approve/reject/suspend POST action endpoints, operator-created user create/resend initial-password, operator profile GET/PATCH, and internal operator/subscription/usage endpoints. Ratify Day-6 decisions without adding new error codes, Idempotency-Key requirements, or Outbox emission: non-APPROVED operator login/write-action guards use `FORBIDDEN`; invalid lifecycle transitions use `VALIDATION_ERROR`; reject cancels `OperatorSubscription` without `deletedAt`; ActivityLog `user_id` stores actor user id with JSONB serializer metadata; Day 10 remains responsible for emitting `identity.operator.approved`/`identity.operator.suspended`. |
| **1.6.2** | 2026-06-06 | BE lead (Vũ) | **PATCH** — Clarify ADR 0004 convention for service-to-service HTTP: FE-facing `/v1/*` successes stay wrapped in `ApiResponse<T>`, but successful `/internal/v1/*` / `/internal/*` responses return raw DTO/list payloads; internal errors still use the standardized `ApiResponse` error envelope. |
| **1.6.1** | 2026-06-06 | BE lead (Vũ) | **PATCH** — Day-5 Identity contract sync: document FE-facing `SET_INITIAL_PASSWORD` consume/resend endpoints and user device-token POST/DELETE shapes in the API contract/Postman without adding new error codes, Idempotency-Key requirements, or Outbox events; record ActivityLog action additions for initial-password token generation/resend flows. Internal `GET /internal/v1/users/{userId}/device-tokens` registry row already exists in §7.2 and is intentionally not duplicated. |
| **1.6.0** | 2026-06-04 | BE lead (Vũ) | **MINOR** — §5.9 Auth error registry: add `AUTH_GOOGLE_TOKEN_INVALID` (HTTP 401) for invalid Google ID token signature/expiry/audience during Google OAuth login. |
| **1.5.1** | 2026-06-03 | BE lead (Vũ) | **PATCH** — §5.9 Generic error registry: add `UPSTREAM_UNAVAILABLE` (HTTP 502) for Gateway-generated downstream connection failures. This syncs the Day-3 Gateway ADR 0004 envelope fallback with the registry discipline. |
| **1.5.0** | 2026-06-01 | BE lead (Vũ) | **MINOR** — **ADR 0004: Adopt `ApiResponse<T>` envelope for all FE-facing HTTP responses.** Rewrite §5.4 (success shape) to envelope `{success,statusCode,message?,data,meta{traceId,timestamp}}`; rewrite §5.5 (error shape) — DROP `application/problem+json` (RFC 7807), adopt error envelope `{success:false,statusCode,error{code,message,fields?},meta}` với `error.code` từ §5.9 registry; rewrite §5.7 (Pagination) — introduce `PagedResult<T>` (7 fields: `items,page,pageSize,totalItems,totalPages,hasNextPage,hasPreviousPage`) + `QueryOptions` (`page/pageSize`-clamped-1..100/`search`/`searchIn`/`sortBy`/`sortDir`/`includeDeleted`); rewrite §5.8 (Filter conventions) — `sortBy`+`sortDir` SUPERSEDES `?sort=-field` convention + sortBy whitelist security requirement → reject non-whitelisted field với `400 INVALID_SORT_FIELD` (đăng ký §5.9 Validation group). §3.1 tree + §3.6 Api/Web layer: `ProblemDetailsExceptionFilter` → `ApiResponseExceptionFilter` + `ApiResponseResultFilter` (Task 3.8 target state). §3.1 tree: `PagedResult.cs` comment cập nhật 7-field shape. Bump 1.4.0 → 1.5.0 MINOR. API Contract wrapped accordingly. ADR 0004 follow-ups #1–#2. |
| **1.4.0** | 2026-06-01 | BE lead (Vũ) | **MINOR** — §5.9 Auth error registry: thêm code mới `AUTH_OTP_RATE_LIMIT_EXCEEDED` (HTTP 429) — OTP request rate limit hit (Redis `identity:otp_rate:{email}` max 3/h TTL 1h, BSOT §6.9 line 1545). Code này backs Day-3 OTP rate-limit path (Task 3.4 handler throws `TooManyRequestsException` → 429). Human decision B2 (plan v7.1 patch). Đồng thời ratify shared-lib edits từ blocked 3.4 attempt: `UnauthorizedException` (401) + `BadRequestException` (400) đã có trong `ApplicationExceptions.cs`; thêm mới `TooManyRequestsException` (429) vào same file + arm tương ứng trong `ProblemDetailsExceptionFilter`. |
| **1.3.6** | 2026-05-31 | BE lead (Vũ) | **PATCH** — §2.2 + §3.4 stack version: **NestJS 10.x → 11.x** để khớp `package.json` (`@nestjs/core`/`@nestjs/common` = `^11.0.0`) — đây là thực tế đã cài, doc bị stale. `package.json` là source-of-truth cho version chính xác. Đồng bộ ghi chú trong `.claude/agents/nest-worker.md` + `nest-reviewer.md` (bỏ workaround "BSOT §2.2 still says 10.x"). No code/DDL change. |
| **1.3.5** | 2026-05-31 | BE lead (Vũ) | **PATCH** — **Decouple soft-delete from activation flag** per ADR 0003. Soft-delete = `deleted_at timestamptz` only (marker `ISoftDeletable`, getter-only `DeletedAt`). `is_active boolean` is a SEPARATE activation toggle (marker `IActivatable`, getter-only `IsActive`) — NOT part of soft-delete. `User` has no `is_active` (uses `status` enum). Cập nhật: §4.4 datatype list, §9.6 Soft delete, §3.1 monorepo tree (BaseEntity.cs comment), §3.6 Domain layer table. No DDL change — schema was already correct. |
| **1.3.4** | 2026-05-31 | BE lead (Vũ) | **PATCH** — §5.9 error registry: thêm **User** group với `USER_INVALID_STATUS_TRANSITION` (HTTP 422, domain/Identity, thrown by `User.VerifyEmail()` guard). Error code đã được dùng trong Task 3.1 nhưng chưa đăng ký registry — patch này sync lại. |
| **1.3.3** | 2026-05-26 | BE lead (Vũ) | **PATCH** — (1) **Bỏ folder `DOC/` uppercase**, gom toàn bộ generated artifacts (`openapi/`, `deliverables/`) vào `docs/` lowercase để chỉ có 1 folder doc duy nhất, tránh confusion 2 folders `DOC/` vs `docs/`. Cập nhật Section 3.1 monorepo tree + Section 3.1 folder semantics table. (2) **DTO mapping: AutoMapper → Mapster 7.x**. Lý do: Mapster source-gen compile-time (không reflection runtime), license MIT (AutoMapper từ v13+ commercial), perf benchmark ~3x faster, ít allocation. Vẫn **OPTIONAL** — manual mapping static factory / extension method là default. Cập nhật Section 2.1 + Section 3.2.3 anti-pattern row. Anti-pattern `IMapper` AutoMapper toàn project vẫn áp dụng cho Mapster — bắt buộc `TypeAdapterConfig` per Aggregate nếu dùng. (3) **Move 4 `<svc>-e2e/` folders từ `apps/` → `tests/`** để `apps/` chỉ chứa deployable units. Break Nx generator default (sibling layout) nhưng cleaner mental model: per-app HTTP e2e (Jest + axios) ở `tests/<svc>-e2e/`, cross-service e2e ở `tests/e2e/`, load test ở `tests/load/`. Khi `nx g @nx/nest:app foo` tạo `apps/foo-e2e/` thì manual move về `tests/foo-e2e/` + fix `jestConfig` path trong `project.json` + update `nx.json` jest plugin exclude paths. |
| **1.3.2** | 2026-05-25 | Senior Backend Architect | **PATCH** — Section 3.4 mở rộng: thêm **3.4.1 Tech stack rationale** giải thích **KHÔNG dùng Kong / YARP / Tyk / Express Gateway / AWS APIM / Nginx-only** + lý do reject từng option + ✅ stack chính xác đã chọn (NestJS + `http-proxy-middleware` + `jose` + `ioredis`). Thêm **3.4.2 Routing approach** với route table config-driven (KHÔNG controller per endpoint trừ health + VNPay IPN exception) + middleware chain order (Cors → RequestId → RateLimit → RouteMatcher → UserJwtVerify → RoleCheck → PhoneCompleteGate → InternalJwtSigner → ProxyForwarder). Folder layout đổi tên thành 3.4.3. Quyết định canonical source vẫn là `technical_context_v7.md` Section 3.2 — doc này chỉ tổng hợp lại + giải thích lý do reject các option khác cho agent. |
| **1.3.1** | 2026-05-25 | Senior Backend Architect | **PATCH** — Thêm callout đầu Section 3 + reminder ở 3.1 / 3.2.1 / 3.3: **file structure trong doc CHỈ là ví dụ minh họa**, KHÔNG phải danh sách bắt buộc. Agent được phép tạo thêm file/folder mới (vd `Sagas/`, `Specifications/`, `BookingRefundService.cs`), bỏ file/folder không cần (Gateway không cần `Domain/`, Notification không cần `Outbox/`), rename `<Aggregate>` placeholder theo domain thật, gom hoặc tách file theo balance philosophy 3.2.3. Liệt kê 5 điều agent KHÔNG được phép thay đổi (naming convention 3.5, dependency direction 3.2.2, anti-pattern 3.2.3, domain leak vào libs, infra cụ thể leak vào libs). Thêm 5-step "nguyên tắc khi quyết định tạo file mới" cho agent. |
| **1.3.0** | 2026-05-25 | Senior Backend Architect | **MINOR** — **Balance SOLID, clarify libs boundaries** (user feedback). (1) Section 3.2.3 anti-pattern checklist viết lại với triết lý **balance** thay vì cứng nhắc: SRP để dễ đọc/test/sửa, KHÔNG để fragment file vụn. Số liệu (10 method, 80 dòng) chỉ là **mốc tham khảo** review thảo luận, KHÔNG hard limit CI fail. Pragmatic thresholds: Service 10–20 method OK, Repository 10–15 method OK, Handler 80–150 dòng OK, file C# 200–400 dòng OK; vượt nhiều mới review. "Khi nghi ngờ → ưu tiên gom (less files), tách sau khi có pain point". Cờ đỏ rõ ràng (god class trộn concern, swallow exception, controller chứa business logic, ...) vẫn reject PR. (2) Section 3.6 thêm callout rõ: **libs/ KHÔNG có Domain nghiệp vụ** (Booking/Trip/Parcel/... sống trong apps/<service>/Domain/) — lib chỉ có shared kernel primitives (Money, Result<T>, BaseEntity); **libs/ KHÔNG có Infrastructure cụ thể** (ApplicationDbContext, EntityTypeConfiguration, migration, VnPayClient, TripServiceClient... sống trong apps/<service>/Infrastructure/) — lib chỉ có generic helpers (EfRepository<T,TId> base, interceptors, RabbitMQ wrapper, Polly factory). (3) Section 3.6 quy tắc IN/OUT redesign — bảng phân loại theo **Layer** (Domain / Application / Infrastructure / Api-Web / NestJS Common / NestJS Infrastructure / Contracts) với 2 cột "lib generic" vs "app service-specific". (4) Section 12.7 CI file-size lint chuyển thành **warn-only**, threshold nới (C# > 500 dòng, TS > 400 dòng, handler > 200 dòng) — review thảo luận, không auto-reject. |
| **1.2.0** | 2026-05-25 | Senior Backend Architect | **MINOR** — **Khôi phục Repository + Service abstractions** (user feedback). Doc v1.1.0 lỡ remove generic `IRepository` / `IService` do hiểu nhầm — v1.2.0 thêm lại: (1) `IRepository<TEntity, TId>` generic base + `EfRepository<T,TId>` impl trong `libs/dotnet/VietRide.Shared.Application` + `VietRide.Shared.Persistence`; (2) per-aggregate `I<Aggregate>Repository extends IRepository<>` + `<Aggregate>Repository` impl trong `apps/<service>/`; (3) per-aggregate `I<Aggregate>Service` + impl trong Application layer cho shared business logic cross-handler; (4) `IUnitOfWork` optional. **Pipeline:** Controller → MediatR.Send → Handler → IService (optional) → IRepository → DbContext. MediatR vẫn là entry point từ Controller (per technical_context_v7). **Section 3.6 mới — Shared libs philosophy** trả lời rõ "libs/ chứa gì, apps/ chứa gì": libs cho generic patterns + cross-cutting + infrastructure helpers (IRepository base, EfRepository, Money, Outbox publisher, JWT handlers, exception filter, Polly factory, RabbitMQ wrapper, NestJS guards + pipes); apps cho domain-specific (entity, business logic, per-aggregate repo extending generic, service-specific external impl). Versioning rule: lib breaking change → sync all consumers cùng PR. Update Section 3.2.3 anti-pattern checklist (god service/god repo cảnh báo, repository contains business logic, controller bypass MediatR, service inject DbContext direct, service-inject-service chain, passthrough service). Section 3.2.4 + 3.2.5 đầy đủ code example cho Repository + Service. Section 3.5 naming conventions thêm rows IRepository/IService. Section 5.10/5.11 update data access + transaction pattern. Section 12.6 mock pattern. Section 12.7 CI gate đổi NetArchTest assertion (Controller không inject IRepository/IService trực tiếp). |
| **1.1.0** | 2026-05-25 | Senior Backend Architect | **MINOR** — Monorepo chuyển sang **Nx layout** (`apps/`, `libs/shared/*`, `libs/dotnet/*`, `infra/`, `docs/`, `DOC/`, `scripts/`, `tests/`). Bỏ folder `services/` → `apps/`. Thêm Section 3.1 top-level folder semantics + Nx file map (`nx.json`, `tsconfig.base.json`, `global.json`, `jest.preset.js`). **Strict OOP/SOLID/SRP** cho .NET (Section 3.2): bỏ `IRepository`, `IUnitOfWork`, `IService` abstraction layer; Command/Query Handler inject `ApplicationDbContext` trực tiếp; query phức tạp dùng `IQueryable<T>` extension method thay vì repository wrapper. Thêm Section 3.2.3 **anti-pattern checklist** (god class, generic repo, service-trong-service, controller-business-logic, nullable disable, async without CancellationToken, ...) + Section 3.2.5 "khi nào ĐƯỢC tạo interface" (chỉ external boundary). NestJS apps update sang Nx-generated layout + `apps/<service>-e2e/`. Update CI gate dùng `nx affected` + anti-pattern grep lint. Update mock conventions dùng external client interface concrete (`IVnPayClient`, `ISendGridEmailClient`, ...) thay vì `IEmailProvider` generic. |
| **1.0.0** | 2026-05-25 | Senior Backend Architect (initial) | Khởi tạo Backend Source of Truth. Cover Section 0–12 — service map, project structure, DB ref, API conventions, auth/JWT, inter-service comm, status machines, cross-cutting, background jobs, env config, testing. Sync với `SU26SE101_VIETRIDE_technical_context_v7.md` + `Docs/API/VietRide_API_Contract_v1.md` + `db-schema/_global/*`. |

---

## Appendix A — TBD / Open Questions

> Liệt kê các gap được phát hiện khi tổng hợp — cần user/team decide trước khi backend implement. KHÔNG tự suy diễn business logic.

| # | Topic | Status | Decision needed |
|---|---|---|---|
| A.1 | Data normalization engine (System Admin "flag dữ liệu sai") | TBD — out of scope v1 | technical_context Section 4.4 ghi rõ defer v2. KHÔNG implement. |
| A.2 | API DTO contract detail per endpoint | Pending sync | `Docs/API/VietRide_API_Contract_v1.md` mới cover Identity → cần expand cho 8 services. Backend agent có thể start scaffold dựa trên Section 5 + technical_context. |
| A.3 | TripDelayed event publisher (Tracking vs Trip service) | Decision needed | technical_context Section 6.3 gợi ý Tracking publish — nhưng Tracking đã có Outbox? Confirm: Tracking publish trực tiếp qua Outbox riêng (đã có `vietride_tracking.outbox_events` per Section 4.2). |
| A.4 | Per-operator shuttle opt-in trên cùng Station | Defer v2 | technical_context 4.4: v1 toggle là property của Station canonical. v2 thêm `OperatorStation.shuttleEnabled`. |
| A.5 | Internal JWT secret rotation procedure | Open | v1 chỉ có 1 secret. Rotation cần redeploy đồng bộ — viết runbook khi cần. |

---

## Appendix B — Quick Reference Card

### Khi scaffold service mới (`<service>`)

1. **Đọc:**
   - `BACKEND_SOURCE_OF_TRUTH.md` Section 1.2 (DB name) + 3 (project layout) + 5 (API) + 6 (auth).
   - `SU26SE101_VIETRIDE_technical_context_v7.md` Section 8 → Entity Requirements per Service → tìm phần dành cho service đó.
   - `db-schema/<service>/schema.sql` để biết exact DDL → sinh EF Core / Prisma entity match.
   - `db-schema/_global/cross-service-references.md` để biết logical FK cần HTTP validate.

2. **Tạo solution:**
   - **NestJS:** `nx g @nx/nest:app <service>` → Nx tạo `apps/<service>/` + `apps/<service>-e2e/`. Reshape theo Section 3.3.
   - **.NET:** tạo folder `apps/<service>/` + `dotnet new sln -n VietRide.<Service>` + 4 project (Api/Application/Domain/Infrastructure) theo Section 3.2.1. Thêm `apps/<service>/project.json` (Nx) với target `build`/`test`/`lint` gọi `dotnet` command tương ứng (qua `@nx-dotnet/core` plugin).
   - Thêm reference Nx `tsconfig.base.json` (cho lib aliases) hoặc `global.json` (pin .NET SDK) nếu chưa có.

3. **Wire up:**
   - JWT auth middleware (Section 6.4).
   - Exception filter + `ApiResponse` error envelope (Section 5.5).
   - Validation pipeline (Section 9.3).
   - Outbox + RabbitMQ producer/consumer (Section 7).
   - Hangfire / BullMQ jobs (Section 10).
   - Health + Sentry + Serilog/Winston (Section 9.1, 9.12).

4. **Sinh entity + repository** từ schema.sql.

5. **Implement Commands/Queries** per use case (Section 6 technical_context cho business logic).

6. **Test:**
   - Unit per Command/Query Handler.
   - Integration per endpoint với Testcontainers.

7. **CI:** add service vào GitHub Actions workflow.

### Khi thêm endpoint mới

1. Check technical_context cho business rule.
2. Check `Docs/API/VietRide_API_Contract_v1.md` — nếu chưa có, thêm vào đó trước.
3. Implement Controller (thin) + Command/Query + Handler + Validator + DTO.
4. Map errors qua canonical error code (Section 5.9).
5. Idempotency-Key nếu endpoint nằm trong danh sách mutation ở Section 5.6.
6. Unit + integration test.
7. Update changelog API contract.

### Khi thêm event mới

1. Đặt routing key theo convention `<service>.<aggregate>.<verb_past>` (Section 3.5).
2. Thêm entry vào Section 7.3 event registry — bump doc MINOR version.
3. Producer: INSERT outbox_events trong cùng transaction với business write.
4. Consumer: declare queue + consumer + handler. Manual ack.
5. Integration test cho cả producer (emits event) và consumer (handles event).

### Khi sửa schema DB

1. Sửa `db-schema/<service>/schema.sql` + `seed.sql` + `README.md` + `schema.drawio`.
2. Tạo EF Core migration (`dotnet ef migrations add`) hoặc Prisma migration.
3. Nếu thay đổi cross-service logical FK → update `db-schema/_global/cross-service-references.md`.
4. Nếu thêm enum value mới → update technical_context Section 8.
5. Run migration smoke test trên fresh DB.

---

**End of Backend Source of Truth v1.0.0**
