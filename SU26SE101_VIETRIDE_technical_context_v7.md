# VietRide — Technical Project Context (Agent-Ready v7)

> **Capstone:** SU26SE101 — SU26
> **Cập nhật:** 2026-07-30 (Days 31/32/35 contract reconciliation)
>
> ## ⚠️ Đọc trước khi dùng — Mục đích của doc này
>
> Đây là **technical context document** (source of truth) cho dự án VietRide. Doc này chứa: business rules, design decisions, architecture, conventions, business flow, status machine, enum values, entity requirements (fields cần có và lý do). API controller/DTO contract chi tiết nằm ở `Docs/API/VietRide_API_Contract_v1.md`.
>
> **Doc này KHÔNG PHẢI là DB schema document.** Doc cố tình KHÔNG bao gồm: SQL DDL, column types/constraints chi tiết, index strategy cụ thể, EF Core/Prisma entity class code, migration scripts.
>
> **Lý do tách:** DB schema design là deliverable riêng do agent chuyên trách thực hiện sau khi đọc doc này. Nếu gom schema vào đây sẽ (1) làm doc dài quá, (2) khóa hướng agent design DB sai (lock vào schema đã viết sẵn dù chưa optimal), (3) khó maintain khi business rules thay đổi.
>
> **Agent đọc doc này nên:**
> - **Generate DB schema:** đọc business rules + entity requirements → tự design schema phù hợp với từng service. Output là deliverable riêng (file SQL/migration).
> - **Scaffold codebase:** dùng conventions section + entity requirements để tạo project structure, controller, handler stub.
> - **Frontend/mobile dev:** dùng business flow + `Docs/API/VietRide_API_Contract_v1.md` + error code list để implement client.
> - **QA / test plan:** dùng business flow + status machine + edge cases để viết test case.
> - **Architecture review:** dùng decisions + non-functional requirements để đánh giá design choice.
>
> **Nếu phát hiện gap, contradiction, hoặc business rule không rõ ràng — flag lại để cập nhật doc trước khi proceed. Không tự suy diễn business logic.**
>

---

## 1. Business Overview

### 1.1 Tên dự án

- **English:** VietRide — A platform for booking, locating, and managing passenger buses
- **Vietnamese:** VietRide — Nền tảng đặt vé, định vị và quản lý xe khách

### 1.2 Vấn đề cần giải quyết

Vận tải hành khách tại Việt Nam, đặc biệt các nhà xe vừa và nhỏ, vẫn vận hành thủ công: đặt vé qua điện thoại, ghi chép bằng tay. Hệ quả là quản lý kém hiệu quả, thiếu dữ liệu tập trung, phối hợp kém.

**Pain point chính của hành khách:** Đặt vé xong không biết xe đến lúc nào, phải chờ mà không có thông tin → mất thời gian, khó chịu.

**Pain point của nhà xe:** Thiếu hệ thống tập trung dẫn đến bỏ sót hành khách, hàng hóa giao sai, không có dữ liệu vận hành để tối ưu.

### 1.3 Giải pháp

Hệ thống số hóa toàn diện cho xe khách liên tỉnh bao gồm:
- Đặt vé online với nhiều phương thức thanh toán
- Theo dõi vị trí xe **thời gian thực** qua GPS (giải quyết pain point chính)
- Quản lý điểm đón linh hoạt (tại bến hoặc dọc tuyến)
- Giao nhận hàng ký gửi theo chuyến với xác nhận điện tử
- RAG AI Assistant hỗ trợ nội bộ và khách hàng

### 1.4 Actors

| Actor | Mô tả |
|---|---|
| **Hành khách (Passenger)** | Đặt vé, theo dõi xe, quản lý hàng ký gửi |
| **Tài xế (Driver)** | Xem lịch trình, phát GPS, xác nhận hành khách lên xe |
| **Phụ xe (Assistant)** | Hỗ trợ tài xế — xác nhận hành khách, quản lý hàng hóa, theo dõi điểm dừng |
| **Nhân viên vận hành (Operator Staff)** | Quản lý tuyến, chuyến, phương tiện, đặt chỗ |
| **Quản lý nhà xe (Operator Admin)** | Toàn quyền công ty + quản lý nhân sự |
| **Admin hệ thống (System Admin)** | Quản lý toàn bộ nền tảng, phê duyệt nhà xe, RAG knowledge base |

### 1.5 Phạm vi chức năng gốc (từ proposal)

**Mobile — Hành khách:** Đăng ký/đăng nhập, tìm kiếm chuyến, xem lịch trình & ghế trống, đặt vé với điểm đón linh hoạt, thanh toán, theo dõi xe thời gian thực, nhận thông báo, quản lý hàng ký gửi.

**Mobile — Tài xế/Phụ xe:** Xem lịch làm việc, quản lý thông tin chuyến, danh sách hành khách, xác nhận lên xe, quản lý hàng ký gửi, theo dõi điểm dừng.

**Web — Operator Dashboard:** Quản lý thông tin công ty, nhân sự, phương tiện, tuyến đường, chuyến đi, đặt vé thủ công, thay đổi lộ trình, hàng hóa, theo dõi vận hành, dashboard & báo cáo, RAG AI assistant.

**Web — System Admin:** Quản lý nhà xe, người dùng, dữ liệu hệ thống, doanh thu, cấu hình, RAG knowledge base.

---

## 2. System Architecture

### 2.1 Tổng quan

**Microservices** — 8 service nghiệp vụ + 1 API Gateway. Mỗi service độc lập, có database riêng, giao tiếp qua HTTP REST (đồng bộ) và RabbitMQ (bất đồng bộ). Triển khai qua Docker + GitHub Actions CI/CD.

```
[Passenger App]  [Driver App]  [Operator Web]  [Admin Web]
        │               │              │               │
        └───────────────┴──────────────┴───────────────┘
                                │
                        ┌───────▼────────┐
                        │  API Gateway   │  ← NestJS: JWT validate, routing, rate limit
                        │                │     sinh Internal JWT (xem 5.4)
                        └───────┬────────┘
                                │
           ┌────────────────────┼──────────────────────┐
           │                    │                      │
    .NET Core Services     NestJS Services         RabbitMQ
    ──────────────────     ───────────────         (message broker)
    Identity & User        Tracking (GPS)
    Booking                Notification
    Trip-Route-Vehicle     RAG AI
    Payment & Wallet
    Parcel
```

### 2.2 Hai loại giao tiếp giữa services

```
ĐỒNG BỘ (HTTP REST) — dùng khi cần kết quả ngay:
  Booking Service ──HTTP──▶ Trip-Route-Vehicle: "Chuyến này còn ghế không? Lock 3 ghế."
  Payment & Wallet Service (internal): charge request → deduct wallet balance trong cùng DB transaction

BẤT ĐỒNG BỘ (RabbitMQ) — dùng khi phát sự kiện, không cần chờ:
  Booking Service ──event──▶ RabbitMQ ──▶ Notification Service (gửi push)
                                       ──▶ Payment & Wallet Service (log WalletTransaction nếu method=WALLET)
                                       ──▶ Analytics (cập nhật dashboard — xem BookingStats note)
```

> **Lưu ý quan trọng:** MediatR **không** phải công cụ giao tiếp giữa services. MediatR chạy hoàn toàn trong nội bộ một service (in-process). Xem giải thích chi tiết ở mục 3.1.

**Pattern saga cho booking flow:** Hybrid — sync core path + async side-effects. Xem chi tiết ở mục 6.9.

### 2.3 Tại sao RabbitMQ, không phải Kafka?

| Tiêu chí | RabbitMQ ✅ | Kafka |
|---|---|---|
| Scale phù hợp | ≤ vài nghìn msg/s — đủ cho dự án | Triệu msg/s — overkill |
| Dead Letter Queue | Tích hợp sẵn, cấu hình đơn giản | Phức tạp hơn |
| Routing linh hoạt | Exchange/binding pattern — gửi đúng queue | Topic-based |
| Vận hành nhóm nhỏ | Nhẹ, có web dashboard sẵn | Cần Zookeeper/KRaft, nặng |
| Use case | Workflow messaging ✅ | Event streaming, log pipeline |

Dự án dùng messaging kiểu workflow (booking confirmed → notify, route changed → update ETA) — RabbitMQ là lựa chọn đúng.

---

## 3. Tech Stack & Lý do chọn

### 3.1 Backend — .NET Core 8

Dùng cho: Identity & User, Booking, Trip-Route-Vehicle, Payment & Wallet, Parcel.

**Tại sao .NET cho các service này?**

Các service này xử lý **nghiệp vụ phức tạp** với nhiều ràng buộc, transaction và business rules. .NET cung cấp:

**Entity Framework Core 8 + Migrations:**
Quản lý schema DB có version. Relationships phức tạp (Route → Stop → TripStop → ETA) được model rõ bằng C# entity. Migration tự động, không viết raw SQL.

**MediatR v11.x — CQRS pattern (trong nội bộ service):**

> ⚠️ **License note:** Từ MediatR v12.0 (cuối 2024), Jimmy Bogard chuyển MediatR sang **commercial license**. Dự án **pin v11.x** (phiên bản cuối còn MIT free) để tránh chi phí license. Khi cài đặt NuGet, dùng `<PackageReference Include="MediatR" Version="11.*" />`. Không update sang v12+. Alternative nếu cần upgrade sau này: `Mediator` (martinothamar — source generator, MIT, performance tốt hơn).

Tách biệt Command (thay đổi data) và Query (đọc data). Mỗi use case là một Handler độc lập.

```
// Ví dụ: Operator tạo tuyến đường
HTTP POST /routes
  → Controller nhận request
  → Tạo CreateRouteCommand { originCity, destinationCity, stops[] }
  → MediatR.Send(command)
  → CreateRouteCommandHandler:
      - FluentValidation: origin ≠ destination, stops ≥ 2, orderIndex duy nhất
      - EF Core: tạo Route + Stop[] trong 1 transaction
      - SaveChanges với optimistic concurrency check
      - Publish RouteCreatedEvent → RabbitMQ
```

Lợi ích: Controller không chứa business logic. Dễ test từng handler riêng. Dễ thêm cross-cutting concern (logging, validation) vào pipeline.

**FluentValidation:**
Validate business rules trong pipeline, tách khỏi controller. Ví dụ: giờ khởi hành phải là tương lai, số ghế không âm, giá vé hợp lệ.

**Hangfire:**
Scheduled & background jobs. Ví dụ: tự động đóng boarding sau giờ khởi hành, gửi reminder trước chuyến, xử lý timeout booking chưa thanh toán sau 10 phút.

**Polly:**
Circuit breaker + retry khi gọi Google Maps API (tính ETA) hoặc VNPay. Tránh cascade failure khi external service chậm.

**C# type system:**
`Money` là struct riêng (không dùng float), `TripStatus` là enum, `BookingCode` là value object — không bao giờ lẫn type lúc runtime.

### 3.2 Backend — NestJS

Dùng cho: API Gateway, Tracking, Notification, RAG AI.

**Tại sao NestJS cho các service này?**

Các service này **integration-heavy**, ít business logic phức tạp, cần WebSocket và event-driven.

**API Gateway — NestJS tốt hơn .NET YARP:**
Guard, Interceptor, Middleware là first-class concept trong NestJS — JWT validation, rate limiting, request routing là middleware chain đơn giản. .NET YARP là reverse proxy tốt nhưng nặng và cần cấu hình nhiều hơn. Gateway không cần EF Core hay MediatR nên không cần .NET.

**API Gateway implementation strategy (v1):**
- Dùng NestJS HTTP app + `http-proxy-middleware` cho proxy REST request tới downstream services.
- Route table config-driven: prefix public `/v1/bookings`, `/v1/trips`, `/v1/payments`, ... map tới service base URL nội bộ.
- Middleware chain: rate limit → validate User Access Token → build Internal JWT → proxy request, giữ nguyên method/body/query.
- KHÔNG dùng `@nestjs/microservices` cho request/response REST gateway; RabbitMQ chỉ dùng giữa business services qua Outbox/consumer.
- KHÔNG viết tay controller per endpoint trong Gateway, trừ health check và auth/public callback exception nếu thật sự cần.

**Tracking Service — Socket.IO vs SignalR:**
SignalR (.NET) và Socket.IO (NestJS) đều là WebSocket abstraction tốt. Lý do chọn NestJS/Socket.IO:

| | SignalR (.NET) | Socket.IO (NestJS) ✅ |
|---|---|---|
| React Native client | `@microsoft/signalr` — ít ví dụ thực tế | `socket.io-client` — docs nhiều, community lớn |
| NestJS integration | Phải mix với NestJS service khác | `@WebSocketGateway` decorator native, rooms/namespaces built-in |
| Cùng stack | Tách service riêng | Cùng NestJS + BullMQ (Redis-backed, **share queue qua Redis chứ KHÔNG share in-process EventEmitter** vì 2 service là 2 Docker container riêng) |

Nếu team quen .NET thì SignalR cũng khả thi về kỹ thuật. Chọn NestJS vì mobile dùng React Native và Socket.IO client library integrate mượt hơn.

**Email boundary — service-local client → Notification provider:**

Identity và Parcel dùng client abstraction riêng trong service của mình; không có shared method
nhận raw Parcel token hoặc một `ParcelDeliveryEmailDto` lớn. Business code không phụ thuộc
SendGrid SDK. Notification là service duy nhất sở hữu `IEmailProvider` và provider implementation.

Parcel tạo URL runtime
`{PUBLIC_APP_URL}/parcels/delivery/confirm?token=<url-encoded-token>` rồi gọi exact Internal-JWT
request:

```json
{
  "notificationId": null,
  "dedupeKey": "parcel-delivery-token:<tokenRowId>",
  "toEmail": "<recipientEmail>",
  "templateKey": "PARCEL_DELIVERY_LINK",
  "templateData": {
    "deliveryUrl": "<runtime-only URL>",
    "parcelCode": "VR-PCL-...",
    "expiresAt": "<ISO-8601 offset timestamp>"
  }
}
```

HTTP `Idempotency-Key` bằng UUID-v4 `tokenRowId`. Notification trả `202 Accepted`; mọi response
khác, timeout hoặc transport failure map `503 UPSTREAM_UNAVAILABLE`, và Parcel không commit token
issuance/state. Raw token/URL chỉ tồn tại trong memory + outbound request, không được log, persist
trong Parcel/Notification audit, Outbox, Redis hoặc RabbitMQ. Notification scrub sensitive
templateData trước mọi log/audit.

Notification provider vẫn hỗ trợ các template `AUTH_OTP`, `SET_INITIAL_PASSWORD` và
`PARCEL_DELIVERY_LINK`; provider swap (SendGrid/SMTP local) không thay đổi service contracts.

Phân biệt 2 loại email:
| Loại | Template | Priority | TTL/Rate-limit | Notification type |
|---|---|---|---|---|
| OTP (Registration/Reset) | Transactional | Cao | Rate-limit Redis (max 3/1h per email) | AUTH_OTP |
| Parcel delivery link | Transactional | Trung bình | Resend cho phép (token TTL 48h) | PARCEL_DELIVERED_PENDING_CONFIRM |

Identity Service và Parcel Service gọi Notification qua `POST /internal/v1/emails` bằng Internal
JWT. Hai service dùng client abstraction riêng nhưng cùng Notification provider.

> Lý do không depend trực tiếp vào SendGrid: dễ swap provider (SES, Mailgun) không cần sửa business code. Dễ mock trong unit test.

**Notification Service — NestJS + RabbitMQ consumer + BullMQ internal queue:**

Notification Service dùng **cả 2 message queue** với vai trò khác nhau — đây là pattern cố ý, không phải lỗi:

```
RabbitMQ  (inter-service event bus — bất đồng bộ giữa các service)
  BookingConfirmed event
    → Notification Service CONSUME từ RabbitMQ
      → ENQUEUE vào BullMQ (Redis-backed, internal queue)
        → BullMQ worker: gọi Firebase FCM với retry logic riêng
```

**Tại sao cần BullMQ bên trong Notification Service?**
RabbitMQ không có built-in retry với exponential backoff **per individual job** — nếu FCM call fail (network timeout, FCM 5xx), RabbitMQ chỉ có Dead Letter Queue (DLQ) không phù hợp để retry từng push notification riêng lẻ.
BullMQ (Redis-backed) handle retry logic tốt hơn: FCM call fail → retry sau 5s → 30s → 5m → nếu hết retry thì log vào DLQ của BullMQ để audit.

**Setup cần:**
- RabbitMQ consumer trong NestJS: subscribe queue `notifications.*`
- BullMQ queue `fcm-push` backed by Redis instance đã có
- BullMQ worker: processor gọi Firebase Admin SDK, retry policy riêng

**RAG AI Service — NestJS:**
Streaming LLM response qua SSE, gọi vector DB, tích hợp nhiều external API. NestJS async/await + stream pipe xử lý tốt. MongoDB bỏ — RAG dùng PostgreSQL + pgvector (đủ cho scale này, không cần thêm DB riêng).

### 3.3 Service — Framework mapping (8 service)

| # | Service | Framework | Phạm vi domain | Lý do chính |
|---|---|---|---|---|
| 1 | **API Gateway** | NestJS | Routing, JWT validate, sinh Internal JWT, rate limit | Guard/Interceptor native, custom auth logic dễ viết, chia sẻ NestJS stack với 3 service khác |
| 2 | **Identity & User** | .NET Core | Auth, RBAC, refresh token, user profile, passenger/driver/assistant info, operator profile | ASP.NET Identity mature, gộp auth + profile giảm cross-service call cho hầu hết request |
| 3 | **Booking** | .NET Core | Booking lifecycle, seat locking, voucher apply, cancellation, multi-passenger per booking | Transaction phức tạp, MediatR CQRS, Hangfire timeout |
| 4 | **Trip-Route-Vehicle** | .NET Core | Route, Station, Stop, AlternativeRoute, Trip scheduling, TripStopFare, Vehicle CRUD + status, DriverSchedule, search endpoints | Domain logic nặng nhất, Hangfire auto-generate Trip từ DriverSchedule, Polly cho Maps API |
| 5 | **Payment & Wallet** | .NET Core | VNPay integration, Wallet, WalletTransaction, refund, idempotency | Financial transaction, strong typing, Polly cho VNPay |
| 6 | **Parcel** | .NET Core | Parcel workflow, delivery link confirmation, email link, cargo weight aggregation | Structured workflow, status machine, email link signing |
| 7 | **Tracking** | NestJS | Socket.IO GPS streaming, ETA calculation, off-route detection | Socket.IO native, real-time broadcast |
| 8 | **Notification** | NestJS | FCM push, in-app notification history, email send (SendGrid) | RabbitMQ consumer, FCM integration, BullMQ queue |
| 9 | **RAG AI** | NestJS | Knowledge base ingest, embeddings, LLM streaming, conversation history | LLM streaming SSE, pgvector search |

> **Lưu ý đếm:** Bảng trên đánh số 1–9 nhưng header ghi "8 service nghiệp vụ + 1 Gateway". Lý do: Gateway (#1) là infrastructure layer, không phải business service. Tổng thực tế = **8 business services** (Identity, Booking, Trip-Route-Vehicle, Payment & Wallet, Parcel, Tracking, Notification, RAG AI) + **1 API Gateway** = **9 containers**. Khi agent đếm service, dùng con số 8 business services.

- **Identity + User → 1 service:** Auth và profile gắn chặt với nhau (mỗi login cần verify + fetch user info). Tách 2 service tạo cross-service call cho mọi request → latency và complexity tăng vô lý ở capstone scale.
- **Payment + Wallet → 1 service (tên canonical: "Payment & Wallet Service"):** Wallet deduct và Payment record tạo trong cùng 1 DB transaction cho wallet payment flow — nếu tách 2 service sẽ cần distributed transaction (2PC) hoặc Saga phức tạp. Wallet chỉ có nghĩa khi gắn với Payment (top-up, charge, refund). Mọi chỗ trong doc viết "Wallet Service" đơn lẻ đều ám chỉ module Wallet bên trong Payment & Wallet Service — KHÔNG phải service độc lập. Agent KHÔNG tạo service riêng cho Wallet.
- **Vehicle & Fleet → gộp vào Trip-Route-Vehicle:** Vehicle CRUD đơn giản (~10–50 xe/operator), Vehicle gắn chặt với Trip (seat layout quyết định TripSeat). Tách ra sẽ phải gọi cross-service liên tục khi query "chuyến này còn ghế không".
- **Search → gộp vào Trip-Route-Vehicle:** Endpoint `GET /trips/search` chỉ là query PostgreSQL với filter (origin, destination, date, operator). Không có domain logic → không cần service riêng.
- **DriverSchedule:** thuộc Trip-Route-Vehicle (vì gắn chặt Route + Vehicle + Trip generation), không tách service riêng.

### 3.4 Infrastructure & Data

| Tech | Vai trò | Ghi chú |
|---|---|---|
| **PostgreSQL 16** | Primary DB tất cả services + pgvector cho RAG | ACID, JSONB, row-level locking. Database isolation strategy: xem note bên dưới |
| **PgBouncer** | Connection pooler trước PostgreSQL | 8 business services + 1 Gateway × pool 10 = ~80–100 connections; PgBouncer giữ actual connections xuống 20–30, tránh vượt `max_connections` |
| **Redis 7** | Cache, seat locking (TTL), session, rate limiting, idempotency keys, GPS last-known location | |
| **RabbitMQ** | Message broker | Xem lý do ở mục 2.3 |
| **Firebase FCM** | Push notification đến mobile | Firebase Admin SDK phía server |
| **Firebase Storage** | Object storage — avatars, tài liệu RAG, ảnh xe | Dùng luôn Firebase vì đã có FCM — 1 project, 1 SDK, 5GB free |
| **Docker + Docker Compose** | Local dev + production deploy | |
| **Nginx** | Reverse proxy, SSL termination, gzip | |
| **GitHub Actions** | CI/CD pipeline | |

> **Không dùng:** Supabase (gộp vào Firebase Storage), Elasticsearch (pgvector đủ), MongoDB (PostgreSQL JSONB đủ cho RAG).

> **Database isolation strategy (quan trọng cho DB schema design):**
>
> **1 PostgreSQL cluster duy nhất, mỗi service có 1 logical database riêng.** KHÔNG dùng shared database, KHÔNG dùng schema-per-service trong cùng database, KHÔNG dùng cluster riêng per service.
>
> | Service | Database name |
> |---|---|
> | Identity & User | `vietride_identity` |
> | Booking | `vietride_booking` |
> | Trip-Route-Vehicle | `vietride_trip` |
> | Payment & Wallet | `vietride_payment` |
> | Parcel | `vietride_parcel` |
> | Tracking | `vietride_tracking` (chỉ chứa GpsTrail + OutboxEvent) |
> | Notification | `vietride_notification` |
> | RAG AI | `vietride_rag` (có pgvector extension) |
>
> **Lý do:**
> - 1 cluster: giảm cost (1 PostgreSQL container/instance thay vì 8), dễ vận hành cho team 3–5 người, đủ performance cho capstone scale.
> - Logical database riêng (không phải schema): isolation thật giữa services (không thể FK cross-service ở DB level — bắt buộc inter-service qua HTTP/event), credentials riêng per service (Identity Service không có quyền đọc Payment DB ngay cả khi compromised), backup/restore independent per service.
> - PgBouncer pool cho từng database riêng.
>
> **Hangfire schema:** Hangfire tables (`hangfire.*`) nằm trong **cùng database của service đó** (vd `vietride_booking.hangfire`). Mỗi service .NET dùng Hangfire có schema `hangfire` riêng trong DB của mình. Không có Hangfire DB share.
>
> **Cross-DB query bị cấm.** Nếu service A cần data của service B → HTTP REST (sync) hoặc consume event (async). Snapshot field (như `Booking.tripSnapshot*`) là pattern accept để tránh cross-DB query.
>
> **Migration tool:** EF Core Migrations cho .NET services, Prisma migrations cho NestJS services. Mỗi service tự quản migration của database mình.

> **Hangfire storage — mỗi .NET service có schema riêng trong PostgreSQL của service đó (chỉ dành cho business scheduled jobs — KHÔNG dùng Hangfire cho Outbox polling):**
> Hangfire cần persistent storage để track job queue. Dùng `Hangfire.PostgreSql` package — Hangfire tự tạo các bảng (`hangfire.job`, `hangfire.queue`, v.v.) trong **cùng PostgreSQL DB** của service đó (không phải DB share riêng).
>
> Services dùng Hangfire (business scheduled jobs) và lý do:
> - **Booking Service:** seat release khi tới authoritative Payment deadline (Trip seat-lock
>   `expiresAt`; round-trip dùng leg sớm hơn), schedule-change confirmation auto-accept khi quá
>   deadline, PENDING_SEAT_ASSIGNMENT escalation (T+2h re-alert) + auto-cancel/refund 100% nếu
>   unresolved tại `departure - 30 phút` (job interval 15 phút)
> - **Trip-Route-Vehicle Service:** auto-generate Trip từ DriverSchedule (2 trigger: immediate on-create + weekly CN 23:00), auto-BOARDING 30 phút trước departure, auto-COMPLETED fallback sau estimatedArrivalTime + 30 phút
> - **Parcel Service:** undo-reject window 15 phút (DELIVERY_REJECTED → RETURN_INITIATED), cancel EXTRA_LARGE review timeout sau 24h, reject/forfeit `RESERVED` quá `latestCheckInAt`, reject/forfeit `PENDING_FINAL_PAYMENT` quá `finalPaymentDeadline` (interval 5 phút), PENDING_TRANSFER_CONFIRM escalation 30 phút, PENDING_OPERATOR_ACTION re-alert 2h
> - **Payment Service:** Payment PENDING_REDIRECT EXPIRED khi `dueAt ?? createdAt + 15 phút <= now`;
>   TopUpRequest EXPIRED sau 15 phút
>
> **Outbox polling KHÔNG dùng Hangfire** — dùng `BackgroundService`/`IHostedService` (xem Decisions section).
>
> **Prisma cho Tracking Service (NestJS):**
> Tracking Service (NestJS) cần write `GpsTrail` vào PostgreSQL. Dùng **Prisma** (native với NestJS via `@nestjs/Prisma`). GpsTrail entity requirements: tripId, lat/lng (decimal precision 10 scale 7 — đủ độ chính xác ~1cm), speed nullable (km/h), timestamp. Tracking Service có **PostgreSQL DB riêng** — chỉ chứa GpsTrail (và OutboxEvent nếu publish event). Redis handle realtime state.
>
> **Tracking Service URL exposure:**
> Tracking Service chạy sau Nginx, **không expose port trực tiếp ra internet**. Nginx route:
> - `wss://api.vietride.app/tracking/` → proxy_pass tới Tracking Service (với `upgrade` header cho WebSocket)
> - Rate limit riêng tại Nginx level cho WebSocket connections: `limit_conn` per IP
> Client (mobile + web) kết nối qua `wss://api.vietride.app/tracking/` — cùng domain với REST API, không cần expose domain/port riêng của Tracking Service.

### 3.5 Observability — Minimal (team 3–5 người)

> Nguyên tắc: setup < 1 giờ, free tier đủ dùng, không cần dedicated DevOps.

| Tech | Vai trò | Free tier |
|---|---|---|
| **Sentry** | Tự động bắt exception — khi server throw lỗi, Sentry chụp stack trace + file + dòng code, gửi email/alert cho team | 5,000 lỗi/tháng, vĩnh viễn |
| **UptimeRobot** | Ping service mỗi 5 phút — nếu không trả lời thì email cảnh báo "service down" | 50 monitors, vĩnh viễn |
| **Serilog** | Structured logging .NET services — ghi ra console + file | Không cần infra |
| **Winston** | Structured logging NestJS services — ghi ra console + file | Không cần infra |

> **Bỏ qua cho v1:** Prometheus, Grafana, Jaeger/Tempo, Loki. Grafana là dashboard vẽ biểu đồ CPU/memory/request rate theo thời gian thực, nhưng cần kết hợp Prometheus (thu thập metrics) và cần thêm 2 Docker container — setup 1–2 ngày. Sentry đã cover error tracking, UptimeRobot cover uptime — đủ cho capstone.

### 3.6 Frontend & Mobile

| Tech | App | Ghi chú |
|---|---|---|
| **React Native** | **Passenger App** | App riêng — store listing riêng, bundle riêng |
| **React Native** | **Driver & Assistant App** | App riêng — store listing riêng, UI khác hoàn toàn |
| **NextJS** | Operator Web Dashboard | App Router, render strategy mixed — xem ghi chú bên dưới |
| **NextJS** | System Admin Web | Cùng codebase NextJS với Operator Web, UI khác theo role |

> **NextJS render strategy (quan trọng cho Operator Web):**
> Operator Web có 2 loại page với nhu cầu khác nhau:
>
> | Page type | Ví dụ | Render strategy |
> |---|---|---|
> | Static/SSR pages | Login, Route management, Report | Server Components (SSR default trong App Router) |
> | Realtime pages | Fleet map, Trip monitoring, Alert dashboard | `'use client'` — CSR hoàn toàn, kết nối Socket.IO trực tiếp từ browser |
>
> **Tại sao NextJS thay ReactJS thuần?** NextJS App Router cung cấp file-based routing, middleware auth, layout system — giảm boilerplate đáng kể. Các realtime page vẫn chạy pure CSR trong NextJS bằng `'use client'` directive — không conflict với Socket.IO. Team không cần maintain 2 framework riêng.
>
> **Socket.IO trên Operator Web:** Realtime pages kết nối thẳng tới Tracking Service (tương tự mobile app) — dùng `socket.io-client` trong React component, không qua Next.js API routes.

### 3.7 External Integrations

| Integration | Mục đích | Ghi chú |
|---|---|---|
| **Google OAuth 2.0** | Đăng nhập hành khách | Google Identity Services |
| **Google Maps SDK** | Hiển thị bản đồ trong mobile và web | Passenger tracking, operator dashboard |
| **Google Maps Directions API** | Tính ETA từ vị trí xe đến điểm dừng | **KHÔNG gọi mỗi GPS packet** — xem ETA strategy bên dưới |
| **VNPay** | Cổng thanh toán, nạp ví | Redirect-based, HMAC-SHA512 signature verify |
| **Firebase FCM** | Push notification mobile | Firebase Admin SDK |
| **Firebase Storage** | Client upload ảnh vehicle/logo/parcel/incident/avatar | 5GB free; RAG và invoice vẫn server-owned |
| **LLM API** | Generate câu trả lời RAG | Claude API (`claude-sonnet-4-6` — model string `claude-sonnet-4-6`) hoặc GPT-4o, streaming SSE |
| **OpenAI Embedding API** | Embed chunks cho RAG retrieval | Model `text-embedding-3-small` — 1536 dims, rẻ hơn ada-002 ~5x, cùng dimension nên drop-in replacement |
| **pgvector** | Vector similarity search cho RAG | PostgreSQL extension — thay thế Elasticsearch |
| **SendGrid / SMTP** | Email — OTP đăng ký, parcel delivery link | Free tier 100 email/ngày |

**Firebase client uploads:** VietRide User Access Token không được dùng trực tiếp với Firebase.
Client gọi `POST /v1/firebase/custom-token` với `purpose` (body rỗng vẫn mặc định
`VEHICLE_IMAGE`), Identity revalidate User và Operator hiện tại rồi tạo Firebase Custom Token
có UID = VietRide `userId`, claims `role`, `uploadPurpose` và `operatorId` khi có. Các prefix
được cấp là `vehicles/{operatorId}/` (OPERATOR_ADMIN), `operators/{operatorId}/logo/`
(OPERATOR_ADMIN), `parcels/{userId}/` (PASSENGER), `incidents/{operatorId}/{userId}/`
(DRIVER/ASSISTANT), và `avatars/{userId}/` (mọi user ACTIVE). FE exchange bằng
`signInWithCustomToken()`, upload MIME JPEG/PNG/WebP dưới 5 MiB, lấy download URL rồi gửi
URL về API resource tương ứng; API kiểm tra lại bucket, prefix và owner trước khi lưu. Sau đó
client `signOut()` (và cũng sign-out khi logout VietRide). RAG documents tiếp tục upload
server-side qua Cloudinary; Invoice PDF tiếp tục backend-owned storage. User lock hoặc Operator
suspend phát Outbox revoke request để Identity gọi Firebase `RevokeRefreshTokensAsync`; ID token
đã phát có residual window tối đa khoảng một giờ.

> **⚠️ Google Maps ETA — gọi có điều kiện, không gọi mỗi GPS packet:**
> GPS update mỗi 3–5 giây × N active trips đồng thời → nếu gọi Maps API mỗi packet sẽ rất tốn kém (ví dụ 10 trips đang chạy = ~2 call/giây = >100k call/ngày, vượt free tier ngay ngày đầu).
>
> **Strategy bắt buộc implement:**
> ```
> ETA recalculate chỉ khi thoả MỘT trong các điều kiện:
>   1. Xe di chuyển > 500m kể từ lần tính ETA trước (distance threshold)
>   2. ETA đến stop tiếp theo còn < 15 phút (high-frequency zone — cần chính xác hơn)
>   3. Operator manual trigger recalculate từ dashboard
>
> Cache kết quả ETA trong Redis:
>   Key: eta:{tripId}:{stopId}
>   TTL: 60 giây (sau TTL, lần GPS update tiếp theo sẽ recalculate nếu đủ điều kiện)
> ```
> Tracking Service lưu `lastEtaCalculatedAt` và `lastEtaCalculatedLat/Lng` trong Redis per trip để check distance threshold.

---

## 4. Client Applications

### 4.1 Passenger App (App hành khách)

Ứng dụng độc lập dành cho hành khách. Tài khoản tự đăng ký.

**Chức năng:**
- Đăng ký / đăng nhập (Google OAuth, Email + Password, Email OTP verify khi đăng ký lần đầu)
- **Quản lý hồ sơ cá nhân:** xem/chỉnh sửa thông tin (tên, số điện thoại, avatar), xem lịch sử chuyến đã đi
- Tìm kiếm chuyến theo điểm đi, điểm đến, ngày
- **Xem danh sách nhà xe** đang khai thác tuyến đã chọn (trước khi chọn chuyến cụ thể) — filter/browse theo công ty, giờ khởi hành, giá vé
- Xem lịch trình, giá vé, ghế trống, thông tin tuyến + danh sách điểm đón/trả
- Chọn **điểm đón**: tại bến xuất phát (Terminal) hoặc điểm dừng dọc tuyến (Along-route stop). Bến chính lớn có thể có **shuttle service** (xe trung chuyển) — xem section 6.14.
- Chọn **điểm trả**: mặc định là bến đích (Terminal). Nếu chuyến có RouteStop dọc tuyến, passenger có thể chọn xuống tại bất kỳ stop nào trên route (sau pickup stop) — fare vẫn tính full theo pickup stop (xem 6.1)
- **Đặt nhiều vé trong 1 booking (tối đa 5 ghế)** — chọn nhiều ghế trên sơ đồ. Booking chỉ lưu thông tin người mua (booking owner) — KHÔNG yêu cầu nhập họ tên/SĐT/CCCD của từng người ngồi xe. Giới hạn 5 ghế để ngăn đầu cơ/chiếm vé
- Áp voucher/mã giảm giá (voucher do System Admin phát hành ở cấp platform HOẶC do OPERATOR_ADMIN tự tạo operator-owned OPERATOR_FUNDED voucher — xem 4.4), thanh toán qua Ví VietRide hoặc VNPay
- Theo dõi vị trí xe thời gian thực trên bản đồ — **tính năng cốt lõi**
- Xem ETA đến điểm đón của mình
- Nhận push notification (xe sắp đến, thay đổi lộ trình, hủy chuyến, thay đổi giờ)
- **Xác nhận thay đổi từ nhà xe** (khi operator đổi lộ trình hoặc giờ khởi hành — hành khách nhận notification và bấm xác nhận/từ chối)
- Quản lý vé, **chỉnh sửa điểm đón và điểm trả** (sang Stop bất kỳ thuộc RouteStop của trip, hoặc về Terminal) trước giờ cắt — cutoff cứng **2 giờ** trước giờ khởi hành
- Ví điện tử: nạp tiền, thanh toán, xem lịch sử giao dịch
- Tạo và theo dõi hàng ký gửi (người gửi)
- **Theo dõi hàng ký gửi với tư cách người nhận:** nếu người nhận có tài khoản VietRide, họ có thể xem trạng thái parcel, mở tracking detail và xem vị trí xe thời gian thực trên bản đồ (join cùng room `trip:{tripId}` với passenger, sau khi Tracking Service verify quyền). Không cần đợi email link để track
- Chatbot CSKH (RAG AI — hỏi về chính sách, vé, hàng hóa)

### 4.2 Driver & Assistant App (App tài xế / phụ xe)

Ứng dụng riêng biệt hoàn toàn với Passenger App — tương tự cách Grab tách app tài xế và app hành khách. Tài khoản do Operator tạo, không tự đăng ký. Hai role DRIVER và ASSISTANT dùng chung app nhưng UI và quyền hơi khác nhau.

**Chức năng chung (cả Driver và Assistant):**
- Xem lịch trình làm việc và thông tin chuyến được phân công
- Xem danh sách hành khách **sắp xếp theo thứ tự điểm đón trên lộ trình** — hiển thị bookingCode, số ghế, điểm đón, boardingStatus
- **Xem chi tiết từng booking:** bookingCode, danh sách ghế trong booking, điểm đón, contact của người mua (booking owner) để liên hệ khi cần — KHÔNG có thông tin nhân thân của từng người ngồi xe (Booking chỉ lưu buyer info)
- Xác nhận từng hành khách đã lên xe (tick hoặc scan QR vé)
- **Cảnh báo bỏ sót hành khách ("Avoid missing passengers"):**
  - Khi xe đang tại stop (GPS trong vòng 200m từ stop) → app tự động highlight danh sách passenger chưa tick tại stop này
  - Khi Driver/Assistant bấm "Rời điểm dừng" → bodyless `POST /v1/driver/trips/{tripId}/stops/{stopId}/depart`
    persists `TripStop.actualDepartureTime` and asks Booking for the exact pending-passenger count.
    A positive count emits `trip.stop.departed_with_pending`; GPS is a future adapter to this
    same command and is not a second write path.
- Nhận push notification thay đổi lộ trình, lịch trình từ operator
- **Tab "Hỗ trợ" — RAG AI (Driver/Assistant App):**
  - Truy cập chatbot RAG AI với context DRIVER role (query được: FAQ, quy trình vận hành, chính sách hàng ký gửi, thông tin tuyến)
  - UI đơn giản: text input + response card, không cần streaming fancy
  - Conversation history per session (không persist qua app restart — đủ cho use case tra cứu nhanh khi đang chạy xe)

**Chức năng Driver thêm:**
- Bật/tắt GPS tracking — app gửi vị trí liên tục khi chuyến đang chạy
- **Nhận điều hướng đến điểm dừng tiếp theo:** v1 dùng **Google Maps deep link** (`google.navigation:q=<lat>,<lng>`) hoặc Apple Maps link tương đương — bấm nút "Chỉ đường" → mở app Google Maps/Apple Maps native với destination đã set. Không embed map navigation trong app (giảm complexity, không cần API turn-by-turn). v2 có thể embed Mapbox Navigation nếu cần.
- **Báo sự cố (incident report):** nút "Báo sự cố" trong Driver App. Form gồm:
  - `category` enum: TRAFFIC_JAM | VEHICLE_BREAKDOWN | ACCIDENT | WEATHER | OTHER
  - `description` text optional (free text, tối đa 500 ký tự)
  - `photoUrls` optional — tối đa 3 ảnh, upload qua Firebase Storage (client upload trực tiếp, BE nhận URL string array)
  - Tự động kèm GPS hiện tại + tripId
  - Submit → publish `IncidentReported` event → Operator nhận push notification + hiển thị trên dashboard. KHÔNG tự động đổi Trip.status — operator quyết định action (trigger alternative route, vehicle substitution, hủy chuyến).
  - Cần entity `Incident` thuộc Trip-Route-Vehicle Service: tripId, reportedByUserId, category, description, `photoUrls` JSONB string[] nullable (max 3 URLs), lat/lng, reportedAt, resolvedAt nullable, resolvedByUserId nullable, resolutionNote nullable. Notification Service chỉ consume `IncidentReported` event để gửi push/in-app notification, không sở hữu Incident DB.

**Chức năng Assistant thêm:**
- Theo dõi điểm dừng tiếp theo với countdown ETA
- **Bấm "Đã đến [stop name]"** khi xe dừng tại mỗi điểm → set `TripStop.actualArrivalTime` + `TripStop.status = ARRIVED`. Nút chỉ enable khi Trip = IN_PROGRESS. Dùng để tính refund khoảng cách cho DISRUPTED trip và cập nhật trạng thái dừng trên app
- Xem danh sách hàng ký gửi: cần nhận lên và cần giao tại điểm nào
- Xác nhận hàng đã nhận lên xe (LOADED)
- Xác nhận **dỡ hàng tại bến đích** (IN_TRANSIT → UNLOADED): yêu cầu QR scan parcel hoặc explicit confirm. Chỉ enable khi TripStop.status = ARRIVED tại destination stop
- Xác nhận đã giao hàng (UNLOADED → DELIVERED_PENDING_CONFIRM) → trigger email xác nhận đến người nhận

### 4.3 Operator Web Dashboard

Web app dành cho nhân viên và quản lý nhà xe. Scope: **một công ty duy nhất** — không chia nhỏ theo chi nhánh để giữ scope hợp lý cho capstone.

Cập nhật thông tin công ty vận tải và bến xe khai thác.

> **Scoping note — không có Branch entity:** v1 model một công ty vận hành đơn lẻ, không phân cấp chi nhánh. Branch/chi nhánh không được model thành entity riêng. Nếu cần mở rộng đa chi nhánh → v2.

**Chức năng:**
- Quản lý hồ sơ công ty (tên, liên hệ, logo, danh sách bến xe đang khai thác qua OperatorStation)
- **Cấu hình chính sách hủy vé (Cancellation Policy):** operator tự config tỷ lệ phí hủy theo khung thời gian trước departure (xem 6.2). UI cho phép thêm/sửa/xóa các tier policy. Lưu vào `Operator.cancellationPolicy` JSONB.
- **Quản lý bến xe khai thác (OperatorStation Management):** Operator **tự tạo và link Station** (không cần System Admin duyệt). UI bắt buộc autocomplete search trước khi cho tạo mới:
  - **Step 1 — Autocomplete search:** Operator nhập tên bến → FE call `GET /v1/stations/search?q=<text>` → BE trả về list Station hiện có (match name + city, fuzzy search) cùng kèm distance từ tọa độ Operator nhập (nếu có).
  - **Step 2a — Link Station hiện có (recommended):** Nếu Station đã tồn tại (operator khác đã tạo) → Operator chỉ tạo `OperatorStation` mapping. KHÔNG tạo duplicate Station.
  - **Step 2b — Create Station mới:** Nếu autocomplete không match (bến mới chưa có operator nào khai thác) → Operator submit form tạo Station mới + tự động link OperatorStation. Endpoint: `POST /v1/operator/stations` (role OPERATOR_STAFF/ADMIN). Station mới available cho operator khác link ngay sau khi tạo.
  - **OperatorStation config:** sau khi link, operator cấu hình thông tin riêng của nhà xe tại bến đó (quầy/điểm đón, hotline, ghi chú hướng dẫn, trạng thái active).
  - **Data quality:** System Admin có quyền merge duplicate Station, normalize coords/address — không phải là người gatekeep việc tạo Station. Xem 4.4.
- **Quản lý phương tiện (Vehicle Management — đầy đủ):**
  - Thêm/sửa/xóa xe, theo dõi trạng thái (`ACTIVE | MAINTENANCE | OFF_DUTY | RETIRED`)
  - Khai báo: **loại xe** (`vehicleTypeId` FK → `VehicleType` entity; 3 record seed STANDARD_BUS/LIMOUSINE/SLEEPER_BUS + operator có thể tạo custom type), **biển số** (`licensePlate`), **sơ đồ ghế** (`seatLayoutJson`), **tổng số ghế** (`totalSeats`), **loại ghế per seat** (qua seatLayoutJson.seats[].type: STANDARD | SLEEPER_LOWER | SLEEPER_UPPER | VIP | DRIVER_AREA)
  - **Số lượng xe per operator** (`quantity` aggregation tự nhiên qua count Vehicle records, có subscription limit `maxVehicles` — xem 4.5)
  - Khai báo `maxCargoWeightKg` và `maxCargoVolumeM3` (nullable) cho khoang hàng — dùng cho parcel capacity (xem 6.6)
- **Quản lý nhân sự:** tạo tài khoản tài xế/phụ xe, phân công chuyến.

  **Driver/Assistant initial password flow:**
  - OPERATOR_ADMIN tạo User { role=DRIVER hoặc ASSISTANT } qua dashboard, chỉ nhập email + thông tin cá nhân (KHÔNG nhập password).
  - Identity Service tự generate `EmailVerificationToken { purpose=SET_INITIAL_PASSWORD, code=UUID v4, expiresAt=now+48h }` và gửi email "Tài khoản VietRide đã được tạo" kèm link `https://app.vietride.app/auth/set-password?token=<token>`.
  - Driver/Assistant click link → trang đặt password lần đầu → `POST /v1/auth/set-initial-password { token, password }` → Identity Service set passwordHash, mark token used, set `User.status = ACTIVE`.
  - Trước khi set password lần đầu, User.status = `PENDING_INITIAL_PASSWORD` (UserStatus enum) — không login được.
  - Token hết hạn 48h: OPERATOR_ADMIN có thể resend từ dashboard (revoke token cũ, gen mới).
  - `EmailVerificationToken.purpose` enum thêm value `SET_INITIAL_PASSWORD` (cùng table với REGISTRATION/PASSWORD_RESET).

> **⚠️ Out of scope cho v1 (Document Management):**
> - **Driver document:** không track GPLX number, GPLX class, ngày cấp/hết hạn, sức khỏe. Driver chỉ là User entity với role DRIVER + thông tin cá nhân cơ bản (họ tên, SĐT, email, avatar)
> - **Vehicle document:** không track biển số chi tiết, đăng kiểm, bảo hiểm, ngày kiểm định. Vehicle chỉ có `licensePlate` (string display), `seatLayoutJson`, `maxCargoWeightKg`, `status`
> - **Lý do out of scope:** Document compliance management là feature riêng đáng có service riêng, ngoài scope capstone. Operator hiện tại tự quản lý giấy tờ offline. v2 có thể thêm DriverDocument + VehicleDocument entity với expiration tracking và alert
- **Quản lý DriverSchedule (Assignment — scope thu hẹp):** tạo assignment driver/assistant vào xe nào cho chuyến nào, lặp lại theo tuần. Ví dụ: tài xế A chạy SG→HN mỗi T2/T4/T6 lúc 08:00, dùng xe BKS XX. Khi tạo schedule chiều đi, UI gợi ý tạo luôn chiều về dựa trên `Route.returnRouteId`. Background job (Hangfire) tự động generate Trip cho 2 tuần kế tiếp dựa trên DriverSchedule. Cũng cho phép xem **history các chuyến driver/assistant đã lái**. Xem chi tiết entity ở mục 6.11.

> **⚠️ Out of scope cho DriverSchedule v1:** KHÔNG quản lý lịch làm việc chi tiết (ca làm, giờ làm, tổng giờ, off day), KHÔNG liên kết tính lương / chấm công. DriverSchedule chỉ là **assignment** driver/assistant ↔ xe ↔ chuyến để generate Trip và tracking history. Chi tiết shift management / payroll là feature riêng (v2+).
- Quản lý tuyến đường: tạo tuyến chính (Route) với `originStationId`, `destinationStationId`, `returnRouteId` (liên kết tuyến chiều về), định nghĩa danh sách Stop dọc tuyến theo thứ tự, tạo tuyến thay thế (Alternative Route — dùng khi có sự cố)
- **Quản lý điểm dừng dọc tuyến (Stop Management — module riêng):** tạo/chỉnh sửa Stop độc lập với route, vô hiệu hóa stop không còn hoạt động, đề xuất stop thay thế khi disable. Stop có thể thuộc nhiều Route
- Quản lý chuyến: lên lịch (manual hoặc từ DriverSchedule), gán xe + nhân sự, đặt giá vé, theo dõi trạng thái chuyến
- Quản lý đặt vé: xem danh sách booking của các chuyến công ty mình, xem chi tiết booking + thông tin Passenger, theo dõi trạng thái (PENDING_PAYMENT, CONFIRMED, CANCELLED, NO_SHOW, COMPLETED)
- **Disable/lock ghế (Manager Seat Disable):** OPERATOR_ADMIN (quản lý nhà xe) có thể disable một ghế cụ thể trên một chuyến (set `TripSeat.status = UNAVAILABLE`). Dùng khi hành khách đã mua vé ngoài hệ thống (kênh truyền thống nhà xe), hoặc ghế bị hỏng nội thất sau khi sinh TripSeat. **Đây là thao tác manual của manager, KHÔNG phải flow bán vé.** Hệ thống không lưu thông tin hành khách, không tạo Booking, không thu tiền — chỉ remove ghế khỏi pool AVAILABLE để tránh passenger khác đặt trùng. OPERATOR_STAFF KHÔNG có quyền này.
  - Endpoint: `POST /v1/operator/trips/{tripId}/seats/{seatNumber}/disable` (role OPERATOR_ADMIN). Bắt buộc nhập `reason` text. Ghi AuditLog.
  - Reverse: `POST /v1/operator/trips/{tripId}/seats/{seatNumber}/enable` — chuyển UNAVAILABLE → AVAILABLE nếu ghế chưa được dùng.

> **⚠️ Scoping note — KHÔNG bán vé tại quầy (walk-in counter sale):** Hệ thống KHÔNG hỗ trợ bán vé tại quầy / walk-in counter sale / offline ticket issuance. Toàn bộ booking đi qua Passenger App. Không có role "Ticket Staff" hay endpoint `POST /operator/bookings/manual` / `POST /operator/bookings/csv-import`. Operator chỉ có quyền **xem và monitor** booking, không tạo/sửa booking thay passenger. Trường hợp passenger đã mua vé qua kênh truyền thống ngoài hệ thống → manager dùng "Disable seat" để khóa ghế, KHÔNG tạo Booking trên hệ thống.
- Theo dõi vận hành: bản đồ thời gian thực tất cả chuyến đang chạy, nhận alert khi xe lệch tuyến, xác nhận và trigger đổi tuyến thay thế
- **Phát hiện xe trễ giờ (Delayed Vehicle Detection):**
  Tracking Service so sánh ETA thực tế (tính từ GPS) với `TripStop.estimatedArrivalTime`. Nếu ETA thực tế trễ hơn > 30 phút → gửi alert `TripDelayed` event → Notification Service gửi:
  - Push đến Operator Dashboard: "Chuyến [X] trễ ~[N] phút tại stop [Y]"
  - Push đến tất cả Passenger của chuyến: "Chuyến của bạn đang bị trễ, ETA mới: [time]"

  **Operator làm gì sau khi nhận delayed alert:**
  - Dashboard hiển thị chip "DELAYED" trên chuyến đó trong fleet map
  - Operator có thể: (1) không làm gì — tracking tự cập nhật ETA; (2) gửi thông báo tùy chỉnh cho hành khách; (3) trigger Alternative Route nếu delay do tắc đường (xem flow 6.4)
  - Không có auto-action — Operator quyết định, hệ thống chỉ cung cấp thông tin
- **Quản lý tải trọng khoang hàng:** theo dõi dung tích khoang đang sử dụng thực tế trên mỗi chuyến (tổng kg hàng ký gửi đã xác nhận lên xe), giới hạn tổng tải trọng per chuyến, nhận alert khi gần đạt giới hạn
- Quản lý hàng ký gửi: **chỉ monitor** (không tạo parcel thay người dùng — consistency với no walk-in), xem danh sách parcel theo chuyến, theo dõi trạng thái, review/approve EXTRA_LARGE parcel, theo dõi giới hạn trọng tải per chuyến
- Dashboard: thống kê chuyến, doanh thu, tỷ lệ lấp đầy ghế, tỷ lệ hủy
- **Export báo cáo Excel (.xlsx):** doanh thu theo chuyến/tuyến/tháng, vé bán, parcel, refund, tỷ lệ lấp đầy. Endpoint `GET /v1/operator/reports/export?reportType=...&format=xlsx`. Xem 4.5.
- **Áp voucher do platform phát hành + TỰ TẠO operator-owned voucher:** Operator có thể tự tạo/sửa/xóa (soft-delete) voucher `OPERATOR_FUNDED` scoped cho công ty mình (xem 4.4 — operator self-create flow, self-consented, NO consent fan-out). Operator cũng chỉ xem báo cáo voucher đã được dùng cho chuyến của mình. Voucher `OPERATOR_FUNDED` do System Admin targeted tới operator thì vẫn yêu cầu consent (xem funding block 4.4)
- RAG AI assistant: tra cứu quy trình nội bộ, chính sách tuyến, hướng dẫn hệ thống

### 4.4 System Admin Web

Cùng codebase NextJS với Operator Web, phân biệt qua role SYSTEM_ADMIN. Navigation và UI khác hoàn toàn.

**Chức năng:**
- Quản lý nhà xe: xem, **thêm** (tạo thủ công), **duyệt đơn đăng ký** (`Operator.registrationStatus` PENDING → APPROVED/REJECTED), khóa tạm thời (`SUSPENDED`), xóa nhà xe, xem thông tin chi tiết từng operator

> **Operator registration flow (self-registration + Admin approval):**
> Có 2 path để Operator tồn tại trong hệ thống:
> - **Path A — Self-registration (primary):** Nhà xe đăng ký qua form trên web (`POST /v1/operators/register` — public endpoint, không cần auth). Body gồm: `{ name, contactEmail, contactPhone, businessRegistrationNumber, taxCode, addressStreet, addressWard, addressDistrict, addressProvince, representativeName, representativePhone }`. System validate `businessRegistrationNumber` + `taxCode` không trùng operator hiện có (return `OPERATOR_DUPLICATE_REGISTRATION` / `OPERATOR_DUPLICATE_TAX_CODE` nếu trùng). Tạo `Operator { registrationStatus = PENDING }` + 1 User `{ role = OPERATOR_ADMIN, status = PENDING_EMAIL_VERIFICATION, phone = representativePhone }` cho người đại diện (email = contact email). **Auto-assign default `SubscriptionPlan` "Starter (Free Trial)" tới `OperatorSubscription` (status=PENDING_APPROVAL, startedAt/expiresAt=NULL — trial KHÔNG tick cho đến khi Admin APPROVE).** Gửi OTP verify email. Sau khi verify, tài khoản OPERATOR_ADMIN ở trạng thái chờ duyệt — chưa login được vào dashboard (Gateway block role OPERATOR_ADMIN nếu `Operator.registrationStatus != APPROVED`). Cùng transaction đăng ký, Identity ghi Outbox fact `identity.operator.registration_submitted { eventId, occurredAt, operatorId, companyName }`; Notification resolve active `SYSTEM_ADMIN` và gửi "Đơn đăng ký nhà xe mới". System Admin duyệt → atomic transaction: `Operator.registrationStatus = APPROVED` + `OperatorSubscription { status=ACTIVE, startedAt=approvedAt, expiresAt=approvedAt+30 days }` + publish event `identity.operator.approved` → OPERATOR_ADMIN nhận email "Đã duyệt, có thể đăng nhập".
>
> Khi Admin reject → atomic transaction:
> ```
>   UPDATE Operator SET registrationStatus = REJECTED, rejectedAt = now,
>                       rejectedByUserId = adminId, rejectReason = :reason
>   UPDATE OperatorSubscription SET status = CANCELLED, deletedAt = now
>                                   WHERE operatorId = :operatorId AND status = PENDING_APPROVAL
>   -- (soft delete — giữ record cho audit + cho phép Operator đăng ký lại với cùng email/taxCode nếu Admin reset thủ công)
> ```
> Email gửi Operator: "Đơn đăng ký bị từ chối. Lý do: [reason]. Bạn có thể submit lại đơn mới nếu cần."
> Nếu Operator submit lại với cùng `businessRegistrationNumber`/`taxCode`: return `OPERATOR_DUPLICATE_REGISTRATION` — cần System Admin manual reset (set deletedAt=null cũ + reactivate) trước khi cho register lại. Quyết định: KHÔNG cho self-resubmit để tránh spam đăng ký.
> - **Path B — Admin tạo thủ công (secondary):** System Admin tạo Operator record + User OPERATOR_ADMIN trực tiếp qua Admin dashboard. `registrationStatus` set thẳng `APPROVED`. Dùng cho case onboard nhà xe lớn hay migrate data.
>
> `Operator.registrationStatus = PENDING` luôn có nghĩa là "đang chờ Admin duyệt" — không bao giờ là initial state từ Path B. Agent cần implement endpoint `POST /v1/operators/register` (public, no JWT required) trong Identity & User Service (vì liên quan tạo User) với response `{ operatorId, message: "Đơn đăng ký đã nhận, vui lòng xác thực email" }`. Endpoint `POST /v1/admin/operators` (System Admin tạo thủ công, cần SYSTEM_ADMIN JWT) tách biệt.
- Quản lý tài khoản người dùng: khóa/mở khóa, **xem activity log** (đăng nhập, thao tác quan trọng — đặt vé, hủy vé, thay đổi profile, etc.)
- **Quản lý Station canonical cấp platform — data quality role (không gatekeep tạo):** System Admin **không phải là người tạo Station first** (operator tự tạo, xem 4.3). System Admin có quyền:
  - **Audit & merge duplicate:** khi 2 operator tạo Station gần giống (tên + tọa độ cách < 100m) → System Admin merge thành 1 record canonical, OperatorStation tự re-link sang Station còn lại.
  - **Normalize:** sửa name/coords/address/operatingHours cho chuẩn (vd "BX Miền Đông" → "Bến xe Miền Đông").
  - **Toggle `supportsShuttle`:Operator đang khai thác Station** (qua OperatorStation active) HOẶC System Admin được set/unset flag này. Quyết định — nhất quán với 6.14. Trường hợp Station được nhiều operator khai thác: flag là **property của Station canonical** (không per-operator) — toggle bởi 1 operator sẽ áp cho cả các operator khác đang khai thác. Nếu cần per-operator opt-in/out shuttle service trên cùng Station → defer v2 (thêm field `OperatorStation.shuttleEnabled`). System Admin có quyền override khi cần.
  - **Soft delete Station không còn hoạt động** — kéo theo OperatorStation set isActive=false.
  - Endpoint: `PATCH /v1/admin/stations/{id}`, `POST /v1/admin/stations/{primary}/merge { duplicateId }`.

  **ActivityLog — entity requirements (thuộc Identity & User Service):**
  - Link tới userId
  - `action` enum: LOGIN | LOGOUT | BOOK_TICKET | CANCEL_TICKET | UPDATE_PROFILE | CHANGE_PASSWORD | CREATE_OPERATOR | APPROVE_OPERATOR | LOCK_USER | UNLOCK_USER | STATION_MERGED | STATION_NORMALIZED (mở rộng khi cần)
  - `metadata` JSONB nullable — context tùy action (bookingId, userAgent, etc.)
  - `ipAddress` string, `createdAt` datetime
  - Query pattern phổ biến: theo userId order by createdAt DESC → cần index phù hợp

- Xem toàn bộ dữ liệu hệ thống (chuyến, tuyến, điểm dừng, station), **kiểm tra và chuẩn hóa dữ liệu:**

  > **Data normalization — TBD (out of scope v1 implementation, spec để tham chiếu):**
  > Feature "flag dữ liệu sai" chưa có spec chi tiết. Gợi ý v2 nếu cần implement:
  > - **Tuyến trùng:** 2 Route cùng `originStationId` + `destinationStationId` + `operatorId` → flag warning (không phải error, operator có thể có multiple schedules)
  > - **Stop thiếu tọa độ:** RouteStop có `stopId` → Stop không có lat/lng → flag error (Tracking Service không tính được ETA)
  > - **Chuyến không hợp lệ:** Trip có `departureDateTime` < now + status = SCHEDULED (quá giờ nhưng chưa update status) → flag warning
  > Agent thiết kế DB/API KHÔNG cần implement validation engine này cho v1.
- **Báo cáo & thống kê cấp nền tảng (aggregate across all operators):** doanh thu theo nhà xe + toàn hệ thống, số chuyến/hành khách, tỷ lệ lấp đầy ghế, tỷ lệ hủy vé, hiệu suất vận hành — khác với dashboard Operator (chỉ thấy dữ liệu công ty mình), System Admin thấy toàn bộ và so sánh giữa các operator
- **Quản lý voucher và khuyến mãi cấp nền tảng (PLATFORM-WIDE) + operator-owned voucher:** System Admin tạo/sửa/xóa voucher platform-wide (cả `VIETRIDE_FUNDED` và `OPERATOR_FUNDED`). `OPERATOR_FUNDED` admin voucher vẫn đi qua consent fan-out flow (xem funding block bên dưới, v7:640-646 UNCHANGED) và vẫn là platform-owned (`owner_operator_id IS NULL`). Trong v1, "promotion/khuyến mãi" được model bằng Voucher campaign, KHÔNG có entity `Promotion` riêng. Voucher áp dụng cho toàn hệ thống (tất cả operator hoặc subset operator). Mỗi voucher cấu hình: type (PERCENT_OFF / FIXED_AMOUNT), giá trị, min order, max discount, usage limit per user, total usage limit, validFrom, validUntil, applicableRouteIds (nullable — null = áp mọi route), applicableOperatorIds (nullable — null = áp mọi operator targeted). **NEW (re-plan 2026-06-19):** OPERATOR_ADMIN CŨNG được tự tạo operator-owned `OPERATOR_FUNDED` voucher (full CRUD) scoped cho công ty mình — xem funding block "Operator self-create flow" bên dưới. System Admin có endpoint `GET /v1/admin/vouchers` chỉ list platform vouchers (`owner_operator_id IS NULL`); operator dùng `GET /v1/operator/vouchers` để list voucher của chính mình (`owner_operator_id = caller.operatorId`).

  > **Voucher funding model:**
  >
  > Mỗi Voucher có `fundingType` enum **`VIETRIDE_FUNDED | OPERATOR_FUNDED`** quyết định ai chịu chi phí discount + flow opt-in:
  >
  > **`VIETRIDE_FUNDED` — VietRide chịu chi phí:**
  > - VietRide trả phần discount cho operator (settle qua subscription credit cuối tháng hoặc direct payout).
  > - Voucher **áp dụng global**, KHÔNG cần operator opt-in.
  > - Operator **không có quyền block** voucher loại này cho chuyến của họ — họ nhận đủ giá vé gốc, VietRide trừ phần discount khỏi tài khoản VietRide.
  > - Use case: VietRide marketing campaign (giảm 50k cho user mới, voucher mừng Tết, etc.) — chi phí marketing platform-wide.
  > - Flow apply: Booking Service apply discount bình thường; Payment Service settle: operator nhận `Booking.totalAmount + discountAmount` (giá gốc), VietRide chịu `discountAmount`.
  >
  > **`OPERATOR_FUNDED` — Operator chịu chi phí:**
  > - Operator trả phần discount (nhận `Booking.totalAmount - discountAmount` = số tiền sau discount).
  > - Voucher **CHỈ active cho operator đã opt-in/consent**.
  > - Flow opt-in:
  >   1. Admin tạo Voucher `{ fundingType: OPERATOR_FUNDED, applicableOperatorIds: [op1, op2, ...] }`.
  >   2. Booking Service INSERT 1 `OperatorVoucherConsent` record per operator targeted với `status=PENDING` và ghi cùng transaction một Outbox fact `booking.voucher.consent_requested { eventId, occurredAt, voucherId, operatorId, voucherCode, voucherType, voucherValue }` cho mỗi consent mới.
  >   3. Notification consume fact và push mỗi OPERATOR_ADMIN: "VietRide đề xuất voucher [code] giảm X% cho chuyến của bạn. Bạn đồng ý áp dụng?"
  >   4. Operator vào dashboard "Voucher đề xuất" → bấm ACCEPT/REJECT → UPDATE `OperatorVoucherConsent.status`.
  >   5. Voucher chỉ active cho operator có `status=ACCEPTED`. Operator REJECTED → Booking thuộc operator đó KHÔNG apply được voucher (error `VOUCHER_NOT_APPLICABLE`).
  >   6. Operator có thể REJECT sau khi đã ACCEPT (revoke consent) → voucher inactive cho operator đó từ thời điểm đó (booking đã CONFIRMED giữ nguyên discount).
  > - Use case: Operator chấp nhận giảm giá để boost demand chuyến vắng khách, hoặc partnership campaign với VietRide.
  >
  > **applicableOperatorIds null behavior khác nhau:**
  > - `fundingType=VIETRIDE_FUNDED` + `applicableOperatorIds=null` → áp mọi operator (không cần opt-in vì VietRide chịu chi phí).
  > - `fundingType=OPERATOR_FUNDED` + `applicableOperatorIds=null` → System tạo OperatorVoucherConsent PENDING cho **mọi operator** đang APPROVED. Chỉ operator ACCEPTED mới có voucher active. Use case hiếm, thường operator-funded sẽ targeted subset. (Lưu ý: Day-14 defer — admin `OPERATOR_FUNDED` yêu cầu `applicableOperatorIds` non-null, null bị reject `VALIDATION_ERROR`; fan-out mọi operator APPROVED là enhancement tương lai vì cần Identity feed.)
  >
  > **Operator self-create flow (NEW — re-plan 2026-06-19, human-approved):**
  > - OPERATOR_ADMIN **được tự tạo** operator-owned voucher `OPERATOR_FUNDED` scoped cho công ty mình (full CRUD: create / PATCH / DELETE soft-delete / activate / deactivate). `owner_operator_id` = caller operator (set server-side), `funding_type` FORCED `OPERATOR_FUNDED` (nếu body truyền `VIETRIDE_FUNDED` → `VOUCHER_FORBIDDEN_FUNDING` 422). `applicableOperatorIds` FORCED bằng chính caller operator.
  > - **Consent-skip (self-consented):** operator-owned voucher KHÔNG tạo `OperatorVoucherConsent` rows (operator tự tạo = tự đồng ý), KHÔNG đi qua consent fan-out flow ở v7:640-646 (flow đó chỉ áp cho admin `OPERATOR_FUNDED` voucher). Operator-owned voucher KHÔNG emit integration event (no consumer — Q7).
  > - **PATCH freeze-on-first-use (Q6):** `code`/`type`/`fundingType`/`ownerOperatorId` ALWAYS immutable. While `voucher_usages` count == 0: operator được edit `name`/`value`/`minOrderAmount`/`maxDiscountAmount`/`totalUsageLimit`/`perUserLimit`/`validFrom`/`validUntil`/`applicableRouteIds`. Once >=1 usage exists: economic fields `value`/`minOrderAmount`/`maxDiscountAmount` FREEZE (edit → `VOUCHER_LOCKED` 409); chỉ còn `name`, EXTEND `validUntil` (không rút ngắn dưới hiện tại), LOOSEN limits, `applicableRouteIds`, và deactivate là editable.
  > - **Tenant isolation:** mọi read/update/delete/activate scoped `owner_operator_id == caller` (cross-operator → `404 VOUCHER_NOT_FOUND`, không leak existence).
  > - **Code uniqueness:** global unique among non-soft-deleted (`uq_vouchers_code` partial `WHERE deleted_at IS NULL`); duplicate → `VOUCHER_CODE_CONFLICT` 409. Soft-delete (set `deleted_at`) cho phép tái sử dụng code.
  > - **Endpoints:** `GET /v1/operator/vouchers`, `POST /v1/operator/vouchers`, `PATCH /v1/operator/vouchers/{id}`, `DELETE /v1/operator/vouchers/{id}`, `POST /v1/operator/vouchers/{id}/activate`, `POST /v1/operator/vouchers/{id}/deactivate` — role `OPERATOR_ADMIN`. GET list lấy `operatorId` từ JWT claim và chỉ trả voucher của chính operator; POST create yêu cầu `Idempotency-Key`; PATCH/DELETE/activate/deactivate behavior-idempotent (no key — mirror driver-schedule-activation precedent).
  >
  > **Checkout applicability branch (Q8 RESOLVED — canonical validation order):** khi passenger áp `voucherCode` tại `POST /v1/bookings` (và round-trip), voucher applicable iff EITHER:
  > - **(a) Operator-owned voucher:** `owner_operator_id == Trip.operatorId` → self-funded, **SKIP consent check** AND skip operator-scope check (chỉ branch (a) bypass 2 check này). TẤT CẢ filter khác (route-scope `applicable_route_ids`, validity window, min_order, total/per-user usage limits) vẫn apply universally.
  > - **(b) Platform/admin voucher:** `owner_operator_id IS NULL` → (VIETRIDE_FUNDED scope match) HOẶC (OPERATOR_FUNDED với `OperatorVoucherConsent.status=ACCEPTED` cho Trip.operatorId). Admin `OPERATOR_FUNDED` chưa ACCEPTED → `VOUCHER_NOT_APPLICABLE` (không reveal opt-in mechanics).
  >
  > Canonical validation order: (1) exists + not soft-deleted + `is_active`; (2) within `valid_from..valid_until`; (3) applicability — operator-scope [branch (a) auto-pass / branch (b) `applicableOperatorIds`] + route-scope + consent [branch (b) OPERATOR_FUNDED yêu cầu ACCEPTED]; (4) `min_order_amount` met; (5) usage limits (total + per-user) not exceeded; (6) compute discount (PERCENT_OFF cap `max_discount_amount`, FIXED_AMOUNT, round half-up `MidpointRounding.AwayFromZero`, NO floor-1000). Mỗi failure throw registered coded exception (`VOUCHER_NOT_FOUND` 404 / `VOUCHER_EXPIRED` / `VOUCHER_NOT_APPLICABLE` / `VOUCHER_USAGE_LIMIT_REACHED` / `VOUCHER_USER_LIMIT_REACHED` / `VOUCHER_MIN_ORDER_NOT_MET` 422). `voucher_usages.funded_by` = snapshot `funding_type` tại apply time.
  >
  > **Voucher list ownership split:** `GET /v1/admin/vouchers` (SYSTEM_ADMIN) — chỉ list platform vouchers (`owner_operator_id IS NULL`), optional filter `?fundingType=`/`?isActive=`, paged + sortBy-whitelisted (default `createdAt desc`), read-only (no Idempotency-Key). Endpoint này không nhận `ownerOperatorId` từ client và không dùng để audit operator-owned vouchers. `GET /v1/operator/vouchers` (OPERATOR_ADMIN) — list voucher có `owner_operator_id = caller.operatorId`, optional filter `?isActive=`, paged + sortBy-whitelisted. Management list response phải trả cấu hình áp dụng gồm `minOrderAmount`, `maxDiscountAmount`, usage limits, `newUserOnly`, `applicableServices` (`BOOKING`, `PARCEL`, hoặc cả hai), `applicablePaymentMethods`, `applicableOperatorIds`, và `applicableRouteIds` để FE phân biệt voucher dùng cho đặt vé hay gửi hàng. v1 returns chỉ active (non-soft-deleted) voucher — respects EF `HasQueryFilter(deleted_at == null)`; soft-deleted audit deferred.
  >
  > **Admin Web UI:** form tạo voucher có dropdown `fundingType`, multi-select `applicableOperatorIds` (autocomplete). Sau khi save, dashboard hiển thị consent status per operator (PENDING/ACCEPTED/REJECTED counts).
  >
  > **Operator Web UI mới:** module "Voucher đề xuất" trong sidebar Operator Web — list voucher OPERATOR_FUNDED targeted với operator này, status PENDING/ACCEPTED/REJECTED, action button consent/revoke.
  >
  > **Endpoint spec:**
  > ```
  > GET /v1/operator/voucher-consents?status=PENDING|ACCEPTED|REJECTED
  >   Role: OPERATOR_STAFF/ADMIN
  >   Response: list { id, voucherId, voucherCode, voucherType, voucherValue, validFrom, validUntil,
  >                    minOrderAmount, maxDiscountAmount, applicableRouteIds, status,
  >                    requestedAt, respondedAt, respondedByUserId }
  >
  > POST /v1/operator/voucher-consents/{id}/accept
  >   Role: OPERATOR_ADMIN only (decision có business impact, không cho OPERATOR_STAFF)
  >   Precondition: status = PENDING
  >   Logic:
  >     UPDATE OperatorVoucherConsent SET status=ACCEPTED, respondedAt=now,
  >                                       respondedByUserId=callerUserId
  >     Publish event `booking.voucher.consent_accepted { voucherId, operatorId }`
  >       → Notification Service push OPERATOR_ADMIN xác nhận; alert admin VietRide track adoption
  >
  > POST /v1/operator/voucher-consents/{id}/reject  body: { reason: text optional }
  >   Role: OPERATOR_ADMIN
  >   Precondition: status IN (PENDING, ACCEPTED)
  >   Logic:
  >     UPDATE OperatorVoucherConsent SET status=REJECTED, respondedAt=now,
  >                                       respondedByUserId=callerUserId, rejectReason=:reason
  >     - Nếu chuyển từ ACCEPTED → REJECTED (revoke after accept):
  >       Booking đã CONFIRMED + apply voucher đó: KHÔNG roll back (giữ nguyên discount).
  >       Booking mới tạo sau respondedAt: voucher KHÔNG còn applicable cho operator này.
  >     Publish event `booking.voucher.consent_rejected`
  >
  > Admin view consent status across operators:
  > GET /v1/admin/vouchers/{voucherId}/consents
  >   Role: SYSTEM_ADMIN
  >   Response: list operator-voucher consent records cho voucher cụ thể
  > ```
  > Add `OperatorVoucherConsent.rejectReason text nullable` vào entity Booking Service spec.
- Quản lý RAG knowledge base: phê duyệt/từ chối tài liệu upload, phân quyền truy cập theo role, audit log AI conversations
- **Quản lý Subscription Plan + Operator Subscription:** xem 4.5. Admin tạo các gói Subscription Plan (limits + module flags), assign/upgrade gói cho từng operator, track usage counters.

### 4.5 Business Model — SaaS Subscription

**Revenue model:** VietRide thu tiền từ **subscription** mà các nhà xe (operator) trả định kỳ. **KHÔNG có commission per ticket, KHÔNG có platform fee per booking, KHÔNG có service charge % trên vé.** Mọi giao dịch vé/parcel chỉ luân chuyển giữa passenger và operator (qua VietRide làm middleware, không thu phí giao dịch).

**Subscription structure** — theo thời gian + giới hạn tài nguyên + module flags:

**a) Resource limits (per Subscription Plan):**
- `maxVehicles` — số xe operator có thể đăng ký
- `maxDrivers` — số tài khoản DRIVER
- `maxAssistants` — số tài khoản ASSISTANT
- `maxOperatorUsers` — tổng số OPERATOR_STAFF + OPERATOR_ADMIN
- `maxRoutes` — số tuyến đường
- `maxTripsPerMonth` (hoặc `maxOnlineTicketsPerMonth`) — giới hạn vận hành theo tháng

**b) Module flags:**
- `enableParcel` — bật/tắt module parcel
- `enableShuttle` — bật/tắt shuttle service
- `enableRag` — bật/tắt RAG AI assistant cho operator

**c) Hành vi khi vượt giới hạn:**
- Operator cố gắng add resource vượt limit → API return error + upsell message: "Bạn đã đạt giới hạn [maxVehicles] xe của gói [planName]. Nâng cấp gói để thêm xe."
- Hành động bị block: tạo vehicle/driver/route/trip vượt limit, bật module chưa enable.
- Soft warning khi đạt 80% limit (notification cho OPERATOR_ADMIN).

**c.0) `maxTripsPerMonth` enforcement:**

Counter `OperatorSubscription.currentTripsThisMonth` increment mỗi khi 1 Trip mới được tạo (manual hoặc auto-generate từ DriverSchedule). Hangfire reset counter về 0 vào ngày 1 hàng tháng 00:01 (`lastResetAt = now`).

**Manual create Trip vượt limit (operator click form trên dashboard):**
- API `POST /v1/operator/trips` validate trước khi INSERT:
  ```
  IF currentTripsThisMonth >= maxTripsPerMonth AND Trip.source != VEHICLE_SUBSTITUTION:
    return 409 SUBSCRIPTION_LIMIT_EXCEEDED
      { detail: "Đã đạt giới hạn [N] chuyến/tháng của gói [planName]. Nâng cấp để tạo thêm." }
  ```
- Block thẳng — operator phải upgrade plan hoặc đợi tháng sau.

Khi `Trip.source = VEHICLE_SUBSTITUTION` (Trip_new tạo từ flow 6.12 sau Trip_old DISRUPTED), counter check `maxTripsPerMonth` được **SKIP**. Lý do: substitution là replacement trip để hoàn thành cam kết booking với passenger — không phải new business activity, không nên block. Counter `currentTripsThisMonth` cũng KHÔNG increment khi insert substitution trip (chỉ count "new business" trips). Thêm enum value `VEHICLE_SUBSTITUTION` vào `Trip.source` (cùng MANUAL, AUTO_FROM_SCHEDULE).

**Hangfire auto-generate Trip từ DriverSchedule vượt limit:**
- Khi `Hangfire weekly job` HOẶC `on-create DriverSchedule trigger` muốn INSERT Trip mới:
  ```
  FOR EACH ngày trong 14 ngày tới khớp dayOfWeek:
    IF Operator.currentTripsThisMonth >= maxTripsPerMonth (cho tháng của ngày đó):
      → SKIP generate Trip này (KHÔNG insert)
      → INSERT TripGenerationSkipLog { operatorId, driverScheduleId, skippedDate, reason: "SUBSCRIPTION_LIMIT_EXCEEDED", createdAt }
      → Publish event `subscription.limit.trip_skipped` → Notification Service
         → Push OPERATOR_ADMIN: "Đã skip generate chuyến ngày [date] do vượt giới hạn [N] chuyến/tháng của gói [planName]. Nâng cấp gói để tự động generate tiếp."
    ELSE:
      → INSERT Trip + increment currentTripsThisMonth atomically
  ```
- Dashboard hiển thị banner "Đã skip [M] chuyến tự động trong tháng do giới hạn gói" với link "Nâng cấp gói".
- Operator có thể manual create từng Trip cụ thể nếu cần (cùng error check), hoặc đợi sang tháng sau Hangfire re-run.

**Entity bổ sung:** `TripGenerationSkipLog { id, operatorId, driverScheduleId UUID NOT NULL, skippedDate, reason enum SUBSCRIPTION_LIMIT_EXCEEDED|VEHICLE_CONFLICT|DRIVER_CONFLICT|OTHER, message text nullable, createdAt }` — thuộc Trip-Route-Vehicle Service. **`driverScheduleId` NOT NULL** vì entity này CHỈ dùng cho path Hangfire auto-generate from DriverSchedule. Manual create Trip vượt limit là synchronous error response (`SUBSCRIPTION_LIMIT_EXCEEDED`), không insert log. Dùng cho audit + dashboard reporting "đã skip X chuyến tháng này".

**Entities:**

```
SubscriptionPlan {
  id UUID
  name string ("Starter", "Pro", "Enterprise")
  pricePerMonth BIGINT VND
  pricePerYear BIGINT VND  // optional, discount per year
  maxVehicles int
  maxDrivers int
  maxAssistants int
  maxOperatorUsers int
  maxRoutes int
  maxTripsPerMonth int
  enableParcel boolean
  enableShuttle boolean
  enableRag boolean
  isActive boolean
  timestamps
}

OperatorSubscription {
  id UUID
  operatorId FK → Operator UNIQUE  // 1 operator có 1 active subscription tại 1 thời điểm
  planId FK → SubscriptionPlan
  status enum: PENDING_APPROVAL | ACTIVE | EXPIRED | CANCELLED | PENDING_PAYMENT
  startedAt datetime nullable
  expiresAt datetime nullable
  paymentMethod enum: VNPAY nullable
  // Usage counters (current period, reset per billing cycle):
  currentVehicles int
  currentDrivers int
  currentAssistants int
  currentOperatorUsers int
  currentRoutes int
  currentTripsThisMonth int
  lastResetAt datetime
  timestamps
}
```

**c.1) Default Plan seed + auto-assign:**

Khi deploy lần đầu, **EF Core seed migration** trong Identity Service tạo 1 record `SubscriptionPlan` mặc định:

```
SubscriptionPlan "Starter (Free Trial)" (id=fixed-UUID, isActive=true) {
  pricePerMonth: 0
  pricePerYear: 0
  maxVehicles: 3
  maxDrivers: 5
  maxAssistants: 5
  maxOperatorUsers: 3
  maxRoutes: 5
  maxTripsPerMonth: 100
  enableParcel: false        // operator phải upgrade plan để bật parcel
  enableShuttle: false       // shuttle là feature trả phí
  enableRag: true            // RAG free cho onboarding
}
```

**Auto-assign khi Operator self-register:** Endpoint `POST /v1/operators/register` (xem 4.4) trong cùng DB transaction với tạo `Operator` + OPERATOR_ADMIN `User` → INSERT `OperatorSubscription { operatorId, planId = default_plan_id, status=PENDING_APPROVAL, startedAt=NULL, expiresAt=NULL, paymentMethod=null }`. **Trial KHÔNG tick trong thời gian `Operator.registrationStatus = PENDING`.** Khi System Admin duyệt operator (`registrationStatus → APPROVED`) → trong cùng transaction update `OperatorSubscription { status=ACTIVE, startedAt=approvedAt, expiresAt=approvedAt + 30 days }`. Operator được full 30 ngày trial tính từ thời điểm có thể login thực sự, không bị mất time chờ Admin duyệt.

**`OperatorSubscription.status` enum:** `PENDING_APPROVAL | ACTIVE | EXPIRED | CANCELLED | PENDING_PAYMENT`
- `PENDING_APPROVAL`: vừa register, đợi Admin duyệt operator. Subscription đã link plan nhưng `startedAt`/`expiresAt` đều NULL — không tick.
- `ACTIVE`: operator APPROVED, trial/paid period đang chạy. `startedAt`/`expiresAt` đã set.
- `PENDING_PAYMENT`: gói trả phí, chưa thanh toán (Path B Admin chọn paid plan, hoặc operator upgrade).
- `EXPIRED`/`CANCELLED`: terminal hoặc reactivatable qua upgrade.

**Khi Path B (System Admin tạo Operator thủ công):** Admin có thể chọn plan ở form tạo Operator (default field = Starter Free Trial). Operator được tạo với `registrationStatus = APPROVED` trực tiếp → OperatorSubscription tạo với status=ACTIVE + startedAt=now + expiresAt=now+30 days ngay. Nếu Admin pick plan trả phí → `OperatorSubscription.status = PENDING_PAYMENT`, Operator phải thanh toán trước khi vào ACTIVE.

**`PENDING_PAYMENT` lifecycle:**

```
Operator chọn upgrade lên paid plan:
  → Giữ nguyên OperatorSubscription.activePlanId (nguồn entitlement duy nhất)
  → Tạo SubscriptionUpgradeAttempt(targetPlanId, dueAt=now+15 phút)
  → OperatorSubscription.status = PENDING_PAYMENT
  → Tạo Payment session referenceType=SUBSCRIPTION, status=PENDING_REDIRECT

Trong 15 phút:
  - Login/read/write vẫn dùng limits và modules của activePlanId.
  - Cancel/failure/timeout chỉ đóng Payment session hiện tại; operator có thể retry bằng session mới,
    vnp_TxnRef mới; retry không kéo dài dueAt của attempt.
  - Payment SUCCEEDED trước dueAt phát `payment.subscription.payment_succeeded`; Identity đổi
    activePlanId sang targetPlanId đúng một lần và chuyển subscription về ACTIVE.

Reconciliation `identity.subscription-auto-revert` chạy mỗi phút:
  - Payment SUCCEEDED nhưng event bị mất → gọi cùng activation service với consumer.
  - Payment FAILED/EXPIRED → đồng bộ latest payment status và cho retry nếu còn hạn.
  - Đủ 15 phút → kiểm tra Payment lần cuối, expire session còn pending, đóng attempt EXPIRED và
    giữ active plan cũ (hoặc Starter theo fallback policy).
  - Callback success sau dueAt bị từ chối, không credit ledger/invoice và không activate plan.

Payment là nguồn sự thật duy nhất về trạng thái tiền. Return URL chỉ phục vụ UX; canonical IPN là
`GET|POST /v1/payments/vnpay-ipn`. Các endpoint IPN cũ chỉ là alias tương thích.
```

**Error codes mới:** `SUBSCRIPTION_PAYMENT_PENDING` (operator gọi endpoint upgrade trong khi đang có PENDING_PAYMENT — phải resolve trước).

**Plans khác (Pro, Enterprise, v.v.):KHÔNG seed sẵn** — System Admin tạo thủ công qua Admin Web (`POST /v1/admin/subscription-plans`) sau khi deploy. Pricing + limits do business team quyết định, không hardcode.

**Trial expire behavior:** Hangfire job (daily 00:30) check `OperatorSubscription WHERE status=ACTIVE AND expiresAt < now`:
- Auto-set `status = EXPIRED` → Operator login vẫn được nhưng Gateway block write actions (tạo Trip/Route/Vehicle) với error `SUBSCRIPTION_EXPIRED`.
- Notification gửi OPERATOR_ADMIN: "Gói Starter đã hết hạn. Vui lòng nâng cấp để tiếp tục vận hành."
- Read actions (xem booking, xem báo cáo) vẫn cho phép — không block toàn diện để operator có data view khi đàm phán upgrade.
- EXPIRED operator có doanh thu đã earned trước EXPIRED → `OperatorTripSettlement` cho các trip terminal trước EXPIRED vẫn được tạo và auto-settle bằng cách debit `PlatformWallet` + credit `OperatorWallet` theo cycle Monday weekly (Hangfire không filter theo subscription status). Operator nhận đủ tiền đã earned vào ví nội bộ. **Lý do:** earned revenue là tài sản của operator, không thể hold lại vì subscription expired (legal risk + bad faith). Xem 4.6.

**Pre-expiry warning (thêm):** Hangfire `subscription-trial-expiring-warn` job (daily 09:00):
```
SELECT OperatorSubscription WHERE status=ACTIVE
  AND expiresAt BETWEEN now + 2 days AND now + 4 days
  AND trialExpiringWarnSentAt IS NULL
→ Push + email OPERATOR_ADMIN: "Gói [planName] hết hạn trong [N] ngày. Nâng cấp ngay để không gián đoạn."
→ UPDATE OperatorSubscription SET trialExpiringWarnSentAt = now
```
OperatorSubscription thêm field `trialExpiringWarnSentAt datetime nullable`.

**d) Báo cáo tài chính cho Operator — Excel export:**

Operator Web có module **Export Báo cáo Excel (.xlsx)** với các báo cáo:
- Doanh thu theo chuyến / tuyến / tháng
- Số vé bán (CONFIRMED + COMPLETED)
- Số parcel + doanh thu parcel
- Refund (CANCELLED + REFUNDED amount)
- Tỷ lệ lấp đầy ghế, tỷ lệ hủy

Endpoint: `GET /v1/operator/reports/export?reportType=REVENUE_BY_TRIP&from=...&to=...&format=xlsx`. BE tạo file Excel (dùng EPPlus hoặc ClosedXML) → return file download.

**e) Subscription Invoice — VietRide → Operator (B2B SaaS billing):**

VietRide xuất **invoice/hóa đơn** cho operator cho mỗi kỳ subscription. Operator xuất hóa đơn vé xe cho passenger là việc **ngoài hệ thống VietRide** (operator có nghĩa vụ theo NĐ 123/2020, dùng provider e-invoice riêng — out of scope app).

**Scope v1:**
- **Generate Invoice record + PDF download** cho mỗi OperatorSubscription payment success.
- KHÔNG integrate với e-invoice provider thật (VNPT/Misa/Viettel) — defer v2.
- Invoice PDF chứa: Invoice number, kỳ subscription (from/to), gói (planName), số tiền, VAT note (operator tự kê khai), thông tin VietRide (publisher), thông tin operator (buyer).

**Invoice entity (thuộc Payment & Wallet Service):**
```
Invoice {
  id UUID
  invoiceNumber string UNIQUE  // format VR-INV-yyyyMM-XXXXXX
  operatorId FK → Operator
  operatorSubscriptionId FK → OperatorSubscription
  paymentId FK → Payment       // payment đã thanh toán subscription
  amount BIGINT VND            // = OperatorSubscription.amount paid
  periodFrom datetime          // = OperatorSubscription.startedAt
  periodTo datetime            // = OperatorSubscription.expiresAt
  status enum: DRAFT | ISSUED | CANCELLED
  issuedAt datetime nullable
  pdfUrl string nullable       // Firebase Storage signed URL của PDF
  eInvoiceProviderRef string nullable  // v2: ref ID từ provider thật
  metadata JSONB nullable      // tax info, billing address, etc.
  timestamps
}
```

**Flow:**
1. OperatorSubscription được tạo + Payment SUCCEEDED → Payment Service publish `SubscriptionPaymentSucceeded` event.
2. Payment Service consume → INSERT PlatformWalletTransaction { type=CREDIT, referenceType=SUBSCRIPTION_PAYMENT, amount=OperatorSubscription.amount, referenceId=paymentId } để ghi nhận platform revenue.
3. Payment Service tạo Invoice (status=DRAFT) → generate PDF qua library (QuestPDF cho .NET) → upload Firebase Storage → set status=ISSUED + pdfUrl.
4. Publish `InvoiceIssued` event → Notification Service push + email cho OPERATOR_ADMIN với link download.
5. Endpoint: `GET /v1/operator/invoices` (list), `GET /v1/operator/invoices/{id}/download` (signed URL PDF).

**E-invoice provider integration (feature flagged v2):** thêm config flag `enableEInvoiceProvider` (env var), tích hợp VNPT/Misa/Viettel API để xuất e-invoice chính thức theo NĐ 123/2020.

### 4.6 Platform Wallet, Operator Wallet & Trip Settlement

**Mô hình thu tiền (v1):**

VietRide là **merchant trung gian** thu tiền vé/parcel của passenger. Tất cả VNPay merchant config dùng tài khoản của **VietRide** (1 merchant ID duy nhất). Tiền vào tài khoản VietRide được ghi nhận vào **PlatformWallet** như clearing/holding pool nội bộ; doanh thu sau đó được settle vào **ví nội bộ** của operator trên platform theo cơ chế hold 7 ngày + chu kỳ thứ Hai hàng tuần.

```
Passenger thanh toán
  ├─ Wallet payment   → trừ PassengerWallet
  │                     → credit PlatformWallet holding pool
  │                     → KHÔNG credit OperatorWallet ngay
  │
  └─ VNPay payment    → tiền vào VNPay merchant của VietRide
                        → credit PlatformWallet holding pool
                        → doanh thu chuyến hold 7 ngày sau Trip terminal (COMPLETED/DISRUPTED)
                        → Hangfire Monday weekly debit PlatformWallet + credit OperatorWallet
```

> **v1 — KHÔNG có bank withdrawal:** Tiền chỉ luân chuyển trong platform giữa các ví nội bộ (PassengerWallet ↔ PlatformWallet holding pool ↔ OperatorWallet). v2 sẽ implement **Bank withdrawal** — operator yêu cầu rút từ wallet về tài khoản ngân hàng thật. Bank account fields trên `Operator` entity (`bankAccountName`/`bankAccountNumber`/`bankName`) vẫn nullable trên schema để chuẩn bị cho v2 nhưng **v1 không enforce nhập, không validate, không có UI banner**.

**Wallet/settlement model — 5 entity chính:**

`PlatformWallet` = ví clearing/holding nội bộ của VietRide (singleton). `PlatformWalletTransaction` = ledger immutable của holding pool. `OperatorWallet` = ví nội bộ (1-1 với Operator). `OperatorWalletTransaction` = ledger immutable của ví operator (giống pattern `Wallet` + `WalletTransaction` của passenger). `OperatorTripSettlement` = đơn vị settle 1 chuyến = chuyển tiền từ PlatformWallet sang OperatorWallet.

> **Lưu ý quan trọng:** `PlatformWallet` không thay thế tài khoản ngân hàng thật của VietRide. Nó là ledger nội bộ để biết bao nhiêu tiền đang được platform giữ ở trạng thái clearing/hold trước khi phân bổ sang PassengerWallet hoặc OperatorWallet. Reconciliation production vẫn cần so sánh tổng balance nội bộ với sao kê ngân hàng/VNPay.

**`PlatformWallet` aggregate entity:**
```
PlatformWallet {
  id UUID (PK)                          // singleton — 1 record duy nhất toàn hệ thống
  balance BIGINT NOT NULL DEFAULT 0     // VND, clearing/holding pool; CHECK >= 0
  currency char(3) DEFAULT 'VND'
  rowVersion int                        // optimistic lock
  createdAt datetime
  updatedAt datetime
}
```

**`PlatformWalletTransaction` entity (immutable ledger of platform holding pool):**
```
PlatformWalletTransaction {
  id UUID
  type enum: CREDIT | DEBIT
  amount BIGINT                          // always positive; type quyết định chiều
  balanceBefore BIGINT
  balanceAfter BIGINT
  referenceType enum:
    BOOKING_PAYMENT_HOLD                 // booking paid by VNPay/PassengerWallet → platform hold
  | PARCEL_PAYMENT_HOLD                  // parcel paid by VNPay/PassengerWallet → platform hold
  | BOOKING_REFUND                       // refund booking về PassengerWallet → reduce hold
  | PARCEL_REFUND                        // refund parcel về PassengerWallet → reduce hold
  | TRIP_SETTLEMENT                      // settle netAmount sang OperatorWallet → reduce hold
  | SUBSCRIPTION_PAYMENT                 // subscription fee thuộc VietRide (platform revenue)
  | MANUAL_ADJUSTMENT                    // System Admin correction/reconciliation
  referenceId UUID nullable              // bookingId / parcelId / settlementId / paymentId / adjustmentId
  note text nullable
  createdAt datetime
}
```

Pattern UPDATE giống Wallet passenger (6.5): optimistic lock theo `rowVersion`. Mọi INSERT `PlatformWalletTransaction` phải atomic với UPDATE `PlatformWallet.balance`. Với PassengerWallet payment, PlatformWallet CREDIT thể hiện việc chuyển liability từ PassengerWallet sang holding pool cho booking/parcel; không có dòng tiền vật lý mới tại thời điểm đó.

**`OperatorWallet` aggregate entity:**
```
OperatorWallet {
  operatorId FK → Operator (PK, 1-to-1)
  balance BIGINT NOT NULL DEFAULT 0    // CHECK >= 0 — non-negative
  rowVersion int                         // optimistic lock
  createdAt datetime
  updatedAt datetime
}
```

Pattern UPDATE giống Wallet passenger (6.5): optimistic lock theo `rowVersion`. Bootstrap row tạo qua event `identity.operator.approved` consume → INSERT `operator_wallets { operator_id, balance=0 }` (UPSERT idempotent).

**`OperatorWalletTransaction` entity (immutable ledger of wallet):**
```
OperatorWalletTransaction {
  id UUID
  operatorId FK → Operator             // denormalized for query convenience
  type enum: CREDIT | DEBIT
  amount BIGINT                          // always positive; type quyết định chiều
  balanceBefore BIGINT
  balanceAfter BIGINT                   // = balanceBefore + (amount if CREDIT else -amount)
  referenceType enum: TRIP_SETTLEMENT | ADJUSTMENT
  referenceId UUID                      // tripSettlementId | (manual adjustment id if needed)
  note text nullable
  createdAt datetime
}
```

INSERT atomic với UPDATE `OperatorWallet.balance` (cùng DB transaction, optimistic lock check) — pattern y hệt `WalletTransaction` passenger.

**`OperatorTripSettlement` entity (per-Trip settlement marker):**
```
OperatorTripSettlement {
  id UUID
  operatorId FK → Operator
  tripId UUID                             // logical FK Trip-Route-Vehicle service
  netAmount BIGINT                        // gross revenue - refunds - operator-funded discounts
                                          // computed at SETTLEMENT time (not creation) for late-refund accuracy
  tripTerminalAt datetime                 // = Trip.completedAt hoặc Trip.disruptedAt
  eligibleAt datetime                     // = tripTerminalAt + 7 days (hold window)
  status enum: PENDING_HOLD | ELIGIBLE | SETTLED | CANCELLED
  settlementMethod enum: AUTO_WEEKLY | ADMIN_MANUAL nullable    // set khi settle
  settledAt datetime nullable
  settledByUserId UUID nullable          // SYSTEM_ADMIN userId nếu ADMIN_MANUAL; null nếu AUTO_WEEKLY
  walletTransactionId UUID nullable      // FK → OperatorWalletTransaction sau khi settled
  rowVersion int                          // optimistic lock cho status transition
  createdAt datetime
  updatedAt datetime
}
```

UNIQUE `(operatorId, tripId)` — 1 settlement record per Trip per Operator. Index `(status, eligibleAt)` cho Hangfire scan.

**Status machine:**
```
PENDING_HOLD → ELIGIBLE → SETTLED            (happy path)
PENDING_HOLD → CANCELLED                      (Trip CANCELLED before completion sau khi đã tạo settlement marker — rare)
ELIGIBLE → CANCELLED                          (manual admin cancel — edge case)
ELIGIBLE → SETTLED via Monday auto or admin manual
SETTLED là terminal.
```

**`OperatorLedgerEntry` entity — vẫn giữ làm AUDIT log per booking/parcel event:**
```
OperatorLedgerEntry {
  id UUID
  operatorId FK → Operator
  tripId UUID nullable                    // link Trip (NULL cho ADJUSTMENT/MANUAL khong gắn trip)
  entryType enum: BOOKING_REVENUE | PARCEL_REVENUE | BOOKING_REFUND | PARCEL_REFUND
                | VOUCHER_VIETRIDE_FUNDED_CREDIT  // VietRide trả phần discount
                | VOUCHER_OPERATOR_FUNDED_AUDIT      // audit-only, operator chịu discount, amount=0
                | ADJUSTMENT                         // manual adjust bởi System Admin
  amount BIGINT                                    // signed: dương=tăng net, âm=giảm net, 0=audit-only
  referenceType enum: BOOKING | PARCEL | VOUCHER_USAGE | MANUAL
  referenceId UUID
  note text nullable
  createdAt datetime
}
```

> **Khác biệt so với phiên bản trước:** (1) Thêm `tripId` để aggregate per trip cho settlement. (2) **Bỏ `balanceBefore`/`balanceAfter`** — concept "ledger balance" được thay bằng "wallet balance" trên `OperatorWallet`; ledger entries chỉ là audit log không track balance. (3) Bỏ enum value `PAYOUT` (không còn payout batch entity). (4) Bỏ enum value `PAYOUT_BATCH` khỏi `referenceType`.

**Net amount formula (compute tại SETTLEMENT time):**

```
netAmount = SUM(OperatorLedgerEntry.amount
                 WHERE operatorId=X AND tripId=Y
                 AND entryType IN (BOOKING_REVENUE, PARCEL_REVENUE,
                                   BOOKING_REFUND, PARCEL_REFUND,
                                   VOUCHER_VIETRIDE_FUNDED_CREDIT))

-- VOUCHER_OPERATOR_FUNDED_AUDIT có amount=0 → không ảnh hưởng SUM
-- Late refunds sau khi settlement marker đã tạo nhưng trước settled: tính vào SUM
-- Late refunds sau khi đã SETTLED: KHÔNG re-settle; admin handle qua wallet ADJUSTMENT (debit)
```

Nếu `netAmount <= 0` tại settle time (vd toàn bộ refund): SET settlement status = CANCELLED, không tạo WalletTransaction, không credit wallet. Log audit.

**Flow tổng thể:**

```
1. Booking/Parcel CONFIRMED + Payment SUCCEEDED:
   → Payment Service publish `payment.payment.succeeded` event
   → Payment Service tự consume:
      a. CREDIT PlatformWallet holding pool:
         - VNPay payment: INSERT PlatformWalletTransaction {
             type=CREDIT, referenceType=BOOKING_PAYMENT_HOLD/PARCEL_PAYMENT_HOLD,
             amount=paidAmount, referenceId=bookingId/parcelId, note="VNPay payment hold"
           } + UPDATE PlatformWallet.balance += paidAmount
         - PassengerWallet payment: DEBIT PassengerWallet như 6.5, đồng thời INSERT
           PlatformWalletTransaction CREDIT cùng amount để chuyển tiền từ passenger wallet
           liability sang platform hold cho booking/parcel.
      b. INSERT OperatorLedgerEntry {
         entryType = BOOKING_REVENUE / PARCEL_REVENUE,
         tripId = Booking.tripId / Parcel.tripId,
         amount = +Booking.totalAmount / +Parcel.depositAmount,
         referenceType = BOOKING / PARCEL,
         referenceId = bookingId / parcelId
       }
   → KHÔNG update OperatorWallet ngay. Tiền "đang chờ" qua TripSettlement sau này.

2. Voucher applied khi Booking CONFIRMED:
   - VIETRIDE_FUNDED: INSERT OperatorLedgerEntry {
       entryType = VOUCHER_VIETRIDE_FUNDED_CREDIT,
       tripId = Trip.id,
       amount = +discountAmount,
       referenceType = VOUCHER_USAGE, referenceId = voucherUsageId
     }
     → Operator sẽ nhận đủ giá gốc khi settle (Booking.totalAmount + VietRide-funded credit = gross).
   - OPERATOR_FUNDED: INSERT OperatorLedgerEntry {
       entryType = VOUCHER_OPERATOR_FUNDED_AUDIT,
       tripId = Trip.id, amount = 0,
       referenceType = VOUCHER_USAGE, referenceId = voucherUsageId,
       note = "Voucher [code] applied — discount [X] VND borne by operator"
     }

3. Booking/Parcel REFUND (trip CANCELLED operator, hoặc parcel REJECTED/RETURNED):
   → Nếu refund xảy ra trước khi settlement SETTLED: CREDIT PassengerWallet refund + DEBIT PlatformWallet holding pool trong cùng transaction:
      INSERT PlatformWalletTransaction { type=DEBIT,
        referenceType=BOOKING_REFUND/PARCEL_REFUND, amount=refundAmount,
        referenceId=bookingId/parcelId }
   → INSERT OperatorLedgerEntry { entryType = BOOKING_REFUND / PARCEL_REFUND,
                                     tripId, amount = -refundAmount, ... }
   → Nếu refund xảy ra TRƯỚC settlement (rất phổ biến — trip CANCELLED tức là không bao giờ
      terminal COMPLETED/DISRUPTED → không có TripSettlement → refund chỉ là audit ledger entry).
      Tiền refund cho passenger chảy từ PlatformWallet holding pool, KHÔNG debit OperatorWallet
      vì wallet chưa từng credit cho trip này.
   → Nếu refund xảy ra GIỮA settlement marker tạo và settled: ledger entry sẽ được tính vào netAmount
     khi compute tại settle time → giảm settlement amount.
   → Nếu refund xảy ra SAU SETTLED (rare, vd late parcel rejection): ledger entry vẫn được INSERT
     cho audit. Admin manual handle wallet ADJUSTMENT (DEBIT) — xem mục Admin endpoints.

4. Trip terminal — settlement marker create:
   → Trip-Route-Vehicle publish `trip.trip.completed` HOẶC `trip.trip.disrupted`
   → Payment Service consume:
       IF SUM(operator_ledger_entries.amount WHERE operator_id=X AND trip_id=Y) <= 0:
         → SKIP — không tạo settlement marker (không có doanh thu net)
       ELSE:
         INSERT OperatorTripSettlement {
           operatorId, tripId,
           tripTerminalAt = Trip.completedAt or Trip.disruptedAt,
           eligibleAt = tripTerminalAt + interval '7 days',
           status = PENDING_HOLD,
           netAmount = compute_now (snapshot — sẽ recompute khi settle để pickup late refunds)
         }

5. Hangfire `trip-settlement-eligibility-flag` job (daily 02:00, Payment Service):
   SELECT OperatorTripSettlement WHERE status = PENDING_HOLD AND eligibleAt <= now
   → UPDATE status = ELIGIBLE  (atomic, optimistic lock theo rowVersion)
   → Optional push notification OPERATOR_ADMIN: "Chuyến [X] đã sẵn sàng tất toán."

6. Hangfire `trip-settlement-weekly-settle` job (Monday 09:00, Payment Service):
   SELECT OperatorTripSettlement WHERE status = ELIGIBLE
   FOR EACH:
     BEGIN TRANSACTION:
       1. Recompute netAmount = SUM(ledger entries cho trip này)
       2. IF netAmount <= 0:
           → UPDATE settlement SET status = CANCELLED, settledAt = now,
                                   settlementMethod = AUTO_WEEKLY
           → SKIP wallet credit
       3. ELSE:
           → UPDATE PlatformWallet SET balance = balance - netAmount,
                                        rowVersion = rowVersion + 1
             WHERE id = singleton_id
               AND balance >= netAmount
               AND rowVersion = :platformOriginalRowVersion
             (0 rows vì balance thiếu → abort transaction, log `PLATFORM_WALLET_INSUFFICIENT_BALANCE`,
              alert System Admin; KHÔNG mark settlement SETTLED)
           → INSERT PlatformWalletTransaction { type=DEBIT, amount=netAmount,
               balanceBefore, balanceAfter, referenceType=TRIP_SETTLEMENT,
               referenceId = settlementId, note = "Transfer to OperatorWallet for Trip [tripId]" }
           → UPDATE OperatorWallet SET balance = balance + netAmount,
                                        rowVersion = rowVersion + 1
             WHERE operatorId = X AND rowVersion = :originalRowVersion
             (optimistic lock fail → retry transaction)
           → INSERT OperatorWalletTransaction { type=CREDIT, amount=netAmount,
               balanceBefore, balanceAfter, referenceType=TRIP_SETTLEMENT,
               referenceId = settlementId, note = "Auto weekly settle Trip [tripId]" }
           → UPDATE settlement SET netAmount = recomputed, status = SETTLED,
                                   settledAt = now, settlementMethod = AUTO_WEEKLY,
                                   walletTransactionId = wallet_txn.id,
                                   rowVersion = rowVersion + 1
            WHERE id = X AND status = ELIGIBLE AND rowVersion = :originalRowVersion
          → Publish event `payment.trip_settlement.completed { tripId, operatorId, amount }`
     COMMIT
      → Notification Service consume: push OPERATOR_ADMIN
        "Đã tất toán [amount] VND từ chuyến [tripId] vào ví."

7. System Admin manual trigger (per trip, bất cứ lúc nào):
   POST /v1/admin/trip-settlements/{settlementId}/settle
     Role: SYSTEM_ADMIN
     Precondition: status IN (PENDING_HOLD, ELIGIBLE) — admin có thể settle SỚM trước eligibleAt
       (override 7-day hold cho case operator yêu cầu rút sớm + admin approve)
     Logic: giống bước 6 nhưng settlementMethod = ADMIN_MANUAL, settledByUserId = adminId
     Errors:
       404 TRIP_SETTLEMENT_NOT_FOUND
       409 TRIP_SETTLEMENT_ALREADY_SETTLED (status = SETTLED hoặc CANCELLED)
     Audit log: { action: TRIP_SETTLEMENT_MANUAL, userId, metadata: { settlementId, tripId } }
```

**Status transition lock cho `OperatorTripSettlement`:** Mọi UPDATE status PHẢI dùng pattern `WHERE id = :id AND status = :expectedCurrentStatus AND rowVersion = :originalRowVersion` để tránh 2 caller concurrent (Monday auto-job + admin manual cùng lúc). 0 rows updated → return `TRIP_SETTLEMENT_ALREADY_SETTLED`.

**Operator dashboard — module "Doanh thu & Ví":**

Sidebar mới "Doanh thu & Ví" trên Operator Web hiển thị:
- **Số dư ví hiện tại** (`OperatorWallet.balance`) — tiền operator đã nhận, sẵn sàng dùng / chờ withdrawal v2.
- **Pending revenue** (= SUM `OperatorTripSettlement.netAmount` WHERE `status IN (PENDING_HOLD, ELIGIBLE)`) — tổng tiền đang chờ. Hiển thị breakdown 2 row:
  - "Đang giữ (chưa đủ 7 ngày)" — sum status=PENDING_HOLD, kèm "ngày sớm nhất sẵn sàng tất toán: [min eligibleAt]"
  - "Sẵn sàng tất toán thứ Hai tới" — sum status=ELIGIBLE
- **Lịch sử ví:** list `OperatorWalletTransaction` (export Excel).
- **Lịch sử chuyến đã tất toán:** list `OperatorTripSettlement` status SETTLED kèm tripId, amount, settledAt, method.
- **Sổ cái chi tiết (audit):** filterable list `OperatorLedgerEntry` per period (export Excel).

**Operator endpoints:**
```
GET /v1/operator/wallet                              → { balance, updatedAt }
GET /v1/operator/wallet/transactions?from=&to=       → list OperatorWalletTransaction
GET /v1/operator/trip-settlements
    ?status=PENDING_HOLD|ELIGIBLE|SETTLED|CANCELLED
    &from=&to=                                       → list with nextEligibleAt summary
GET /v1/operator/ledger?from=&to=                    → list OperatorLedgerEntry (audit)
Role: OPERATOR_STAFF/ADMIN, tenant filter operator_id từ Internal JWT.
```

**System Admin endpoints:**
```
GET /v1/admin/trip-settlements
    ?operatorId=&status=&from=&to=
    → list across all operators

GET /v1/admin/platform-wallet
    → { balance, updatedAt }

GET /v1/admin/platform-wallet/transactions?from=&to=&referenceType=
    → list PlatformWalletTransaction for reconciliation

POST /v1/admin/platform-wallet/adjust
    Body: { type: CREDIT|DEBIT, amount: BIGINT, note: text required }
    Role: SYSTEM_ADMIN
    Use case: reconciliation correction, initial seed, production support
    Logic: Validate amount > 0; note required; DEBIT requires PlatformWallet.balance >= amount.
    Audit log: { action: PLATFORM_WALLET_ADJUSTMENT, userId, metadata: { type, amount, note } }

POST /v1/admin/trip-settlements/{settlementId}/settle
    Body: optional { note: text }
    Role: SYSTEM_ADMIN
    Logic: as described in flow step 7 above
    Response 200: { settlementId, status: SETTLED, netAmount, walletTransactionId }
    Errors: 404 TRIP_SETTLEMENT_NOT_FOUND, 409 TRIP_SETTLEMENT_ALREADY_SETTLED

POST /v1/admin/operators/{operatorId}/wallet/adjust
    Body: { type: CREDIT|DEBIT, amount: BIGINT, note: text required }
    Role: SYSTEM_ADMIN
    Use case: manual correction (post-settlement late refund debit; bonus credit; etc.)
    Logic:
      Validate amount > 0
      Validate balance >= amount nếu DEBIT (else return WALLET_INSUFFICIENT_BALANCE)
      Atomic: INSERT OperatorWalletTransaction { type, amount, referenceType=ADJUSTMENT,
              referenceId = adjustment_id (newly generated), note }
              + UPDATE OperatorWallet (optimistic lock)
    Audit log: { action: OPERATOR_WALLET_ADJUSTMENT, userId, metadata: { operatorId, type, amount, note } }
```

**v1 scope:**
- PlatformWallet clearing/holding pool cho booking/parcel payment, refund, subscription revenue và settlement transfer.
- Settlement vào ví nội bộ trên platform — KHÔNG có bank transfer ra ngoài.
- Hold 7 ngày sau Trip terminal (COMPLETED/DISRUPTED) trước khi eligible.
- Auto-settle Monday 09:00 weekly cho mọi settlement ELIGIBLE.
- Admin manual settle per-trip bất cứ lúc nào (kể cả override 7-day hold).
- Bank account fields `Operator.bankAccountName/Number/bankName` vẫn nullable trên schema — KHÔNG yêu cầu nhập, KHÔNG có UI banner trong v1. Sẽ enable trong v2 (Bank Withdrawal).
- v2 sẽ implement: (1) Bank Withdrawal — operator request rút balance về tài khoản ngân hàng; (2) Reconciliation UI; (3) Banking API integration (VietQR/VNPay PayOuts/etc.).

**Refund flow vs operator wallet:**
- **Passenger refund về Wallet** → VietRide credit Wallet passenger ngay và DEBIT PlatformWallet holding pool tương ứng.
- **Refund xảy ra trước Trip terminal** (operator cancel trip / passenger cancel với refund) → KHÔNG ảnh hưởng OperatorWallet vì wallet chưa từng được credit cho chuyến đó. Refund đi từ PlatformWallet. `OperatorLedgerEntry { entryType=BOOKING_REFUND/PARCEL_REFUND, amount=-x, tripId=trip.id }` được INSERT cho audit; sẽ làm giảm netAmount nếu trip này về sau vẫn terminate COMPLETED/DISRUPTED.
- **Refund xảy ra giữa eligibleAt và settled** (hiếm; sub-week window) → ledger entry tính vào SUM tại settle time → wallet được credit ít hơn.
- **Refund xảy ra sau SETTLED** (very rare; vd parcel DELIVERY_REJECTED tuần sau khi trip xong) → INSERT ledger entry cho audit + Admin thủ công gọi `POST /v1/admin/operators/{operatorId}/wallet/adjust { type: DEBIT, amount: refundAmount, note: "Late refund for parcel X" }` để debit wallet operator. Nếu balance không đủ → admin coordinate offline với operator. PlatformWalletTransaction adjustment/refund cũng phải được ghi để reconciliation không lệch.
- **User top-up Wallet passenger** → tiền vào PassengerWallet trước. PlatformWallet chỉ CREDIT khi PassengerWallet được dùng để thanh toán booking/parcel; KHÔNG ảnh hưởng OperatorWallet.

> **Lưu ý vận hành:** Trong v1, VietRide chịu trách nhiệm legal/regulatory với passenger (e-invoice vé xe vẫn do operator xuất ngoài hệ thống). VietRide chỉ là payment facilitator. Cần consult lawyer cho production — capstone scope OK.

**Error codes mới (Section 4.6):**
- `TRIP_SETTLEMENT_NOT_FOUND` — settlement id không tồn tại
- `TRIP_SETTLEMENT_ALREADY_SETTLED` — settlement đã SETTLED hoặc CANCELLED (admin manual retry)
- `WALLET_INSUFFICIENT_BALANCE` — admin DEBIT adjustment khi balance < amount (re-use error code wallet hiện có)
- `PLATFORM_WALLET_INSUFFICIENT_BALANCE` — PlatformWallet không đủ balance để refund/settle; alert System Admin và không mark settlement SETTLED

---

## 5. Authentication & Authorization

### 5.1 Phương thức đăng nhập

| Actor | Phương thức |
|---|---|
| Hành khách | Google OAuth 2.0, Email + Password; Email OTP verify bắt buộc khi đăng ký lần đầu. **Phone REQUIRED + UNIQUE** ở registration — dùng cho contact, NO_SHOW alert, sau này SMS notify v2. Phone format E.164 Việt Nam (vd `+84901234567`). Khi self-register email/password: bắt buộc nhập đủ `{ email, password, displayName, phone }`. Google OAuth callback chưa có phone → first-login redirect màn "Hoàn tất hồ sơ" yêu cầu nhập phone trước khi vào app. |
| Tài xế / Phụ xe | Email + Password — tài khoản do Operator tạo, set password lần đầu qua **email link** (`SET_INITIAL_PASSWORD` token TTL 48h). Operator nhập phone khi tạo account (bắt buộc — operator gọi liên hệ). Xem 4.3 |
| Operator Staff / Admin | Email + Password. **OPERATOR_ADMIN:** tạo qua self-registration (`POST /v1/operators/register`) hoặc System Admin tạo thủ công. **OPERATOR_STAFF:** do OPERATOR_ADMIN tạo trong dashboard, set initial password qua email link giống Driver/Assistant. Phone required (bắt buộc cho contact operator). |
| System Admin | Email + Password — **bootstrap admin đầu tiên do Identity Service startup seeder tạo từ env vars** (xem 5.1.1). Phone optional. |

> **Phone uniqueness scope:** `User.phone` UNIQUE **across all roles + all operators** (1 SĐT chỉ thuộc 1 User account duy nhất trong hệ thống). Lý do: tránh confusion khi notify (1 SĐT nhận thông báo từ 2 account khác nhau), ngăn user lách rate limit OTP bằng cách tạo nhiều account cùng SĐT. Edge case: nếu user đổi sim hoặc bị mất SĐT → System Admin có endpoint manual update (audit log).

> **Complete-profile flow cho Google OAuth:**
>
> Google OAuth callback chỉ trả về `email + googleSub + displayName + avatarUrl` — KHÔNG có phone. User tạo từ Google OAuth ban đầu `phone = NULL` nhưng status=ACTIVE (email đã verified bởi Google).
>
> **Middleware enforcement (Gateway level):**
> Mọi request từ User có `phone IS NULL` + role=PASSENGER → Gateway block với HTTP 403:
> ```
> {
>   errorCode: "AUTH_PHONE_REQUIRED",
>   detail: "Vui lòng hoàn tất hồ sơ trước khi tiếp tục."
> }
> ```
> **Whitelist endpoints không bị block** (cho phép user complete profile):
> - `GET /v1/users/me` (xem profile hiện tại)
> - `POST /v1/users/me/complete-profile` (submit phone)
> - `POST /v1/auth/logout`
> - `POST /v1/auth/refresh`
> - Health/static endpoints
>
> **Endpoint spec:**
> ```
> POST /v1/users/me/complete-profile
> Auth: User Access Token (RS256)
> Body: { phone: string (E.164 format, e.g. "+84901234567") }
>
> Validation:
>   - phone format E.164 Việt Nam (regex `^\+84[0-9]{9,10}$`)
>   - phone UNIQUE check across User table → conflict → return AUTH_PHONE_ALREADY_REGISTERED
>   - User.phone phải đang IS NULL (chỉ cho complete 1 lần qua endpoint này) → đã set → return VALIDATION_ERROR
>
> Response 200: { userId, phone, message: "Hồ sơ hoàn tất." }
>   → Identity Service UPDATE User SET phone = :phone
>   → Audit log: { action: COMPLETE_PROFILE, userId }
>
> Errors:
>   400 AUTH_PHONE_INVALID_FORMAT
>   409 AUTH_PHONE_ALREADY_REGISTERED
>   422 VALIDATION_ERROR (phone đã set, không thể overwrite qua endpoint này — phải dùng `PATCH /v1/users/me` với verify password)
> ```
>
> **Client behavior (mobile app):**
> - Sau Google OAuth success → app gọi `GET /v1/users/me` → nếu `phone IS NULL` → redirect screen "Hoàn tất hồ sơ" force user nhập phone trước khi vào main app.
> - Mọi API call khác trả 403 `AUTH_PHONE_REQUIRED` → app tự redirect về complete-profile screen.
>
> **Error code mới:** `AUTH_PHONE_INVALID_FORMAT`.

### 5.1.1 System Admin Bootstrap

System Admin đầu tiên được tạo bởi **Identity Service startup seeder** khi app khởi động lần đầu, không có endpoint registration cho SYSTEM_ADMIN role và không seed placeholder password trong EF migration/`seed.sql`.

```
Startup seeder (Identity Service):
  - Đọc env vars: SYSTEM_ADMIN_BOOTSTRAP_EMAIL, SYSTEM_ADMIN_BOOTSTRAP_PASSWORD, SYSTEM_ADMIN_BOOTSTRAP_DISPLAY_NAME (optional)
  - Nếu chưa có User nào với role = SYSTEM_ADMIN trong DB:
      INSERT User {
        email: env.SYSTEM_ADMIN_BOOTSTRAP_EMAIL,
        passwordHash: bcrypt(env.SYSTEM_ADMIN_BOOTSTRAP_PASSWORD, cost=12),
        role: SYSTEM_ADMIN,
        status: ACTIVE,
        displayName: env.SYSTEM_ADMIN_BOOTSTRAP_DISPLAY_NAME ?? "System Administrator"
      }
  - Idempotent: chạy nhiều lần không tạo duplicate (check role=SYSTEM_ADMIN exists).
  - Sau khi deploy thành công, bootstrap admin được khuyến nghị đổi password lần đầu login (UI hint, không enforce hardcode).

Subsequent System Admin (thêm admin thứ 2+):
  - Endpoint: POST /v1/admin/users { email, displayName, role: SYSTEM_ADMIN } (role SYSTEM_ADMIN required)
  - Identity Service tạo user passwordless với status=PENDING_INITIAL_PASSWORD
  - SET_INITIAL_PASSWORD token generation + email send defer sang Day 5
```

> **Security note:** `SYSTEM_ADMIN_BOOTSTRAP_PASSWORD` env var chỉ dùng cho deployment đầu tiên — sau khi admin đăng nhập và đổi password, env var này không còn ảnh hưởng (vì startup seeder check existence trước). Production cần rotate password mặc định ngay sau deploy.

### 5.2 Token Strategy (user-facing)

- **Access Token:** JWT ngắn hạn (15 phút), payload chứa `userId`, `role`, `operatorId` (nullable). **Algorithm: RS256** — Identity Service ký bằng private key; các service khác verify bằng public key từ JWKS endpoint.
- **Refresh Token:** opaque token, lưu DB, 30 ngày, rotate mỗi lần dùng (cũ bị revoke)
- API Gateway validate Access Token trước khi forward đến downstream service

**JWKS endpoint (Identity Service expose):**
```
GET /v1/.well-known/jwks.json  (public, no auth required)
Response: { keys: [{ kty, alg, use, kid, n, e }] }  // JWK Set format (RFC 7517)
```
- Services (Gateway, Tracking) cache JWKS tại startup + refresh mỗi 1h (hoặc khi gặp unknown `kid`)
- **`USER_JWT_PUBLIC_KEY` env var chỉ dùng cho local dev / unit test** — production phải fetch JWKS endpoint
- Private key lưu trong env var `USER_JWT_PRIVATE_KEY` (PEM), chỉ Identity Service có

**JWKS key rotation decision (v1):**
- Mỗi signing key có `kid` unique trong JWT header. Identity Service có thể publish nhiều public keys trong JWKS cùng lúc.
- **Normal rotation:** add key mới vào JWKS, bắt đầu ký token mới bằng key mới, giữ key cũ trong JWKS tối thiểu `accessTokenTtl 15 phút + JWKS cache 1 giờ` (≈75 phút) rồi mới remove để token cũ hết hạn an toàn.
- **Emergency compromised key:** rotate sang key mới, remove key cũ khỏi JWKS, force Gateway/Tracking refresh JWKS cache qua redeploy/restart. v1 **không implement access-token blacklist**; chấp nhận các access token đã phát hành còn valid tối đa 15 phút sau khi services đã refresh JWKS. RefreshToken có thể revoke theo user/family/global qua DB.

> **Tại sao RS256 thay HS256?** Với HS256 (HMAC), tất cả services phải share cùng secret — 1 service bị compromise thì tất cả bị forge token. RS256 chỉ Identity Service có private key; các service khác chỉ verify, không thể ký token mới. Principle of least privilege.

### 5.3 RBAC Roles

| Role | Scope |
|---|---|
| `PASSENGER` | Passenger App |
| `DRIVER` | Driver App |
| `ASSISTANT` | Driver App — quyền bổ sung về cargo |
| `OPERATOR_STAFF` | Operator Web — dữ liệu trong phạm vi công ty mình (`operatorId`) |
| `OPERATOR_ADMIN` | Operator Web — toàn quyền công ty + quản lý nhân sự |
| `SYSTEM_ADMIN` | Admin Web — toàn bộ hệ thống |

### 5.3.1 Account & Operator Status Enums

**User.status (canonical enum):**
```
PENDING_EMAIL_VERIFICATION | ACTIVE | LOCKED | DELETED
```

- `PENDING_EMAIL_VERIFICATION`: user đăng ký bằng email/password nhưng chưa verify OTP. `PASSENGER` được login lấy access token cho mobile restricted session; FE khóa chức năng dựa trên `data.user.status`. Non-passenger pending email không được login và chỉ được request/verify OTP. Sau verify thành công → `ACTIVE`.
- `ACTIVE`: account dùng bình thường. Google OAuth account tạo mới vào thẳng `ACTIVE` vì Google đã verify email ownership.
- `LOCKED`: bị khóa do password lockout hoặc System Admin khóa thủ công. Không login, không refresh token, không request password reset. `lockedFromStatus` bắt buộc lưu origin `ACTIVE` hoặc `PENDING_EMAIL_VERIFICATION`; manual admin lock chỉ nhận `ACTIVE`, password lockout có thể nhận cả hai. Unlock khôi phục đúng origin và không tự verify email.
- `DELETED`: soft delete/anonymized account. Terminal cho v1; không login/reactivate trong app flow bình thường.

**Operator.registrationStatus (canonical enum):**
```
PENDING | APPROVED | REJECTED | SUSPENDED
```

- `PENDING`: operator mới đăng ký/chờ System Admin duyệt.
- `APPROVED`: operator được phép vận hành, tạo route/trip, quản lý nhân sự.
- `REJECTED`: hồ sơ bị từ chối. Terminal cho request hiện tại; muốn đăng ký lại thì tạo request/operator record mới hoặc admin reset thủ công.
- `SUSPENDED`: operator đã từng `APPROVED` nhưng bị System Admin khóa tạm thời. Không tạo/sửa trip mới; Operator Web bị chặn write actions. Không dùng `LOCKED` cho Operator để tránh nhầm với `User.status = LOCKED`.

### 5.4 Service-to-service Authentication — Internal JWT

Khi các service nội bộ gọi nhau (Booking → Trip-Route-Vehicle, Booking → Payment & Wallet, etc.), cần verify caller là service đáng tin cậy và lấy được context user gốc.

**Pattern: Gateway issues Internal JWT**

```
1. Client request:
   POST /bookings  Authorization: Bearer <userAccessToken>

2. Gateway validate userAccessToken (verify signature, check expiry)
   → Lấy userId, role, operatorId từ payload

3. Gateway sinh Internal JWT mới:
      payload {
        sub: userId
        role: PASSENGER | DRIVER | ...
        operatorId: nullable
        callerService: "gateway"
        iat, exp (TTL: 120 giây — đủ tolerance cho Polly retry exponential backoff)
      }
      ký bằng INTERNAL_JWT_SECRET (shared secret, env var)

4. Gateway forward đến Booking Service:
   POST /bookings  X-Internal-Auth: Bearer <internalJWT>

5. Booking Service middleware verify Internal JWT:
   - Verify signature với INTERNAL_JWT_SECRET
   - Check exp (request phải đến trong 120 giây từ gateway)
   - Extract user context → đưa vào HttpContext / RequestScope

6. Khi Booking gọi sang Trip-Route-Vehicle:
   POST /trips/{id}/lock-seats  X-Internal-Auth: Bearer <internalJWT mới>
   (Booking sinh internal JWT mới với callerService = "booking", truyền context user gốc)
```

**Lợi ích:**
- Mỗi request có user context (userId, role, operatorId) truyền dọc các service — không cần fetch lại từ DB
- TTL 120 giây — chống replay attack (window đủ ngắn) đồng thời cho phép Polly retry với exponential backoff không bị token expired giữa chừng
- Service không trust HTTP header tùy ý — phải verify signature
- Một secret duy nhất `INTERNAL_JWT_SECRET` (lưu trong env var, **không** commit code)

> **Security trade-off v1:** Internal JWT dùng shared secret nghĩa là nếu một service nội bộ bị compromise và leak `INTERNAL_JWT_SECRET`, attacker có thể forge internal token cho service khác. Đây là trade-off chấp nhận được cho capstone/v1 để giảm vận hành keypair per service. Production hardening v2: asymmetric service-to-service JWT hoặc mTLS + per-service audience/key.

**Triển khai cụ thể:**
- NestJS: custom Guard verify Internal JWT, inject vào `@CurrentUser` decorator
- .NET Core: AuthenticationHandler riêng, inject vào `ClaimsPrincipal` qua `HttpContext.User`
- Gateway dùng thư viện `jose` (NestJS) hoặc `Microsoft.IdentityModel.Tokens` để sign

**Lưu ý:**
- External callback (VNPay redirect, Firebase webhook) **không** có Internal JWT — handle bằng signature verification riêng (VNPay HMAC-SHA512, Firebase signed token)
- Health check endpoints (`/health`, `/ready`) **không** require Internal JWT

### 5.5 Socket.IO Client Authentication (Tracking Service)

Internal JWT (section 5.4) áp dụng cho service-to-service HTTP call. Tracking Service nhận kết nối **trực tiếp từ Driver App và Passenger App** qua Socket.IO — đây là **client-to-service**, dùng User Access Token, không phải Internal JWT.

```
Socket.IO handshake flow:

  Client kết nối:
    io("wss://api.vietride.app", {
      path: "/tracking/socket.io",                 // Nginx proxy_pass → Tracking Service
      auth: { token: "<userAccessToken>" }         // Access token 15 phút từ Identity Service
    })
    // v1: cùng domain với REST API, route qua Nginx.
    // v2: có thể tách domain riêng `wss://tracking.vietride.app` nếu cần scale độc lập
    //     hoặc tách SSL cert. Khi đó client chỉ đổi URL, auth flow giữ nguyên.

  Tracking Service middleware (chạy trước mọi event handler):
    1. Extract token: socket.handshake.auth.token
    2. Verify RS256 bằng public key từ JWKS cache
       (local dev: có thể dùng USER_JWT_PUBLIC_KEY env var thay JWKS fetch)
    3. Extract { userId, role, operatorId } → attach vào socket.data
    4. Nếu token invalid/expired → disconnect với error "UNAUTHORIZED"

  Room assignment sau auth thành công:
    role = DRIVER:
      socket.join(`driver:${driverId}`)       // room nhận dispatch từ operator
    role = OPERATOR_STAFF / OPERATOR_ADMIN:
      socket.join(`operator:${operatorId}`)   // nhận tất cả alert của operator

  Trip-specific room assignment:
    Client KHÔNG auto-join `trip:{tripId}` chỉ dựa trên role.
    Khi mở màn tracking, client phải emit `joinTripTracking` với tripId.
```

**Socket event contract — `joinTripTracking`:**

```
Client → Server:
  socket.emit("joinTripTracking", { tripId: "<uuid>" }, (ack) => { ... })

Server authorization:
  Tracking Service lấy user context từ socket.data (đã verify JWT ở middleware).
  Tracking Service verify quyền qua HTTP internal:
    - Booking Service: user là booking owner có quyền xem trip
    - Trip-Route-Vehicle Service: user là driver/assistant/operator của trip
    - Parcel Service: user là sender hoặc recipient của parcel trên trip đó

Success ack:
  {
    success: true,
    tripId: "<uuid>",
    room: "trip:<tripId>",
    scope?: "BOOKING_OWNER" | "DRIVER" | "ASSISTANT" | "OPERATOR" |
           "PARCEL_SENDER" | "PARCEL_RECIPIENT"
  }

Error ack:
  {
    success: false,
    error: "UNAUTHORIZED" | "VALIDATION_ERROR" | "TRIP_NOT_FOUND" | "ACCESS_DENIED",
    message?: "<human-readable short message>"
  }

Nếu success → socket.join(`trip:${tripId}`).
Room `trip:{tripId}` nhận GPS update + ETA update giống passenger tracking.
Không broadcast thông tin hành khách/parcel khác; client chỉ render dữ liệu đúng scope.
```

**Client biết `tripId` từ đâu:**

```
Passenger booking owner:
  → Booking detail/history trả về tripId.

Parcel sender:
  → Parcel create/detail/list của người gửi trả về tripId.

Parcel recipient có account VietRide:
  → Passenger App gọi GET /v1/parcels/received
  → Response mỗi parcel PHẢI có { parcelId, parcelCode, tripId, status, originStation, destinationStation, eta? }
  → Khi user mở tracking detail, app connect Socket.IO rồi emit joinTripTracking { tripId }.
  → Nếu recipientUserId = null (người nhận không có account) thì KHÔNG có in-app realtime tracking; chỉ nhận email delivery link.
```

> **Key difference:** User Access Token (15 phút, RS256, verify bằng JWKS public key) — KHÔNG phải Internal JWT (120 giây, HS256, verify bằng INTERNAL_JWT_SECRET). Driver và Passenger connect trực tiếp, không qua Gateway khi dùng WebSocket.

> **Socket.IO token expiry — reconnect strategy (Option A — client-side reconnect):**
> Access Token 15 phút có thể expire trong khi Socket.IO connection đang active (long-lived session theo dõi GPS). **Tracking Service chỉ verify JWT tại thời điểm handshake** — không verify lại mid-session. Khi token expire, Tracking Service KHÔNG disconnect client chủ động.
>
> **Client responsibility:**
> - Mobile client dùng refresh token flow để lấy access token mới trước khi token cũ expire (proactive refresh ~1 phút trước TTL).
> - Nếu connection bị drop (network lỗi, app background, token expire và server restart): client reconnect với access token mới (đã refresh trước đó hoặc refresh ngay lúc reconnect).
> - `socket.io-client` có built-in reconnect với exponential backoff — client chỉ cần set `auth: { token: newAccessToken }` trước khi reconnect.
> - Tracking Service socket middleware verify token fresh tại mỗi handshake (kể cả reconnect).
>
> **Acceptable trade-off:** User có thể thấy tracking "giật" 1–2 giây khi reconnect. Không ảnh hưởng đến GPS accuracy hay booking data. Đơn giản hơn Option B (mid-session reauth event) đáng kể.

---

## 6. Core Business Flows

> Mô tả nghiệp vụ để agent thiết kế API, DB schema, frontend. Không đi sâu implementation vì có thể thay đổi khi thiết kế chi tiết.

### 6.1 Đặt vé (Booking)

Hành khách tìm kiếm chuyến theo điểm đi, điểm đến và ngày. Hệ thống trả về danh sách chuyến còn ghế trống. Hành khách chọn chuyến, chọn pickup stop, chọn dropoff stop (nếu chuyến cho phép), chọn ghế trên sơ đồ, áp voucher nếu có, chọn phương thức thanh toán và xác nhận. **Booking chỉ lưu thông tin người mua (booking owner)** — không nhập nhân thân từng người ngồi xe. Sau khi thanh toán thành công, người mua nhận vé điện tử kèm mã QR.

**QR code spec — vé điện tử:**
```
QR encode: plain string = bookingCode
  → `bookingCode` format: VR-yyyyMMdd-XXXXXXXX
     - VR = VietRide Booking prefix (phân biệt với VRP của Parcel)
     - yyyyMMdd = ngày tạo booking theo Asia/Bangkok (ICT)
     - XXXXXXXX = 8 ký tự uppercase base32 (A-Z, 2-7) random, unique trong DB
     - Ví dụ: "VR-20260518-ABCDEF34"
  → KHÔNG encode JSON, token, hay encrypted payload
  → Booking Service generate `bookingCode` khi tạo Booking (PENDING_PAYMENT); client KHÔNG tự gửi
  → Booking Service lưu `bookingCode` string unique + indexed
  → Passenger App/email render QR động từ bookingCode, KHÔNG lưu ảnh QR trong DB
  → Driver App scan QR → decode ra bookingCode (regex `^VR-\d{8}-[A-Z2-7]{8}$`)
  → Driver App match bookingCode với danh sách booking của trip đang chạy
  → Per-passenger boarding: scan 1 QR booking → app hiển thị N ghế trong booking,
     driver/assistant tick từng Passenger (BOARDED) tương ứng với từng người lên xe

Nếu scan ra code không thuộc trip hiện tại hoặc booking không CONFIRMED:
  → không update status
  → trả lỗi `BOOKING_NOT_FOUND` hoặc `BOOKING_NOT_FOR_THIS_TRIP`

Lý do dùng plain bookingCode:
  - Đơn giản: bookingCode đã là unique identifier per booking
  - Dễ đọc khi support/manual lookup
  - Driver App có sẵn danh sách booking của trip qua API → match nhanh
  - Không cần lưu thêm field QR nào trong DB — generate động từ bookingCode
  - Nhất quán với parcel QR (VR-PCL-yyyyMMdd-XXXXXXXX) — cùng pattern, prefix khác để
    phân biệt loại
```

Hành khách có thể chỉnh sửa pickup/dropoff trong thời hạn cho phép trước giờ khởi hành — **cutoff cứng toàn hệ thống: 2 giờ trước departure time**.

**Edit pickup/dropoff scope (v1):**
- `POST /v1/bookings/{bookingId}/edit-pickup` và `POST /v1/bookings/{bookingId}/edit-dropoff` đều in-scope.
- Edit dropoff không đổi giá v1 (pricing full theo pickup point đến terminal đích).

**Edit pickup reprice — downgrade-only policy:**
```
Default (không có TripStopFare exception):
  Mọi pickup point trên tuyến cùng giá = Trip.baseFare
  → Edit pickup KHÔNG đổi giá, không refund, không charge

Có TripStopFare exception:
  Case được phép (giá mới ≤ giá cũ):
    delta = oldFare - newFare
    → Hoàn delta về Ví VietRide ngay lập tức nếu delta > 0
    → Publish BookingUpdated + WalletCredited event

  Case BỊ BLOCK (giá mới > giá cũ):
    → Return error BOOKING_EDIT_PICKUP_PRICE_INCREASE
    → Message: "Không thể đổi sang điểm đón có giá cao hơn.
                Vui lòng hủy vé và đặt lại nếu cần thay đổi."
```
> **Lý do không cho phép reprice lên:** Mở thêm VNPay redirect trong flow edit booking = UX phức tạp và dễ abandon. Charge thêm fail (ví không đủ, VNPay lỗi) → booking limbo. Consistent với industry practice (Traveloka, Vexere): refund simple, charge thêm trong edit avoid.

**Edit dropoff rules — `POST /v1/bookings/{bookingId}/edit-dropoff`:**
```
Preconditions:
  1. Booking.status = CONFIRMED
  2. Trip.departureDateTime - now > 2 giờ  (cùng cutoff 2h với edit-pickup, hardcode)

Các case hợp lệ (đổi 2 chiều):
  Terminal đích → Along-route stop (dropoffStopId = một Stop trên RouteStop):
    → UPDATE Booking.dropoffStationId = null, Booking.dropoffStopId = newStopId
    → Không tính lại fare, không refund, không charge thêm

  Along-route stop → Terminal đích (về mặc định):
    → UPDATE Booking.dropoffStationId = Trip.route.destinationStationId, Booking.dropoffStopId = null
    → Không tính lại fare

  Along-route stop A → Along-route stop B (khác):
    → UPDATE Booking.dropoffStopId = newStopId
    → Không tính lại fare

Validation thêm:
  - dropoffStop.orderIndex phải > pickupStop.orderIndex (không cho phép đặt dropoff trước pickup)
  - Stop phải thuộc RouteStop của chuyến đó (single source of truth)
  - Stop phải có `allowDropoff = true` — nếu false return `STOP_NOT_DROPOFF_ALLOWED`
  - Nếu Stop không thuộc RouteStop → return `STOP_NOT_FOUND`

Pricing note:
  v1 fare model là pickup-based (TripStopFare.fareFromThisStop = giá từ pickup đến terminal đích).
  Không có fare matrix pickup→dropoff. Segment pricing (giá khác nhau theo pickup→dropoff pair)
  là v2 feature — xem Feature flagged v2.
  → Edit dropoff KHÔNG đổi giá, KHÔNG refund delta, KHÔNG charge thêm.
```
> **Scoping note:** Section 6.2 spec rõ edit-pickup với downgrade-only pricing. Edit-dropoff có flow riêng biệt và đơn giản hơn — không có pricing logic vì v1 chưa có segment fare matrix.

**1 Booking = tối đa 5 ghế (tối đa 5 Passenger):**

Hành khách có thể đặt nhiều ghế trong 1 booking duy nhất cho gia đình/nhóm. Giới hạn cứng **5 ghế per booking** để ngăn hành vi đầu cơ/chiếm vé.

Booking là entity gốc — chứa thông tin **người mua duy nhất** (booking owner — userId hoặc contact info của buyer), trip, pickup/dropoff, tổng tiền, voucher áp dụng, status lifecycle. **Booking KHÔNG lưu thông tin nhân thân (họ tên, SĐT, CCCD) của những người ngồi xe khác buyer.** Passenger là sub-entity của Booking (1 booking có 1–5 Passenger record), chỉ lưu **operational fields**: `seatId/seatNumber`, `boardingStatus` (PENDING/BOARDED/NO_SHOW), `boardedAt`, `boardedAtStopId`, `bookingId`. KHÔNG có fullName/phoneNumber/idNumber. TripSeat là entity riêng track trạng thái ghế per chuyến (AVAILABLE/HELD/BOOKED), reference đến Booking và Passenger khi đã đặt. Business constraint: số Passenger per Booking ≤ 5 (enforce ở app layer + DB constraint).

**Lý do chỉ lưu buyer:** Trải nghiệm thực tế vận hành xe khách VN — phụ xe đối chiếu vé qua bookingCode/QR và đếm đầu người tại điểm đón, không cần định danh từng cá nhân. Giảm phức tạp UX (không bắt user nhập 5 lần thông tin), tránh lưu PII không cần thiết. Quy đổi pháp lý/CCCD nếu cần làm offline.

**Trip history scope — `GET /bookings/history` query theo người đặt:**

Trip history trong Passenger App hiển thị các booking mà user đó đã ĐẶT (làm chủ booking — dù đặt cho người khác hay cho chính mình). KHÔNG include các booking mà user đó là Passenger thực tế (người đi xe) nhưng người khác đặt cho.

Lý do: Passenger entity chỉ lưu operational data (seatNumber, boardingStatus), không lưu nhân thân và không liên kết tới User account khác buyer. Không thể lookup "booking nào có Passenger là tôi". Đây là design trade-off chấp nhận được cho v1 — đặt vé cho người khác vẫn thấy trong history của người đặt; người được đặt hộ không thấy (trừ khi họ tự vào app và đặt).

**Trip info cho history list — snapshot strategy:** `GET /bookings/history` trả về data từ Booking DB, **không cần HTTP call sang Trip-Route-Vehicle Service**. Booking entity có 4 snapshot fields (`tripSnapshotOriginName`, `tripSnapshotDestName`, `tripSnapshotDeparture`, `tripSnapshotRouteName`) được set khi tạo Booking — đủ để render history row (tên tuyến, giờ khởi hành, tên bến). Xem entity spec ở section 8. Lý do chọn snapshot thay vì (b) HTTP call mỗi lần: N HTTP call khi load list — không scale, latency cao; thay vì (c) client ghép 2 API: double round-trip, lộ service boundary.

**Passenger history facade — `GET /v1/passenger/history`:** FE chọn đúng một nhánh bằng query
`type=TICKET|PARCEL`; v1 không có `ALL` và không free-text search. Parcel Service sở hữu facade vì
dependency một chiều Parcel → Booking đã tồn tại. Nhánh `TICKET` chỉ gọi
`GET /internal/v1/bookings/history?userId=...`, phân trang theo Booking và trả Ticket summaries
bên trong Booking. Nhánh `PARCEL` chỉ query Parcel local với `senderUserId = JWT sub`; parcel mà
user chỉ là người nhận không nằm trong nhánh này. Cả hai dùng `createdAt DESC, id DESC`, range
`from` inclusive/`to` exclusive, `pageSize <= 100`. Booking upstream lỗi phải trả
`502 UPSTREAM_UNAVAILABLE`, không giả thành trang rỗng. Trip enrichment cho parcel được deduplicate
theo `tripId` và chạy bounded concurrency; Trip thiếu/lỗi chỉ làm journey fields null.

**Round-trip booking (Đặt vé khứ hồi):**

v1 scope đặt vé khứ hồi đầy đủ. UI 1 luồng checkout, backend 2 Booking độc lập.

**Structure:**
- UI: 1 luồng checkout, hiển thị như 1 chuyến 2 chiều cho hành khách.
- Backend: tạo **2 Booking hoàn toàn độc lập** với `tripDirection = OUTBOUND` / `RETURN`.
- 2 booking liên kết qua `bookingGroupId` (UUID) **CHỈ để display purposes** (hiển thị "đơn của bạn gồm 2 chiều"). Business logic của 2 booking độc lập hoàn toàn.
- Single booking (1 chiều) có `bookingGroupId = NULL`.

Cần entity hoặc field:
  - Booking thêm: `bookingGroupId` UUID nullable, `tripDirection` enum (OUTBOUND | RETURN) nullable
  - Không cần BookingGroup entity riêng — query group bằng bookingGroupId trên Booking table

**Hủy từng chiều — độc lập hoàn toàn:**
- Hủy 1 chiều **KHÔNG ảnh hưởng** chiều kia.
- Refund theo **cancellation policy của operator chuyến đó** (xem 6.2) — 2 chiều có thể thuộc 2 operator khác nhau, mỗi operator có policy riêng. Áp policy của operator tương ứng với booking bị hủy.
- 2 booking lifecycle độc lập (CONFIRMED, CANCELLED, COMPLETED, REFUNDED).

**Giá 2 chiều có thể khác nhau:**
- Đừng assume "khứ hồi = 2× 1 chiều". Mỗi chiều có giá riêng theo `Route.baseFare` của trip đó (có thể khác nhau do lễ, demand, operator khác nhau).

**NO_SHOW outbound không ảnh hưởng return booking:**
Nếu Passenger NO_SHOW trên Booking_outbound, Booking_return **vẫn giữ nguyên trạng thái CONFIRMED** — passenger có quyền đi chiều về. Hệ thống KHÔNG auto-cancel hoặc void chiều về khi chiều đi bị NO_SHOW. Lý do: 2 booking là entity hoàn toàn độc lập về lifecycle.
Agent KHÔNG implement auto-cascade NO_SHOW → cancel partner booking trong bookingGroupId.

**Voucher cho round-trip — áp per Booking riêng:**

Voucher áp **per Booking riêng**, KHÔNG áp lên cả BookingGroup:
- Mỗi Booking có `discountAmount` riêng, validate min order theo từng booking (không dùng tổng 2 chiều).
- Nếu user nhập voucher khi checkout round-trip:
  - Validate riêng cho từng chiều (outbound đủ minOrder → áp voucher cho outbound; tương tự return).
  - Tạo 2 VoucherUsage record riêng (1 per booking).
- Hủy 1 chiều: **KHÔNG ảnh hưởng voucher chiều kia.** DELETE VoucherUsage của booking bị hủy, voucher chiều còn lại giữ nguyên.

Lý do per-booking: phù hợp với mô hình 2 booking độc lập, đơn giản hóa refund logic, tránh complexity của split discount khi 1 chiều hủy.

**Payment cho round-trip:**

- **Wallet:** mỗi Booking có **Payment record riêng** (`referenceType = BOOKING`, mỗi booking deduct wallet 1 lần).
- **VNPay:** dùng `referenceType = BOOKING_GROUP` **chỉ khi** muốn checkout gộp 2 booking trong 1 VNPay redirect (cùng `vnp_TxnRef`, `vnp_Amount = outboundFare + returnFare`). Đây là tùy chọn UX để tránh user thanh toán 2 lần redirect. Nếu user chọn VNPay riêng từng chiều → 2 Payment record `referenceType = BOOKING`.

**Điều kiện tạo round-trip:**
  - Route phải có returnRouteId (Route.returnRouteId NOT NULL)
  - Return trip phải còn ghế (lock seats cho cả 2 trip trong cùng 1 Redis Lua script)
  - Departure của return trip phải sau arrival time ước tính của outbound trip
    (validate ở app layer: returnTrip.departureDateTime > outboundTrip.departureDateTime + routeDuration)

**Seat lock cho round-trip:**
  Lock 2 set ghế (outbound + return) atomically trong cùng 1 Lua script
  trước khi show payment screen. Nếu 1 trong 2 fail → rollback cả 2.

**Endpoint sketch:**
```
POST /v1/bookings/round-trip
Request: {
  outbound: {
    tripId, pickup: { stationId?, stopId? }, dropoff?: { stationId?, stopId? },
    seats: [{ seatNumber }]
  },
  return: {
    tripId, pickup: { stationId?, stopId? }, dropoff?: { stationId?, stopId? },
    seats: [{ seatNumber }]
  },
  voucherCode?,
  paymentMethod: "WALLET" | "VNPAY"
}
Response: {
  bookingGroupId,
  outbound: { bookingId, bookingCode, totalAmount, discountAmount },
  return:   { bookingId, bookingCode, totalAmount, discountAmount },
  grandTotal,
  paymentRedirectUrl? (chỉ VNPay)
}
```
Single-direction booking vẫn dùng `POST /v1/bookings` (không thay đổi).

> **Round-trip Payment:**
> - **Wallet:** mỗi Booking 1 Payment record riêng (`referenceType=BOOKING`). Deduct wallet 2 lần (1 per booking) trong cùng 1 transaction khi checkout. Booking Service confirm từng booking độc lập theo PaymentSucceeded events.
> - **VNPay gộp (recommended cho UX):** `referenceType=BOOKING_GROUP`, 1 redirect duy nhất với `vnp_Amount = outboundFare + returnFare`, IPN nhận → publish `PaymentSucceeded { referenceType=BOOKING_GROUP, bookingGroupId }` → Booking Service consume → CONFIRM cả 2 Booking. Lý do gộp: VNPay chỉ cho phép 1 `vnp_TxnRef` per transaction, user chỉ thấy 1 redirect.
> - **Refund khi hủy 1 chiều:** refund = fare chiều bị hủy × tỷ lệ hoàn theo **cancellation policy của operator chuyến đó** (không phải grandTotal, không phụ thuộc chiều kia). Payment Service tạo 1 Payment record mới `{ referenceType=BOOKING, referenceId=cancelledBookingId, method=WALLET, status=REFUNDED }`. Hủy 1 chiều KHÔNG ảnh hưởng Payment record của chiều kia.

**Round-trip VNPay BOOKING_GROUP — edge case partial expire:**

Cả 2 Booking_outbound + Booking_return được tạo trong cùng DB transaction với cùng `bookingGroupId`. Seat lock cho cả 2 trip atomic trong 1 Lua script (xem "Seat lock cho round-trip" phía trên). Payment record `referenceType=BOOKING_GROUP` được tạo với `referenceId=bookingGroupId`, `amount=outboundFare+returnFare`.

**Vấn đề:** Hangfire seat-release job chạy per-Booking khi Payment đã tới deadline mà chưa
`SUCCEEDED`. Trên lý thuyết 2 Booking cùng `bookingGroupId` dùng chung deadline sớm hơn của hai
seat lock, nên job sẽ release cả 2 cùng lúc. Tuy nhiên race condition vẫn có thể xảy ra (Hangfire
worker chạy không atomic giữa 2 booking).

**Rule chốt cho round-trip group:**
```
Hangfire job seat-release CHECK bookingGroupId trước khi release:
  SELECT Booking WHERE status = PENDING_PAYMENT
    AND effectivePaymentDueAt <= now
  FOR EACH:
    IF Booking.bookingGroupId IS NOT NULL:
      -- Group transaction: chỉ release nếu CẢ 2 booking trong group đều PENDING_PAYMENT timeout
      groupBookings = SELECT * FROM Booking WHERE bookingGroupId = :gid
      IF ALL groupBookings.status = PENDING_PAYMENT AND group payment deadline <= now:
        Release seats cho cả 2 + UPDATE both to EXPIRED
      ELSE:
        SKIP (case này không xảy ra vì cùng createdAt — nhưng defensive)
    ELSE:
      Release single Booking như flow thường

VNPay IPN handler (referenceType=BOOKING_GROUP):
  ON PaymentSucceeded:
    UPDATE Booking SET status=CONFIRMED WHERE bookingGroupId = :gid AND status=PENDING_PAYMENT
    IF ROW_COUNT < 2:
      -- Edge case: 1 booking đã EXPIRED (rất hiếm — chỉ xảy ra nếu Hangfire chạy ngay trước IPN)
      -- Sẽ có 1 booking CONFIRMED, 1 booking EXPIRED
      -- Compensation: 
      compensateAmount = fare của booking EXPIRED  (chiều bị mất seat)
      → Payment Service:
          INSERT Payment { referenceType=BOOKING_REFUND, referenceId=expiredBookingId,
                          amount=compensateAmount, method=WALLET, status=REFUNDED }
          INSERT WalletTransaction { type=CREDIT, referenceType=BOOKING_REFUND, ... }
          INSERT PlatformWalletTransaction { type=DEBIT, referenceType=BOOKING_REFUND,
                                             amount=compensateAmount,
                                             note="VNPay paid but 1 booking expired race" }
          INSERT OperatorLedgerEntry { entryType=BOOKING_REFUND, amount=-compensateAmount, 
                                       note="VNPay paid but 1 booking expired race" }
      → Notification Service push passenger:
          "Chiều [outbound/return] không thể giữ ghế. Đã hoàn [X] VND về ví. 
           Chiều còn lại đã xác nhận thành công."
      → Booking đã CONFIRMED của chiều còn lại — KHÔNG cancel (passenger vẫn dùng được)
```

Lý do compensation đầy đủ thay vì cancel cả 2: passenger đã trả tiền cho 1 chiều OK, không nên invalidate; refund đúng phần bị mất. Operator chịu phần refund qua ledger debit như flow thường.

**Pickup options:**

| Loại | Mô tả | Reference |
|---|---|---|
| **Tại bến (Terminal)** | Hành khách tự đến bến xe khởi hành đúng giờ | `pickupStationId` = `Route.originStationId`, `pickupStopId` = null |
| **Dọc tuyến (Along-route stop)** | Chọn từ danh sách Stop cố định đã được nhà xe định nghĩa sẵn trên tuyến (giao lộ, cây xăng, địa danh cụ thể). Xe ghé theo lịch | `pickupStopId` = một Stop trong `RouteStop` của Route |
| **Shuttle service (xe trung chuyển)** | Chỉ tại các Station chính lớn có `supportsShuttle = true`. Hệ thống bố trí xe trung chuyển gom passenger nội thành về bến chính (hoặc đưa từ bến về địa chỉ ở chiều ngược lại). Xem section 6.14. | passenger chọn shuttle khi pickup là Station có `supportsShuttle = true` và nhập địa chỉ shuttle origin |

**Dropoff options:**

| Loại | Mô tả | Reference |
|---|---|---|
| **Tại bến đích (Terminal)** — mặc định | Hành khách xuống tại bến cuối tuyến | `dropoffStationId` = `Route.destinationStationId`, `dropoffStopId` = null |
| **Dọc tuyến (Stop có `allowDropoff=true`)** | Hành khách xuống tại Stop dọc tuyến sau pickup, chỉ enable Stop có vai trò dropoff | `dropoffStopId` = một Stop trong RouteStop có `allowDropoff=true`, orderIndex > pickup stop orderIndex |

> Mỗi RouteStop entry có 2 flag `allowPickup` + `allowDropoff` (DB CHECK ít nhất 1 = true). Thực tế operator phân loại:
> - **Pickup-only** (gần đầu tuyến gom khách — ví dụ Hàng Xanh, An Sương trên tuyến SG→HN): `allowPickup=true, allowDropoff=false`
> - **Both** (giữa tuyến — NT, ĐN): cả 2 = true (default)
> - **Dropoff-only** (gần cuối tuyến trả khách dần — Phủ Lý, Pháp Vân): `allowPickup=false, allowDropoff=true`
>
> UI Operator Web hiển thị 2 checkbox khi add/edit RouteStop. Passenger App filter dropdown theo từng flag.

**Pricing rule cho dropoff (v1 — đơn giản):**
- Drop-off **miễn phí tại bất kỳ stop nào trên tuyến**. Khách xuống sớm hay xuống cuối tuyến giá vé như nhau.
- `dropoffStopId` chỉ lưu để phụ xe biết khách xuống đâu, không ảnh hưởng giá.
- Ghế bị lock toàn chuyến — xe vẫn chạy hết tuyến, đây là chuẩn vận hành thực tế xe khách VN.

> **v2 tiềm năng:** Segment pricing thật (fare = pickup→dropoff thay vì pickup→end). Cần thêm `TripSegmentFare` matrix hoặc tính động. Chưa scope cho v1.

**Along-route pricing — Giá theo chặng/tuyến/vùng giá (không tính theo km từ điểm đón phụ):**

Giá vé là **giá theo chặng/tuyến/vùng giá**, KHÔNG tính theo km từ điểm đón phụ. Ghế chiếm 1 slot trên toàn chuyến bất kể đón tại đâu.

**Default rule:** Giá vé khi đón giữa đường = **giá vé từ đầu tuyến** (giống như đón tại bến gốc). Operator chỉ phải set 1 mức giá per route — không cần khai báo giá riêng cho từng stop.

**Exception — `RouteStopFareTemplate`:** Nếu operator muốn cấu hình giá khác cho một số stop dọc tuyến (ví dụ stop ở khu vực ngoại thành, operator muốn ưu đãi thấp hơn), operator có thể tạo entry trong `RouteStopFareTemplate { routeId, stopId, fareFromThisStop }`. Đây là **exception, không phải default**. Stop không có entry trong template → dùng `Route.baseFare`.

Ví dụ tuyến **Sài Gòn → Hà Nội** có stop Nha Trang:
- Default: Khách đón Sài Gòn = Khách đón Nha Trang = **giá SG→HN** (ví dụ 400,000 VND)
- Nếu operator config RouteStopFareTemplate cho Nha Trang = 350,000 VND (exception) → khách đón Nha Trang trả 350k
- Ghế của khách Nha Trang bị **lock toàn bộ chuyến** (từ SG) — ghế SG→NT bỏ trống, operator chấp nhận đây là chi phí khi cho phép along-route pickup
- Entity `TripStopFare` — composite key (tripId + stopId), `fareFromThisStop` BIGINT VND, **chỉ tồn tại cho stop có exception**. Stop không có TripStopFare → Booking Service dùng `Trip.baseFare`.

**Operator kiểm soát stop nào passenger book được — qua `RouteStop` entry + 2 flag pickup/dropoff:**

KHÔNG có flag `allowAlongRoutePickup` / `allowAlongRouteDropoff` ở Trip. `RouteStop` entries là single source of truth với 2 flag per entry:

- **Operator add RouteStop entry cho Stop X** với `allowPickup`/`allowDropoff` → quyết định vai trò Stop X trên route đó:
  - `allowPickup=true, allowDropoff=true` → cả pickup và dropoff đều OK (giữa tuyến)
  - `allowPickup=true, allowDropoff=false` → chỉ pickup (gom khách đầu tuyến)
  - `allowPickup=false, allowDropoff=true` → chỉ dropoff (trả khách cuối tuyến)
  - DB CHECK `allowPickup OR allowDropoff` (không cho cả 2 = false)
- **Operator KHÔNG add RouteStop entry cho Stop X** → X không thuộc route → UI tự nhiên không hiển thị X cho passenger chọn

Ví dụ thực tế (tuyến SG→HN qua các stops dọc đường):
```
RouteStop entries của Route SG→HN:
  - Hàng Xanh:    allowPickup=true,  allowDropoff=false  (gom khách SG)
  - An Sương:     allowPickup=true,  allowDropoff=false  (gom khách SG)
  - Nha Trang:    allowPickup=true,  allowDropoff=true   (giữa tuyến)
  - Đà Nẵng:      allowPickup=true,  allowDropoff=true   (giữa tuyến)
  - Phủ Lý:       allowPickup=false, allowDropoff=true   (trả khách vào HN)
  - Pháp Vân:     allowPickup=false, allowDropoff=true   (trả khách vào HN)

Passenger chọn pickup → dropdown:
  [Bến SG (terminal), Hàng Xanh, An Sương, Nha Trang, Đà Nẵng]
  (Phủ Lý, Pháp Vân không hiển thị vì allowPickup=false)

Passenger chọn pickup = Nha Trang → dropoff dropdown (sau pickup):
  [Đà Nẵng, Phủ Lý, Pháp Vân, Bến HN (terminal)]
  (Hàng Xanh, An Sương không hiển thị vì allowDropoff=false; NT bị filter do orderIndex)
```

Chuyến "express" SG→HN không ghé giữa đường → Operator KHÔNG add bất kỳ RouteStop nào. Trip generate ra cũng không có TripStop. Passenger chỉ thấy `[Bến SG]` và `[Bến HN]` trong dropdown.

> **Edge case — operational stop (xe ghé chỉ để đổ xăng, không cho passenger book):**
> v1 workaround: **không** add stop đó vào RouteStop (xe vẫn ghé vì lý do internal nhưng business state không track).
> v2 nếu cần granular control: thêm `RouteStop.isPublicVisible boolean default true` — entry vẫn tồn tại để Tracking tính ETA cho ops nhưng UI ẩn.

**Trip edit khi đã có Booking CONFIRMED — Snapshot rule:**

Operator có thể edit Trip qua canonical `PATCH /v1/operator/trips/{tripId}` với body chỉ
`{baseFare?,notes?,vehicleId?,routeId?}`. Omitted nghĩa là giữ nguyên; `notes:null` xóa, trim và
blank normalize thành null; null cho field còn lại, empty/unknown-only body, departure và crew đều
không hợp lệ. Hệ thống dùng **snapshot pattern** — booking cũ giữ điều kiện kinh tế tại thời điểm
đặt vé, booking mới (sau edit) áp điều kiện mới. `baseFare|routeId` chỉ đổi ở `SCHEDULED`,
`vehicleId` ở `SCHEDULED|BOARDING`, và `notes` ở mọi trạng thái non-terminal. Same-value là no-op,
không audit/event/downstream call.

| Field edit | Behavior với booking CONFIRMED hiện có |
|---|---|
| `baseFare` | Booking cũ giữ giá cũ (`Booking.totalAmount` đã lock + có snapshot từ payment). Booking mới (sau edit) áp giá mới. Không refund delta. |
| `departureDateTime` | **Không phải Trip PATCH field.** Day 22 chỉ thay đổi field này qua DriverSchedule `ALL_PENDING` cascade, rồi trigger **SCHEDULE_CHANGE flow** (6.13). |
| `vehicleId` | `SCHEDULED\|BOARDING` dùng Vehicle Swap lightweight. Compatibility keyed theo normalized `seatNumber`, rank chỉ cho swap `STANDARD < SLEEPER_UPPER < SLEEPER_LOWER < VIP`; `DRIVER_AREA` không phải ghế passenger. Absent=`SEAT_REMOVED`; disabled/`DRIVER_AREA`=`SEAT_DISABLED`; lower rank=`SEAT_TYPE_DOWNGRADED`; equal/higher giữ HELD/BOOKED. `SCHEDULED`: incompatible HELD block; incompatible BOOKED chỉ tạo `PENDING_SEAT_ASSIGNMENT` khi `min(now+4h,departure-30m) > now`. `BOARDING`: mọi incompatible HELD/BOOKED quá muộn. `IN_PROGRESS`: dùng Vehicle Substitution 6.12. |
| `routeId` | Chỉ `SCHEDULED`; **BLOCK** với exact `TRIP_ROUTE_CHANGE_BOOKINGS_EXIST` nếu có Booking `PENDING_PAYMENT\|CONFIRMED`. Nếu không có active impact thì rebuild TripStop/TripStopFare/static planned ETA baseline atomically; GPS/Tracking không update baseline này. |
| `notes` | Nullable `VARCHAR(2000)`, cho phép ở mọi trạng thái non-terminal, không ảnh hưởng booking. |

> `Trip.allowAlongRoutePickup`/`allowAlongRouteDropoff` đã bị bỏ → không còn case "operator edit flag". Nếu operator muốn thay đổi stop nào passenger book được → edit `RouteStop` của route (trigger STOP_DISABLED flow 6.4.1 nếu cần disable stop đang có booking).

**Endpoint:** `PATCH /v1/operator/trips/{tripId}` với body chỉ các field được phép edit. Validate per-field theo bảng trên.

**Lý do snapshot:** giá vé đã commit khi passenger payment success → đổi giá ngược lại = không fair. Booking lưu `Booking.totalAmount` immutable sau CONFIRMED, đủ để tránh ambiguity. Operator vẫn flexible thay đổi cho chuyến tương lai mà không ảnh hưởng vé đã bán.

**Pricing clock + source (Day 22):** Booking capture `pricingAt` đúng một lần ở handler start và
gửi cùng timestamp vào raw `GET /internal/v1/trips/{tripId}?pricingAt=...` cho từng leg. Fare
precedence là persisted `TripStopFare.source=MANUAL_OVERRIDE` → active
`RouteStopFareTemplate` với half-open window `effectiveFrom <= pricingAt < effectiveUntil` (hoặc
open-ended) → `Trip.baseFare`. Khi không có `pricingAt`, internal snapshot giữ legacy operational
semantics: persisted `MANUAL_OVERRIDE|TEMPLATE_SNAPSHOT`, fallback `Trip.baseFare`, không query
template hiện hành. Day 22 không tạo `TEMPLATE_SNAPSHOT` mới; legacy rows chỉ còn để đọc ở path
không có `pricingAt` và không có quyền ưu tiên khi `pricingAt` explicit. Chỉ explicit operator
per-Trip fare override mới tạo `MANUAL_OVERRIDE`. `PaymentSucceeded` không re-query/reprice.
Booking giữ immutable `baseFare`, `discountAmount`, `totalAmount`; cancel/refund dùng persisted
`totalAmount`.

Trip DB dùng `btree_gist` + exclusion constraint trên equality `(routeId,stopId)` và overlap của
half-open time range để chặn template window trùng ngay cả khi concurrent. Money giữ đến đơn vị
đồng: `Money.FromRaw` pass-through; kết quả phép tính lẻ round `AwayFromZero`.

**Snapshot timing — chi tiết để tránh race condition:**

`Booking.totalAmount` được tính và **commit immutable tại thời điểm INSERT Booking record (status=PENDING_PAYMENT)** trong handler `POST /v1/bookings`. Cụ thể:

```
Trong DB transaction của Booking Service handler:
  1. Capture pricingAt đúng một lần ở handler start; HTTP call Trip-Route-Vehicle Service:
     GET /internal/v1/trips/{tripId}?pricingAt={pricingAt}
     → Raw Trip snapshot giữ nguyên wire fields; fareFromThisStop đã resolve theo
       MANUAL_OVERRIDE → active RouteStopFareTemplate → Trip.baseFare
  2. Apply voucher (nếu có) tại Booking Service local
  3. Tính totalAmount = (baseFare or exceptionFare) - discountAmount, làm tròn đến đồng gần nhất nếu phép tính ra số lẻ (BSOT v1.11.0 — KHÔNG floor 1000)
  4. INSERT Booking { status=PENDING_PAYMENT, totalAmount, discountAmount } — IMMUTABLE từ đây
  5. HTTP call Trip Service: POST /internal/v1/trips/{tripId}/lock-seats (seat hold)
  6. HTTP call Payment Service: POST /charge { amount=totalAmount, ... }
  COMMIT
```

**Race rule:** Booking.totalAmount được set tại bước 4 và **KHÔNG bao giờ thay đổi** dù sau đó Trip.baseFare bị operator edit. Nếu operator PATCH Trip.baseFare giữa step 1 và step 4 (rất hiếm, window milliseconds):
- Booking sẽ commit với fare snapshot LẤY TỪ STEP 1 (consistency cho 1 transaction)
- Booking sau (transaction mới) sẽ dùng fare mới

**KHÔNG re-query Trip.baseFare ở bước 4 hoặc trong PaymentSucceeded handler** — nếu re-query, sẽ phá rule snapshot và tạo case passenger thấy giá A trên screen nhưng bị charge giá B.

**Concurrent edit Trip.baseFare khi có Booking PENDING_PAYMENT đang VNPay redirect:**
- Booking đã INSERT với totalAmount cũ (lock tại step 4).
- VNPay charge theo totalAmount cũ (đã encode trong vnp_Amount).
- Operator edit baseFare → không ảnh hưởng booking đã có. Chỉ áp cho booking tạo sau update.
- Nếu authoritative Payment deadline qua + booking EXPIRED → passenger book lại sẽ thấy giá mới.

**Seat selection — có sơ đồ ghế (seat map):**

Khi hành khách bấm vào mua vé một chuyến cụ thể:
1. Hiển thị **số ghế còn trống** trong card tóm tắt chuyến (danh sách kết quả tìm kiếm)
2. Vào màn hình đặt vé → hiển thị **sơ đồ ghế 2D** của xe:
   - Ghế đã đặt: màu xám, không chọn được
   - Ghế đang hold bởi người khác (trong TTL 10 phút): màu vàng, không chọn được
   - Ghế trống: màu xanh, chọn được
3. Hành khách tap để chọn ghế → hệ thống **lock ghế trong Redis TTL 10 phút** trong khi hành khách điền thông tin và thanh toán
4. DB cần có: `Vehicle` có `seatLayoutJson` (JSON mô tả layout — số hàng, số cột, vị trí lối đi), `TripSeat` (tripId, seatNumber, status: AVAILABLE/HELD/BOOKED)

> **Lý do cần seat map:** Xe khách Việt Nam có ghế đơn/đôi, ghế đầu/cuối xe khác nhau về trải nghiệm → hành khách muốn chọn. Cũng cần để tránh double-booking khi nhiều user đặt cùng lúc.

**`Vehicle.seatLayoutJson` — API/UI contract (không phải DB schema):**

Đây là JSON contract dùng chung giữa BE và FE để render sơ đồ ghế. Cấu trúc cần có:
- `version` (int) — cho phép migrate schema sau này
- `vehicleTypeCode` string
- `totalSeats` (int), `rows` (int), `cols` (int), `decks` (int — 1 cho xe thường, 2 cho xe giường nằm)
- `aisles` array — mỗi entry có `afterCol` (số nguyên, ví dụ `afterCol: 2` cho layout 2-2 nghĩa là lối đi sau cột 2)
- `seats` array — mỗi seat có: `seatNumber` (string unique per vehicle, ví dụ "A01", "B05"), `row` + `col` (1-indexed), `deck`, `type` enum (STANDARD | SLEEPER_LOWER | SLEEPER_UPPER | VIP | DRIVER_AREA), `isWindow` (bool), `isAisle` (bool), `disabled` (bool — ghế hỏng/driver area, không bookable)

**Quy ước:**
- `seatNumber` là **string** (không phải số nguyên) — TripSeat reference theo string này
- `type` ảnh hưởng icon hiển thị, có thể ảnh hưởng pricing (VIP đắt hơn — defer cho v2, v1 dùng base fare)
- Backend validate khi operator tạo Vehicle: `totalSeats == seats.length`, không có `seatNumber` trùng
- Khi tạo Trip từ Vehicle, Hangfire job generate TripSeat record cho từng seat trong layout (status AVAILABLE, trừ `disabled: true`)

### 6.2 Hủy vé (Booking Cancellation)

Hành khách có thể hủy vé trong Passenger App → vào chi tiết vé → bấm "Hủy vé". Hệ thống kiểm tra thời điểm hủy so với giờ khởi hành để tính mức hoàn tiền.

**Chính sách hoàn tiền (cancellation policy — operator-configured):KHÔNG có mức phí hủy cố định toàn platform.** Mỗi operator tự cấu hình policy: tỷ lệ phí hủy (% hoặc khoản tiền) theo các khung thời gian trước giờ khởi hành.

**Cấu trúc policy:**
- Lưu trên `Operator.cancellationPolicy` JSONB (apply mặc định cho mọi trip của operator), có thể override per Route/Trip nếu cần.
- Format: array of `{ hoursBeforeDeparture: number, feePercent: number }` — sorted ascending theo hoursBeforeDeparture. Ý nghĩa: nếu hủy trong vòng ≤ X giờ trước khởi hành thì phí = Y%.

Ví dụ policy của 1 operator:
```
cancellationPolicy: [
  { hoursBeforeDeparture: 1,  feePercent: 100 },   // ≤ 1h trước departure: phí 100% (không hoàn)
  { hoursBeforeDeparture: 2,  feePercent: 50 },    // ≤ 2h: phí 50%
  { hoursBeforeDeparture: 24, feePercent: 10 }     // ≤ 24h: phí 10%
]
// Nếu hủy > 24h trước departure: phí 0% (hoàn 100%)
```

**Khi passenger hủy:**
1. Tra `Operator.cancellationPolicy` của operator thuộc Trip đó.
2. Tính `hoursToDeparture = (Trip.departureDateTime - now) / 3600`.
3. Tìm entry đầu tiên trong policy có `hoursBeforeDeparture >= hoursToDeparture` → `feePercent` áp dụng. Nếu không match (hủy quá sớm) → fee = 0%.
4. `refundAmount = round((paidAmount × (100 - feePercent) / 100), đồng gần nhất)` (BSOT v1.11.0 — không floor 1000).
5. Hoàn về **Ví VietRide** ngay lập tức.

**Platform cung cấp UI cho operator config policy** trên Operator Web (section 4.3) khi setup nhà xe và mỗi khi cần điều chỉnh.

**Luồng hủy vé:**
```
Hành khách bấm "Hủy vé"
  → App hiển thị confirmation: "Bạn sẽ được hoàn X% = Y VND về Ví VietRide"
  → Hành khách confirm
  → Booking Service: cập nhật booking status → CANCELLED
  → Ghế được giải phóng (TripSeat status → AVAILABLE)
  → Payment & Wallet Service: cộng tiền hoàn vào ví hành khách (consume event qua RabbitMQ)
  → Notification Service: gửi push notification xác nhận hủy + số tiền hoàn
```

**Operator hủy cả chuyến:**
```
Operator có thể hủy chuyến bất kỳ lúc nào TRƯỚC khi Trip.status = IN_PROGRESS
(SCHEDULED hoặc BOARDING). Không có hard cutoff về thời gian.
Sau IN_PROGRESS chỉ có thể dùng Vehicle Substitution (6.12) hoặc DISRUPTED no-substitution (6.12.1).

POST /v1/operator/trips/{tripId}/cancel:
  Role: OPERATOR_ADMIN (decision có business impact)
  Body: { reason: text required }

Operator bấm "Hủy chuyến" trên dashboard:
  → UI gọi `POST /v1/operator/trips/{tripId}/cancel/preview` (idempotent, không thay đổi state)
    Response: { affectedBookingIds: [...], refundTotalBooking, affectedParcelIds: [...],
                refundTotalParcel, grandTotal }
  → Hiển thị confirm dialog: "Sẽ hoàn [X] VND cho [N] hành khách + [M] parcel. Xác nhận?"
  → Operator confirm 2 bước
  → Trip status → CANCELLED, cancelledAt, cancelledByUserId, cancelReason
  → Tất cả booking của chuyến đó → CANCELLED (refundOverride=true,
    cancellationReason=OPERATOR_CANCELLED_TRIP)
  → Hoàn 100% cho toàn bộ hành khách → Payment & Wallet Service xử lý refund
  → Notification Service: gửi push đến từng hành khách

UX khuyến nghị (không bắt buộc): nếu hủy trong vòng 24h trước departure, dashboard
hiển thị warning "Hủy gấp ảnh hưởng nhiều hành khách — cân nhắc gửi voucher xin lỗi".

> **Vì sao KHÔNG còn check balance trước cancel (khác trước đây):** với wallet model
> (Section 4.6), doanh thu chỉ settle vào OperatorWallet sau khi Trip COMPLETED/DISRUPTED
> + đủ 7 ngày hold. Trip cancel xảy ra TRƯỚC khi terminal → KHÔNG có TripSettlement nào
> tạo cho chuyến này → wallet operator chưa từng được credit. Refund cho passenger chảy
> trực tiếp từ PlatformWallet holding pool, không debit OperatorWallet.
> Các ledger entry BOOKING_REFUND/PARCEL_REFUND vẫn được INSERT cho audit (đánh dấu trip
> này có refund). Operator không cần có balance để cancel.
```

**Trip cancel — refund compensation flow (durability):**

Trip cancel có 2 phase: (A) update state — atomic; (B) refund từng booking — eventual qua event. Phase B có thể fail (Wallet Service down, partial refund). Vẫn phải đảm bảo eventually consistent.

```
PHASE A — Trip-local atomic state update:
  1. UPDATE Trip SET status=CANCELLED, cancelledAt=now, cancelledByUserId, cancelReason
  2. INSERT OutboxEvent {
       eventType: "trip.trip.cancelled",
       payload: { eventId, occurredAt, tripId, operatorId, cancelledAt, cancelReason },
       status: PENDING
     }
  3. COMMIT Trip + Outbox cùng local transaction.

Booking và Parcel KHÔNG bị Trip Service ghi trực tiếp. Mỗi service consume
`trip.trip.cancelled`, lock/recheck rows local và dedupe bằng EventId:
  - Booking chuyển row eligible sang CANCELLED và publish canonical
    `booking.booking.cancelled` per Booking với refundAmount frozen.
  - Parcel dùng cùng classifier cho preview/execution. Pre-load → CANCELLED + release cargo +
    publish Parcel refund request; LOADED/IN_TRANSIT → PENDING_OPERATOR_ACTION, chưa refund/release.

PHASE B — Payment refund processing:
  Payment chỉ consume canonical `booking.booking.cancelled` và Parcel refund request, không
  consume `trip.trip.cancelled` trực tiếp. Mỗi reference được xử lý trong local Payment
  transaction: credit PassengerWallet, debit PlatformWallet, append immutable wallet/payment/
  operator-ledger rows và publish `payment.wallet.credited`. Một reference fail được ghi
  RefundFailureLog và không block các reference khác.

RefundFailureLog entity (Payment & Wallet Service):
  {
    id UUID,
    bookingId UUID nullable,
    parcelId UUID nullable,
    triggerEventType string ("trip.trip.cancelled", "booking.booking.cancelled", etc.),
    failureReason text,
    retryCount int default 0,
    lastAttemptAt datetime,
    resolvedAt datetime nullable,
    resolvedByUserId UUID nullable (System Admin manual resolve),
    createdAt
  }

Hangfire retry job (Payment Service, mỗi 5 phút):
  SELECT RefundFailureLog WHERE resolvedAt IS NULL AND retryCount < 5
  FOR EACH:
    Retry Phase B logic cho booking/parcel đó
    IF success: UPDATE RefundFailureLog SET resolvedAt = now
    IF fail: increment retryCount, update lastAttemptAt
    IF retryCount >= 5: alert System Admin qua Sentry + Notification dashboard
                       → Admin can manual trigger refund qua endpoint
                         POST /v1/admin/refund-failures/{id}/manual-resolve

Booking Service consume "payment.wallet.credited" event:
  → UPDATE Booking SET status=REFUNDED, refundedAt=now
  → Push notification "Đã hoàn [X] VND về ví" qua Notification Service
```

**Đảm bảo eventual consistency:** Phase A đảm bảo state Booking/Trip CANCELLED ngay. Phase B retry đến khi success hoặc Admin can thiệp. Passenger nhìn thấy booking CANCELLED ngay, refund về sau (có notification khi đến). Đây là pattern compensating transaction chấp nhận được — không có distributed transaction global.

**Error codes mới:** `REFUND_FAILURE_PERSISTED` (Admin manual handle), `REFUND_RETRY_EXHAUSTED`.

**Ràng buộc:** Hành khách không thể hủy sau khi chuyến đã `IN_PROGRESS` hoặc `COMPLETED`.

### 6.2.1 Booking COMPLETED — Trigger

`BookingStatus.COMPLETED` được trigger khi `Trip.status` chuyển sang `COMPLETED`. Flow:

```
Trip-Route-Vehicle Service: Trip.status → COMPLETED
  → Publish event TripCompleted { tripId } qua RabbitMQ (Outbox pattern)

Booking Service consume TripCompleted:
  → UPDATE Booking SET status = COMPLETED
    WHERE tripId = :tripId AND status IN (CONFIRMED, PARTIAL_NO_SHOW)
    (NO_SHOW và CANCELLED giữ nguyên status — không chuyển COMPLETED)
```

**Trip COMPLETED trigger — 2 cơ chế (PRIMARY + fallback):**

```
PRIMARY — Driver HOẶC Assistant bấm "Kết thúc chuyến" trong Driver App:
  Endpoint: POST /v1/driver/trips/{tripId}/complete
    - Role check: caller phải là DRIVER hoặc ASSISTANT của trip này (Trip.driverId hoặc Trip.assistantId)
    - Precondition: Trip.status = IN_PROGRESS
    - Trip-Route-Vehicle Service: Trip.status → COMPLETED, completedByUserId = caller userId
    - INSERT AuditLog { userId: caller, action: TRIP_COMPLETED_MANUAL,
        metadata: { tripId, role: DRIVER|ASSISTANT } }
    → Publish TripCompleted event (Outbox pattern)
    → Booking Service consume → UPDATE Booking → COMPLETED

SECONDARY — Hangfire auto-fallback (dùng khi Driver/Assistant quên bấm):
  Hangfire job (chạy mỗi 15 phút):
    SELECT Trip WHERE status = IN_PROGRESS
      AND estimatedArrivalTime < now - interval '30 minutes'
    → Trip.status → COMPLETED (auto)
    → Publish TripCompleted event
```

> **Lý do cần cả 2:** PRIMARY đảm bảo Trip COMPLETED đúng thời điểm thực tế; SECONDARY là failsafe tránh Trip treo trạng thái IN_PROGRESS mãi mãi (ví dụ Driver quên bấm, app crash). Window 30 phút đủ để Driver bấm sau khi xe vào bến.

> **Ảnh hưởng:** Booking COMPLETED → không thể hủy vé, không hoàn tiền. Dùng để tính revenue report chính xác (chỉ count COMPLETED, không count CONFIRMED còn chờ). Agent thiết kế DB/report cần dùng `BookingStatus.COMPLETED` làm filter doanh thu thực tế.

### 6.2.2 No-Show (Hành khách không lên xe)

Khi hành khách đã đặt vé, đã thanh toán, nhưng không xuất hiện tại điểm đón → sau khi xe rời điểm đón cuối cùng mà hành khách vẫn chưa được tick "đã lên xe" → booking chuyển sang `NO_SHOW`.

**Flow:**

```
Driver/Assistant App tại mỗi điểm đón:
  - Hiển thị danh sách passenger sắp đón tại stop này
  - Tick "Đã lên xe" cho từng passenger
  - Khi xe rời stop, hệ thống flag những passenger chưa tick là "potentially no-show"

Hangfire `NoShowDetectionJob` (chạy mỗi 5 phút) có 2 trigger strict theo loại pickup:

  TRIGGER A — Along-route pickup (`Booking.pickupStopId NOT NULL`):
    chỉ đủ điều kiện khi `Booking.status=CONFIRMED`, pickup TripStop có
    `status=ARRIVED`, và `TripStop.actualArrivalTime + 15 minutes < now` (equality bị loại).

  TRIGGER B — Terminal pickup (`Booking.pickupStationId NOT NULL`, `pickupStopId NULL`):
    chỉ đủ điều kiện khi `Booking.status=CONFIRMED`, `Trip.status=IN_PROGRESS`, và
    `Trip.actualDepartureTime + 15 minutes < now` (equality bị loại). Terminal không có
    TripStop anchor nên dùng trực tiếp thời điểm rời bến.

Booking fails closed khi raw Trip snapshot/anchor upstream lỗi hoặc thiếu; không suy diễn từ
planned ETA, stale projection, hoặc Outbox publish time. Job locks Booking/Passenger rows,
rechecks the strict anchor and status, marks every remaining `PENDING` passenger as `NO_SHOW`,
and appends one `MARK_NO_SHOW` history row plus one
`booking.booking.passenger_no_show_marked` fact per Booking transition.

The existing raw `GET /internal/v1/trips/{tripId}` snapshot is extended only with nullable
trip-level `actualDepartureTime`; stop `status` and nullable `actualArrivalTime` remain
authoritative. It requires only a valid Internal JWT (`401 AUTH_TOKEN_INVALID` otherwise), adds no
tenant authorization, and introduces no event/projection seam.

  Common cho cả A và B:
    → TripSeat status giữ nguyên BOOKED (không release — ghế đã bán, hành khách bỏ tự nguyện)
    → Publish event PassengerNoShow → Notification Service tạo Notification.type = PASSENGER_NO_SHOW và gửi push:
       "Bạn đã không lên xe đúng giờ. Vé không được hoàn tiền theo chính sách."
```

**Chính sách hoàn tiền NO_SHOW:**
- **Không hoàn tiền** — passenger đã có cơ hội đến đúng giờ + thông báo ETA realtime
- Khác với CANCELLED (chủ động hủy trước cutoff) — NO_SHOW là passive forfeit
- Ghế đã BOOKED không quay lại pool AVAILABLE — xe vẫn chạy với ghế trống đó

**Edge case — Partial no-show:**

Khi một số passenger trong cùng booking lên xe, một số không → Booking status chuyển sang **`PARTIAL_NO_SHOW`** (status mới, không phải CONFIRMED).

```
Trường hợp 1: TẤT CẢ Passenger no-show
  → Booking.status = NO_SHOW
  → Không hoàn tiền

Trường hợp 2: MỘT PHẦN Passenger no-show (ví dụ 1 BOARDED + 2 NO_SHOW trong booking 3 ghế)
  → Booking.status = PARTIAL_NO_SHOW
  → Passenger entity giữ boardingStatus riêng (BOARDED / NO_SHOW)
  → Không hoàn tiền (chính sách NO_SHOW áp cho 2 passenger no-show — passenger đã có cơ hội)
  → Revenue report: count toàn bộ totalAmount (vì đã thu tiền 3 ghế)
  → Khi Trip COMPLETED → Booking từ PARTIAL_NO_SHOW chuyển sang COMPLETED
     (giống CONFIRMED → COMPLETED, không treo trạng thái)

Trường hợp 3: TẤT CẢ Passenger BOARDED
  → Booking giữ CONFIRMED → COMPLETED khi Trip COMPLETED (bình thường)
```

Exactly one `booking.booking.passenger_no_show_marked` event is emitted for each successful
Booking transition, with `bookingStatus=NO_SHOW|PARTIAL_NO_SHOW`, newly marked passenger ids,
`triggerType=ALONG_ROUTE|TERMINAL`, and an optional `pickupStopId` only for along-route pickup.
The event identity is stable end-to-end: `eventId == OutboxEvent.Id == RabbitMQ MessageId`.

Passenger entity cần track per-passenger boarding: `boardingStatus` enum (PENDING | BOARDED | NO_SHOW, default PENDING), thời điểm boarded, và stop nơi passenger thực sự lên xe (để audit + đối chiếu pickup stop đã đặt).

```
Passenger entity bổ sung field:
  boardingStatus: PENDING | BOARDED | NO_SHOW (default PENDING)
  boardedAt: datetime nullable
  boardedAtStopId: FK nullable (stop nơi passenger lên xe)
```

> **Lưu ý:** NO_SHOW khác với passenger không xác nhận route change. Route change passenger không phản hồi đi theo `BookingPendingAction` riêng; timeout có thể auto-fallback destination station theo policy route-change, không mark NO_SHOW và không refund tự động. NO_SHOW = không xuất hiện vật lý.

### 6.2.3 Schedule Time Change (Đổi giờ khởi hành)

Spec đầy đủ tại **Section 6.13** — phân loại 3 mức (MINOR/MEDIUM/MAJOR), confirmation window, BookingPendingAction flow, refund tier (0% / 50% / 100%).

**Edge case riêng — Minor schedule change khi booking đang có BookingPendingAction active:** Minor change (≤ 2 giờ) chỉ push informational notification, **không** tạo BookingPendingAction mới và **không interrupt** pending action đang tồn tại. Deadline của pending action (ROUTE_CHANGE, SEAT_DOWNGRADE, etc.) **không thay đổi** do minor schedule change.

### 6.3 Theo dõi xe thời gian thực (Real-time GPS Tracking)

**Đây là tính năng cốt lõi** — giải quyết pain point chính: hành khách không biết xe đến lúc nào.

Khi chuyến bắt đầu, Driver App gửi vị trí GPS liên tục lên hệ thống. Hành khách mở màn hình tracking trong Passenger App và thấy:
- Vị trí xe hiện tại trên bản đồ, cập nhật liên tục (gần thời gian thực)
- ETA đến **điểm đón của mình** (không phải điểm cuối tuyến)
- Danh sách điểm dừng còn lại trên lộ trình theo thứ tự

Hành khách không cần mở app liên tục — khi xe sắp đến điểm đón, hệ thống tự động gửi push notification cảnh báo **2 lần** theo ngưỡng sau:

**"Xe sắp đến" — approaching notification spec (phân biệt theo loại pickup):**

```
Along-route pickup (Booking.pickupStopId NOT NULL) — 2 wave:
  - Wave 1: ETA ≤ 30 phút → "Xe của bạn sẽ đến [stop name] trong khoảng 30 phút"
  - Wave 2: ETA ≤ 10 phút → "Xe của bạn sắp đến [stop name]! Vui lòng ra điểm đón."

Terminal pickup (Booking.pickupStationId NOT NULL, pickupStopId NULL) — 1 wave duy nhất:
  - Wave 1: T-30 phút trước Trip.departureDateTime (không tính ETA — dùng scheduled departure)
    "Chuyến [route name] khởi hành lúc [time]. Vui lòng có mặt tại bến [station name] trước 30 phút."
  - KHÔNG có wave 2 — xe đã ở bến trước giờ xuất phát, passenger phải đến bến theo quy định
    nhà xe (thường 15-30 phút trước departure)
  - Trigger: Hangfire job trong Trip-Route-Vehicle Service (chạy mỗi 5 phút):
      SELECT Trip WHERE departureDateTime BETWEEN now+25m AND now+35m
        AND status IN (SCHEDULED, BOARDING)
      FOR EACH Booking với pickupStationId = trip.originStationId AND status = CONFIRMED:
        Publish ApproachingAlert { tripId, bookingId, stationId, wave=1, terminal=true }
```

Spec chi tiết wave (along-route):

```
Lần 1 — Cảnh báo sớm:
  ETA đến pickup stop của passenger ≤ 30 phút
  → Tracking Service phát hiện khi tính ETA (check sau mỗi ETA recalculate)
  → Publish ApproachingAlert { tripId, bookingId, stopId, etaMinutes, wave=1 }
  → Notification Service: push "Xe của bạn sẽ đến [stop name] trong khoảng 30 phút"
  → Notification type: TRIP_VEHICLE_APPROACHING

Lần 2 — Cảnh báo gấp:
  ETA đến pickup stop của passenger ≤ 10 phút
  → Publish ApproachingAlert { ..., wave=2 }
  → Notification Service: push "Xe của bạn sắp đến [stop name]! Vui lòng ra điểm đón."
  → Notification type: TRIP_VEHICLE_APPROACHING (cùng type, wave=2 phân biệt trong payload)

Chống duplicate — Redis key:
  approaching_notified:{tripId}:{bookingId}:w1  TTL đến hết chuyến  (lần 1 đã gửi)
  approaching_notified:{tripId}:{bookingId}:w2  TTL đến hết chuyến  (lần 2 đã gửi)
  → Tracking Service check key trước khi publish event — chỉ push 1 lần mỗi wave

Điều kiện trigger along-route wave 1/2:
  - Trip.status = IN_PROGRESS
  - Booking.status = CONFIRMED (không push cho NO_SHOW/CANCELLED)
  - Booking.pickupStopId = stopId đang tính ETA (along-route pickup)

Terminal pickup wave 1 trigger riêng (Hangfire Trip-Route-Vehicle):
  - Trip.status IN (SCHEDULED, BOARDING)
  - Booking.status = CONFIRMED
  - Booking.pickupStationId = trip.originStationId
  - T-30 phút trước Trip.departureDateTime (window 25-35 phút)

Service trigger: Tracking Service (có ETA Redis) detect ngưỡng → publish event qua Outbox
  → RabbitMQ → Notification Service consume → FCM push
```

Tài xế trên Operator Dashboard cũng thấy vị trí thời gian thực của tất cả chuyến đang chạy trên bản đồ.

**Kiến trúc GPS — Redis buffer + batch write:**

```
Driver app gửi location mỗi 3–5 giây
  → Tracking Service nhận
  → Ghi vào Redis: overwrite latest position (TTL 5 phút)
                   append vào GPS trail buffer (list per tripId)
  → Broadcast ngay qua Socket.IO đến room trip:{tripId}

Background job (BullMQ scheduled job — chạy mỗi 5–10 phút trong Tracking Service NestJS):
  → Đọc buffered GPS trail từ Redis (key prefix `tracking:gps_buffer:{tripId}`)
  → Batch insert vào PostgreSQL bảng GpsTrail { tripId, lat, lng, speed, timestamp } qua Prisma
  → Clear buffer trong Redis
```

> **Trade-off chấp nhận được:** Nếu Tracking Service crash giữa chu kỳ, mất tối đa 5–10 phút GPS history — không ảnh hưởng booking hay thanh toán, chấp nhận được cho use case tracking.

> **Cơ chế batch job — BullMQ:** Tracking Service là NestJS, không dùng Hangfire (.NET only). Pattern dùng **BullMQ scheduled job** (cùng stack với Notification Service đã có sẵn BullMQ + Redis):
> - Bootstrap: setup repeat job với interval 5 phút trên queue `gps-batch`
> - Job handler: đọc danh sách active tripIds từ Redis set, với mỗi tripId đọc buffer GPS list, batch insert vào PostgreSQL qua Prisma, xóa buffer sau khi insert thành công
> - Tránh dùng `setInterval` thuần — mất khi service restart, không persistent. BullMQ scheduled job persistent (lưu state trong Redis), tự resume sau restart.

### 6.4 Thay đổi lộ trình giữa chuyến

**Cấu trúc Alternative Route:**
- Mỗi Route chính có **tối đa 2 tuyến phụ** (alternative routes) — đủ cho 1 phương án kẹt xe nhẹ và 1 phương án sự cố lớn
- Alternative Route có **danh sách Stop riêng hoàn toàn** — không dùng lại stop của tuyến chính vì lộ trình khác nhau có thể không đi qua các điểm cũ
- Relationship: `Route (1) → (0..2) AlternativeRoute`, mỗi `AlternativeRoute` có stop sequence độc lập qua junction table `AlternativeRouteStop (alternativeRouteId, stopId, orderIndex, estimatedDuration)`
- Operator định nghĩa alternative routes trước (trong lúc quản lý tuyến), không tạo mới trong lúc sự cố

**Làm sao operator biết cần đổi tuyến:** Hệ thống kết hợp 2 cơ chế:

1. **Alert tự động:** Khi GPS xe lệch khỏi lộ trình kế hoạch quá ngưỡng định nghĩa bên dưới, hệ thống tự động gửi alert đến Operator Dashboard và thông báo cho tài xế qua app

   **Off-route detection — threshold cụ thể (Tracking Service implement theo đây):**
   ```
   Distance threshold: khoảng cách vuông góc từ vị trí xe đến đường route > 500m
   Time threshold: liên tục > 2 phút ngoài zone mới trigger alert
                   (tránh false positive do GPS drift hoặc đường vòng ngắn)

   Algorithm:
     1. Với mỗi GPS update, tính khoảng cách từ lat/lng xe đến segment đường route gần nhất
     2. Nếu distance > 500m: bắt đầu đếm timer (lưu Redis key off_route_since:{tripId})
     3. Nếu khoảng cách về < 500m trước 2 phút: clear timer (false alarm)
     4. Nếu timer đạt 2 phút liên tục: gửi OffRouteAlert event → Notification Service
   ```
2. **Tài xế/phụ xe báo:** Nhận thấy đường tắc hoặc sự cố, tài xế báo trực tiếp qua app (nút "Báo sự cố") → operator nhận notification

Sau khi nhận alert, operator xem vị trí xe, chọn alternative route phù hợp và confirm đổi tuyến.

**Xử lý hành khách khi đổi tuyến:**

Sau khi operator confirm đổi tuyến, hệ thống phân loại hành khách trên chuyến:

```
Hành khách TERMINAL pickup → không bị ảnh hưởng về điểm đón
  → Nhận notification thông báo lộ trình thay đổi, ETA mới

Hành khách ALONG_ROUTE stop — stop vẫn tồn tại trên tuyến mới
  → Cập nhật ETA mới cho stop đó
  → Nhận notification ETA mới

Hành khách ALONG_ROUTE stop — stop KHÔNG tồn tại trên tuyến mới
  → Nhận notification: "Lộ trình thay đổi — điểm đón của bạn không còn trên tuyến mới"
  → App hiển thị danh sách stop trên tuyến mới để hành khách chọn lại
  → Tạo BookingPendingAction { reason = ROUTE_CHANGE }
  → Confirmation window phụ thuộc trạng thái chuyến:
      - Trip.status = IN_PROGRESS: 30 phút
        (`ROUTE_CHANGE_CONFIRM_WINDOW_IN_PROGRESS_MINUTES = 30`)
      - Trip.status = SCHEDULED hoặc BOARDING: 60 phút
        (`ROUTE_CHANGE_CONFIRM_WINDOW_PRE_PROGRESS_MINUTES = 60`)
    Lý do: khi xe đang chạy cần quyết định nhanh; trước khi xe chạy, 30 phút quá gấp
    cho passenger sắp xếp lại điểm đón.
  → Nếu không phản hồi trong thời hạn → hệ thống tự động fallback:
    BookingPendingAction.resolvedAction = AUTO_FALLBACK_DESTINATION
    Booking.status = CONFIRMED (giữ nguyên), gửi notification xác nhận
```

**AUTO_FALLBACK_DESTINATION — prose giải thích đầy đủ:**

Khi passenger không phản hồi trong window confirmation, hệ thống auto-fallback theo quy trình sau:

1. **Chuyến chính (xe đang chạy):** đưa passenger đến **điểm cuối của tuyến thay thế** (destination station mới của alternative route) — đây là điểm xe thực sự sẽ đến. Passenger không bị kẹt giữa đường vì tuyến mới có thể không đi qua stop cũ.

2. **Shuttle fallback (xe trung chuyển):** sau khi xe chính đến destination tuyến thay thế, **shuttle của nhà xe sẽ đưa passenger từ destination về điểm stop ban đầu họ đã đặt**. Đây là dịch vụ trung chuyển bổ sung do operator chịu trách nhiệm tổ chức (vì lỗi đổi tuyến từ phía operator). Xem flow shuttle chi tiết tại section 6.14.

3. **Notification gửi passenger:** "Vì bạn không phản hồi, vé của bạn được tự động chuyển: xe sẽ đưa bạn đến [destination tuyến mới] và shuttle của nhà xe sẽ đưa bạn về điểm dừng [stop name] ban đầu. Liên hệ phụ xe để biết chi tiết."

> **Lưu ý:** Không có hoàn tiền tự động khi đổi tuyến do bất khả kháng (tắc đường, sự cố) — đây là điều kiện vận hành thực tế. Booking vẫn `CONFIRMED`; Operator hủy cả chuyến thì mới trigger luồng hoàn tiền theo trip cancellation/disruption.

> **Implementation note:** AUTO_FALLBACK_DESTINATION là **business resolution**, không phải nghĩa đen "đổi pickupStationId = destinationStationId". Agent lưu resolution trong `BookingPendingAction.resolvedAction` và `metadata` (gồm: originalStopId, fallbackDestinationStationId, shuttleRequired = true) để audit + trigger shuttle dispatch. Operator dashboard hiển thị danh sách shuttle pickup requests cần điều phối.

**Route change pricing — khi pickup thay đổi do route change:**
```
Hành khách along-route stop bị đổi pickup sang terminal (stop cũ không có trên tuyến mới):
  → Hành khách chọn không phản hồi → hệ thống chuyển về terminal đích
  → GIỮ NGUYÊN GIÁ CŨ — không tính lại fare, không refund delta

Lý do: việc route change do bất khả kháng (tắc đường, sự cố) — giá vé đã commit.
Terminal là điểm nhận nếu stop không có. Operator chịu trách nhiệm logistic.
Nếu hành khách không hài lòng → xử lý theo chính sách hủy hiện hành hoặc liên hệ operator;
route change không tự tạo refund 100% khi chuyến vẫn tiếp tục vận hành.
```

### 6.4.1 Stop Disable Flow (Operator disable Stop đang được dùng)

Khi Operator disable một Stop, canonical mutation là bodyless
`DELETE /v1/operator/stops/{stopId}?replacedByStopId=`. `OPERATOR_ADMIN` phải gửi UUID-v4
`Idempotency-Key`; first success sets `isActive=false`, preserves `deletedAt`, stores the optional
same-operator/cycle-free replacement, and publishes `trip.stop.disabled`. `PATCH
/v1/operator/stops/{stopId}` remains details-update-only: it never disables a Stop and never emits
`trip.stop.disabled`.

**Flow:**

```
Operator bấm "Vô hiệu hóa Stop" trên dashboard:
  → DELETE ghi Stop và Outbox atomically; response luôn có `warning: null` và KHÔNG có
    `ActiveBookingCount`. `STOP_DISABLED_BOOKING_AFFECTED` là legacy/deprecated cho DELETE;
    Booking impact is sourced only from `booking.stop_disabled.affected`.

Booking Service consume StopDisabled:
  FOR EACH Booking WHERE (pickupStopId = stopId OR dropoffStopId = stopId)
                          AND status = CONFIRMED
                          AND Trip.status IN (SCHEDULED, BOARDING):
    INSERT BookingPendingAction {
      reason   = STOP_DISABLED,
      deadline = min(capturedNow + 24h, Trip.tripCurrentDeparture - 2h),
      metadata = {
        disabledStopId,
        affectedField: "PICKUP" | "DROPOFF",
        suggestedStopId: replacedByStopId,
        fallbackStationId: affectedField == "PICKUP"
          ? Route.originStationId
          : Route.destinationStationId
      }
    }
    Push notification passenger:
      "Điểm [pickup/trả] [Stop name] của chuyến [X] đã ngừng hoạt động.
       Vui lòng chọn điểm thay thế trước [deadline], hoặc hệ thống sẽ tự động
       chuyển về [terminal name] / [suggested stop]. Bạn có quyền hủy với hoàn 100%."

Passenger 3 options trong window:
  1. Chọn stop khác trên route → POST /v1/bookings/{id}/edit-pickup (hoặc edit-dropoff)
     → BookingPendingAction.resolvedAction = ACCEPTED, resolvedAt = now
     → Booking giữ CONFIRMED với pickupStopId/dropoffStopId mới

  2. Accept fallback về terminal (mặc định) → POST /v1/bookings/{id}/pending-action/{id}/accept-fallback
     → Pickup case: UPDATE Booking.pickupStationId = Route.originStationId, pickupStopId = NULL
     → Dropoff case: UPDATE Booking.dropoffStationId = Route.destinationStationId, dropoffStopId = NULL
     → resolvedAction = AUTO_FALLBACK_DESTINATION (reuse enum hiện có)

  3. Hủy hoàn 100% → POST /v1/bookings/{id}/cancel với reason=STOP_DISABLED_REFUSED
     → Booking → CANCELLED, refundOverride=true, cancellationReason=STOP_DISABLED_REFUSED
     → Refund 100% (lỗi operator, không áp cancellation policy)

Hangfire `StopDisabledAutoFallbackJob` (Booking Service, mỗi 5 phút):
  SELECT BookingPendingAction WHERE reason = STOP_DISABLED
    AND resolvedAt IS NULL AND deadline < now
  → Auto-fallback giống option 2 (terminal đích)
  → resolvedAction = AUTO_FALLBACK_DESTINATION
  → Publish one `booking.booking.stop_disabled_auto_fallback_applied` per resolved action
  → Push notification: "Vì bạn không phản hồi, vé chuyển về [terminal name]."
```

`deadline == now` still creates the action and remains passenger-action eligible; no synchronous
fallback occurs at equality (no synchronous fallback); only a later scheduler pass with strict
`deadline < now` resolves it.

**Phân biệt STOP_DISABLED vs ROUTE_CHANGE:**

| | ROUTE_CHANGE (6.4) | STOP_DISABLED (6.4.1) |
|---|---|---|
| Trigger | Operator confirm Alternative Route trong/trước trip | Operator disable Stop entity |
| Scope | Stop không còn trên tuyến mới (tuyến thay đổi) | Stop bị xóa khỏi hệ thống (không hoạt động nữa) |
| Refund khi cancel | Theo cancellation policy (route change do bất khả kháng) | 100% (lỗi operator) |
| Shuttle fallback | Có (shuttle đưa về stop ban đầu) | Không |
| Window | 30/60 phút | 24 giờ hoặc đến T-2h, whichever sooner |

### 6.5 Thanh toán & Ví điện tử

Hệ thống có **PassengerWallet** (ví hành khách) và **PlatformWallet** (clearing/holding pool của VietRide — xem 4.6). Hành khách nạp tiền vào PassengerWallet qua VNPay rồi dùng ví thanh toán nhanh khi đặt vé. PassengerWallet cũng là kênh hoàn tiền mặc định khi hủy vé. PlatformWallet chỉ ghi nhận tiền đang được platform giữ/clearing cho booking/parcel/subscription/settlement, không phải ví mà passenger thao tác trực tiếp.

**Phương thức thanh toán:**

| Phương thức | Trải nghiệm |
|---|---|
| **Ví VietRide** | Thanh toán ngay, không redirect, mượt nhất |
| **VNPay** | Redirect sang cổng VNPay, callback xác nhận, hoàn tất booking |

**Hoàn tiền khi hủy vé:**
- **Hoàn về ví VietRide:** tức thì (v1 — luôn thực hiện được)
- **Hoàn về ngân hàng qua VNPay Refund API:** feature flagged cho v2, phụ thuộc khả năng tích hợp

**Wallet & WalletTransaction — requirements:**

- **Wallet:** 1 User có đúng 1 Wallet. **`userId` là PK natural** (giống pattern `OperatorWallet.operatorId` PK — xem 4.6) — không có cột `id` synthetic riêng. Lưu balance (BIGINT, VND, có constraint không âm), currency (default 'VND', future-proof multi-currency), `rowVersion` cho optimistic lock. Bootstrap row qua event `identity.user.created` consume → `INSERT wallets { user_id, balance=0 } ON CONFLICT (user_id) DO NOTHING` (UPSERT idempotent). Soft delete không cần (xóa user = xóa wallet via cascade event).
- **WalletTransaction:** mỗi giao dịch ví là 1 record immutable. **Tham chiếu wallet qua `userId`** (logical FK tới `wallets.user_id` = `identity.users.id`) — KHÔNG có hard FK tới `wallets` table (mirror pattern `OperatorWalletTransaction` không FK tới `OperatorWallet`). Consistency enforce app-layer: mọi INSERT WalletTransaction phải atomic cùng DB transaction với UPDATE Wallet.balance + optimistic lock check. Fields:
  - `userId` — logical FK
  - `amount` luôn dương — `type` (CREDIT | DEBIT) quyết định chiều
  - `referenceType` enum: TOP_UP, BOOKING_PAYMENT, BOOKING_REFUND, PARCEL_PAYMENT, PARCEL_REFUND, MANUAL_ADJUSTMENT
  - `referenceId` nullable — trỏ tới bookingId/parcelId/topUpRequestId
  - **`balanceBefore`/`balanceAfter` snapshot** — bắt buộc cho audit trail, không cần recalculate balance từ đầu khi audit, đồng thời dùng làm optimistic lock chống race condition (UPDATE Wallet SET balance = :balanceAfter, rowVersion = rowVersion+1 WHERE user_id = :userId AND balance = :balanceBefore AND rowVersion = :originalRowVersion)
  - `note` nullable — human-readable, đặc biệt cho MANUAL_ADJUSTMENT cần ghi lý do

**Wallet top-up flow (nạp tiền qua VNPay):**

`TopUpRequest` entity track 1 lần nạp ví: userId, amount, status (PENDING | SUCCEEDED | FAILED | EXPIRED), vnpayTxnRef (string unique — mã giao dịch gửi sang VNPay), vnpayResponseCode (nullable), timestamps. Mỗi top-up tạo 1 TopUpRequest, sau callback success thì insert WalletTransaction tương ứng.

```
Flow:
1. Passenger chọn "Nạp tiền" → nhập số tiền → POST /wallet/top-up { amount }
   → Payment Service validate amount >= 10_000 VND
     - Nếu amount < 10_000 → return error `WALLET_TOP_UP_AMOUNT_TOO_LOW`
       ("Số tiền nạp tối thiểu là 10,000 VND.")
   → Payment Service tạo TopUpRequest { status = PENDING }
   → Tạo VNPay redirect URL (tương tự booking payment)
   → Return redirect URL cho client

2a. VNPay server gọi POST /v1/payments/vnpay-topup-ipn (IPN — server-to-server)
     → Payment Service verify HMAC-SHA512
     → Nếu success:
         UPDATE TopUpRequest status = SUCCEEDED
         UPDATE Wallet balance += amount (trong DB transaction)
         INSERT WalletTransaction { type=CREDIT, referenceType=TOP_UP, referenceId=topUpRequestId }
         KHÔNG credit PlatformWallet tại bước top-up; PlatformWallet chỉ nhận CREDIT khi PassengerWallet
         được dùng để thanh toán booking/parcel, nhằm tránh double-count cùng một khoản tiền.
         Publish event WalletCredited → Notification Service: push "Nạp tiền thành công: +X VND"
    → Nếu fail/cancel:
        UPDATE TopUpRequest status = FAILED
    → Return {"RspCode":"00","Message":"Confirm Success"} cho VNPay

2b. Browser redirect về GET /v1/payments/vnpay-topup-return?vnp_ResponseCode=00&...
    → Payment Service query TopUpRequest status hiện tại
    → Return kết quả cho client hiển thị (KHÔNG xử lý business logic)

3. Hangfire job (mỗi 30 phút):
   SELECT TopUpRequest WHERE status=PENDING AND createdAt < now - 15 phút
   → UPDATE status = EXPIRED (VNPay session hết hạn)
```

**BookingStatus — CANCELLED → REFUNDED transition:**

```
Sau khi booking CANCELLED + refund được xử lý:
  Booking Service nhận event WalletCredited (hoặc WalletTransaction đã insert):
  → UPDATE Booking SET status = REFUNDED, refundedAt = now

  Payment Service consume `booking.booking.cancelled` event (hoặc xử lý ngay khi tạo refund):
  → INSERT PlatformWalletTransaction { type=DEBIT, referenceType=BOOKING_REFUND,
                                       amount=refundAmount,
                                       referenceId=bookingId }
  → UPDATE PlatformWallet balance -= refundAmount (optimistic lock + CHECK balance >= refundAmount).
    Nếu refund xảy ra sau khi trip settlement đã SETTLED, phải đi kèm admin OperatorWallet DEBIT
    + PlatformWallet adjustment tương ứng như 4.6 để không lệch reconciliation.
  → INSERT OperatorLedgerEntry { entryType=BOOKING_REFUND, tripId, amount = -refundAmount,
                                  referenceType=BOOKING, referenceId=bookingId, ... }
  → KHÔNG update OperatorWallet ngay (xem 4.6): nếu trip chưa terminal/settled, refund
    không ảnh hưởng wallet (revenue chưa từng vào wallet). Nếu trip đã SETTLED rồi mới refund
    (late refund) → Admin manual debit wallet qua `POST /v1/admin/operators/{id}/wallet/adjust`.
  → Nếu voucher đã apply trên booking đó:
    - VIETRIDE_FUNDED: rollback VOUCHER_VIETRIDE_FUNDED_CREDIT entry
      (INSERT entry mới amount = -discountAmount, entryType=ADJUSTMENT, note="Refund voucher credit").
    - OPERATOR_FUNDED: chỉ rollback VoucherUsage record (không có balance change vì entry audit amount=0).

Ai trigger REFUNDED:
  - Hủy vé chủ động (6.2): Booking Service deduct wallet → chờ confirm từ Wallet Service
    → khi WalletCredited event về → set REFUNDED
  - Operator hủy chuyến: Trip CANCELLED → Booking Service cancel + refund hàng loạt
    → mỗi booking về REFUNDED sau khi WalletCredited
  - NO_SHOW: KHÔNG chuyển REFUNDED (không có refund)
  - EXPIRED khi chưa capture: KHÔNG chuyển REFUNDED (seat release only). Capture được xác minh sau
    expiry phải đi qua compensation về ví như rule authoritative bên dưới.

BookingStatus machine (đầy đủ):
  PENDING_PAYMENT → CONFIRMED → COMPLETED
                  ↘ EXPIRED   (authoritative payment deadline)
                  CONFIRMED → CANCELLED → REFUNDED  (hủy chủ động + tiền về)
                  CONFIRMED → NO_SHOW               (không hoàn)
                  CONFIRMED → PARTIAL_NO_SHOW → COMPLETED  (một phần passenger no-show, không hoàn)
                  CONFIRMED → CANCELLED              (operator hủy chuyến TRƯỚC IN_PROGRESS — sau đó → REFUNDED khi wallet credited)
                  CONFIRMED → DISRUPTED → REFUNDED   (Trip IN_PROGRESS bị gián đoạn không thay xe — refund proportional)
```

**Authoritative VNPay deadline, late IPN và Booking compensation (ratified 2026-07-31):**

- Mỗi VNPay Payment dùng `effectiveDueAt = dueAt ?? createdAt + 15 phút`. `dueAt` persisted là
  authoritative khi có; fallback 15 phút chỉ dành cho Payment cũ thiếu deadline.
- Booking one-way truyền nguyên `expiresAt` của Trip seat lock vào `Payment.dueAt`. Round-trip dùng
  deadline sớm hơn của hai leg. Cấu hình VNPay timeout 15 phút không kéo dài seat lock 10 phút.
  Parcel tiếp tục truyền deadline nghiệp vụ của chính flow; vì vậy final-payment window 30 phút
  không được expire ở phút 15.
- `effectiveDueAt <= now` là hết hạn. Expiry phải là CAS atomic trên `PENDING_REDIRECT`; transition
  và `payment.payment.expired` Outbox cùng transaction.
- IPN xác minh signature, merchant, amount, transaction status và signed `vnp_PayDate` trước khi
  mutate. `paidAt` từ `vnp_PayDate` là thời điểm tài chính authoritative, kể cả callback đến sau khi
  job đã chuyển local Payment sang `EXPIRED`.
- Booking đã `EXPIRED`, `paidAt >= effectiveDueAt`, hoặc không còn confirm được seat thì không được
  hồi sinh. Khoản capture phải hoàn idempotent về PassengerWallet, một allocation cho mỗi Booking.
  Lỗi Trip tạm thời (timeout/network/5xx) phải retry; không expire/refund cho tới khi có outcome
  definitive.
- Booking phát `booking.payment_refund.requested` trong cùng transaction với terminal transition:

```json
{
  "eventId": "uuid",
  "occurredAt": "date-time",
  "paymentId": "uuid",
  "paymentReferenceType": "BOOKING|BOOKING_GROUP",
  "paymentReferenceId": "uuid",
  "bookingId": "uuid",
  "userId": "uuid",
  "amount": 350000,
  "reason": "PAYMENT_CAPTURE_AFTER_BOOKING_EXPIRY|SEAT_CONFIRMATION_FAILED"
}
```

Payment không tin `userId`/`amount` từ event; phải revalidate captured VNPay Payment, owner, original
reference và exact net allocation từ immutable trusted context. One-way Payment chỉ thành
`REFUNDED` sau exact refund. `BOOKING_GROUP` chỉ thành `REFUNDED` khi mọi allocation có tổng
`BOOKING_REFUND` đúng exact net amount; partial refund lịch sử không được tính là hoàn đủ.

**Resumable VNPay history (ratified 2026-07-31):**

Booking và Passenger history có nullable root field `paymentRedirectUrl`, luôn serialize. Chỉ
Payment attempt mới nhất theo `(createdAt DESC, id DESC)` của requested reference được xét; không
fallback URL cũ. URL chỉ hợp lệ khi đúng owner, exact domain amount, trusted context, method
`VNPAY`, status `PENDING_REDIRECT`, persisted `dueAt > now`, và absolute HTTPS authority trùng
VNPay base URI cấu hình. Payment lookup unavailable/malformed phải fail open thành `null`, không
làm hỏng base history.

### 6.6 Giao nhận hàng ký gửi (Parcel Delivery)

**Mô hình cân nặng + deposit + cân lại:**

**a) Tải trọng khoang xe:**
- Operator khai báo `Vehicle.maxCargoWeightKg` và `Vehicle.maxCargoVolumeM3` (nullable, có thể chỉ dùng weight) khi đăng ký xe.
- Khi passenger mua vé: hệ thống ước tính **dự phòng hành lý passenger** (`Trip.estimatedPassengerLuggageKg`). Phần còn lại = capacity available cho parcel.
- Available parcel capacity = `Vehicle.maxCargoWeightKg - estimatedPassengerLuggage - sum(reservedParcelWeight chưa hủy)`.

**`estimatedPassengerLuggageKg` config hierarchy:**

Giá trị `Trip.estimatedPassengerLuggageKg` được **tính snapshot khi Hangfire/manual generate Trip** theo công thức:

```
kgPerSeat = VehicleType.estimatedPassengerLuggageKgPerSeat  // override per loại xe (nếu có)
         ?? Operator.luggagePolicy.defaultLuggageKgPerSeat   // operator default (JSONB)
         ?? 10                                                // platform default fallback

Trip.estimatedPassengerLuggageKg = kgPerSeat × Vehicle.totalSeats
```

**Config locations:**

| Layer | Field | Mục đích | Override priority |
|---|---|---|---|
| Platform default | hardcode = 10 kg/seat | Fallback khi mọi config khác null | Lowest |
| **Operator** | `Operator.luggagePolicy` JSONB `{ defaultLuggageKgPerSeat: number }` | Nhà xe set policy chung cho mọi loại xe của họ | Medium |
| **VehicleType (mới)** | `VehicleType.estimatedPassengerLuggageKgPerSeat int nullable` | Override per loại xe (SLEEPER_BUS thường nhiều hành lý hơn STANDARD_BUS) | Highest |

**VehicleType entity (mới):**

Thay vì hardcode enum `STANDARD_BUS | LIMOUSINE | SLEEPER_BUS` ở code, v6 promote thành entity riêng cho phép operator/admin config:

```
VehicleType {
  id UUID
  code string UNIQUE             // "STANDARD_BUS", "LIMOUSINE", "SLEEPER_BUS" — kế thừa enum cũ
  displayName string             // "Xe ghế ngồi tiêu chuẩn", "Limousine 9 chỗ", "Xe giường nằm"
  estimatedPassengerLuggageKgPerSeat int nullable  // override Operator.luggagePolicy
  defaultSeatCount int nullable  // gợi ý khi operator tạo Vehicle mới
  isSystemDefined boolean        // true cho 3 type platform seed; false cho custom type operator tạo
  isActive boolean
  timestamps
}
```

`Vehicle.vehicleTypeId FK → VehicleType`. Seed migration (full spec) tạo 3 record default:

| code | displayName | estimatedPassengerLuggageKgPerSeat | defaultSeatCount | isSystemDefined |
|---|---|---|---|---|
| `STANDARD_BUS` | Xe ghế ngồi tiêu chuẩn | 10 | 45 | true |
| `LIMOUSINE` | Limousine | 15 | 9 | true |
| `SLEEPER_BUS` | Xe giường nằm | 20 | 40 | true |

Operator có thể tạo custom type (vd "Bus 45 chỗ VIP") nhưng KHÔNG xóa được 3 type system (`isSystemDefined=true` block delete).

**Snapshot timing:**
- Khi Hangfire `generate Trip từ DriverSchedule` HOẶC manual `POST /v1/operator/trips`:
  - Tính `Trip.estimatedPassengerLuggageKg` theo công thức trên + Vehicle gắn với Trip
  - Lưu vào Trip entity (snapshot immutable sau khi tạo Trip)
- Nếu sau đó operator đổi `Operator.luggagePolicy` hoặc `VehicleType.estimatedPassengerLuggageKgPerSeat` → **Trip đã tồn tại giữ snapshot cũ**. Trip mới generate sau update dùng giá trị mới.
- Nếu Vehicle Substitution (xe khác thay thế): tính lại `Trip_new.estimatedPassengerLuggageKg` theo vehicle mới.

**b) Báo giá, cọc và giữ tải — Settlement policy v2:**

```text
dimWeightKg = lengthCm × widthCm × heightCm / 6000
chargeableWeightKg = max(weightKg, dimWeightKg)
grossPriceVnd = max(minimumPriceVnd,
                    round(chargeableWeightKg × pricePerKgVnd,
                          MidpointRounding.AwayFromZero))

estimatedTotalPriceVnd = estimatedGrossPriceVnd
                       - min(discountAmountVnd, estimatedGrossPriceVnd)
depositRequiredVnd = round(estimatedTotalPriceVnd × 20%,
                           MidpointRounding.AwayFromZero)
```

- Không làm tròn kg và không floor/ceil số tiền theo 1.000 đồng. Ví dụ `3,2 kg × 1.000đ/kg = 3.200đ`.
- Operator cấu hình `pricePerKgVnd` và `minimumPriceVnd`; Passenger/Assistant không được nhập giá.
- Parcel thường được tạo ở `PENDING_PAYMENT`. `EXTRA_LARGE` vào `PENDING_OPERATOR_REVIEW`; chỉ sau khi duyệt mới vào `PENDING_PAYMENT`.
- `POST /v1/parcels/{parcelId}/deposit-payment` tạo payment và giữ mềm cargo theo số đo ước tính trong 15 phút. Payment hết hạn chuyển `EXPIRED`, release hold và không consume voucher.
- Payment cọc có `paidAt < dueAt` luôn được công nhận dù callback đến sau event timeout. Nếu chuyến không còn phục vụ được thì Parcel chuyển `CANCELLED` và hoàn toàn bộ khoản đã thu.
- Cọc v2 cố định 20%. Nếu Passenger không check-in trước `latestCheckInAt`, Parcel chuyển `REJECTED`, mất toàn bộ cọc và release cargo.

**c) Check-in, cân thực tế và thanh toán số dư:**

- Assistant đúng trip/operator check-in trước `latestCheckInAt`, chuyển `RESERVED → CHECKED_IN`, sau đó nhập kích thước và cân nặng thực tế.
- Backend tự derive size và tính lại `finalGrossPriceVnd`; không nhận size hay giá cuối authoritative từ client.
- Voucher được clamp lần nữa ở giá cuối:

```text
finalTotalPriceVnd = finalGrossPriceVnd
                   - min(discountAmountVnd, finalGrossPriceVnd)
balanceRequiredVnd = max(0, finalTotalPriceVnd - depositPaidVnd)
refundDueVnd = max(0, depositPaidVnd - finalTotalPriceVnd)
```

- Tolerance không miễn thanh toán. Nếu `balanceRequiredVnd > 0`, Parcel vào `PENDING_FINAL_PAYMENT`; nếu đã đủ tiền, Parcel vào `READY_TO_LOAD`.
- Khi `refundDueVnd > 0`, Parcel vẫn vào `READY_TO_LOAD` và enqueue refund idempotent qua Outbox; refund không chặn load.
- `finalPaymentDeadline = min(reweighedAt + finalPaymentTimeoutMinutes, loadCutoffAt)`. Payment chỉ hợp lệ khi `paidAt < finalPaymentDeadline`.
- Timeout số dư chuyển `PENDING_FINAL_PAYMENT → REJECTED`, ghi `forfeitedDepositVnd = depositPaidVnd` và release cargo. Nếu callback đến sau timeout nhưng chứng minh đã trả đúng hạn, hệ thống hủy forfeiture và chuyển `READY_TO_LOAD` nếu còn phục vụ được; nếu không còn phục vụ được thì `CANCELLED` và hoàn toàn bộ tiền đã thu.
- Chỉ `READY_TO_LOAD` được chuyển sang `LOADED`.

**d) Delivery — giao tại bến hoặc Stop dọc tuyến:**
- Parcel giao đến **bến đích (destination station)** hoặc **Stop dọc tuyến** thuộc RouteStop của trip. KHÔNG trung chuyển parcel đến địa chỉ nhà.
- Người nhận tự ra bến/Stop để nhận hàng. Phụ xe bàn giao tại đó.
- **Sender chọn `dropoffStopId` khi tạo parcel** — null = giao tại destination station mặc định; not null = giao tại Stop đó (phải thuộc RouteStop của trip). UX: tương tự Booking dropoff (single source of truth = RouteStop).
- Cùng giá `ParcelRouteFare {routeId, sizeCategory}` cho mọi điểm xuống (giống dropoff passenger free at any stop).

**e) Trip cargo capacity — ownership theo settlement v2:**

Trip có 2 counter cargo phân biệt rõ vai trò:

| Counter | Ý nghĩa | Add | Remove |
|---|---|---|---|
| `Trip.reservedParcelWeightKg` | Tổng weight cargo đang được giữ cho Parcel chưa load. Dùng tính **available capacity cho parcel mới** | Khi deposit payment tạo soft hold; sau deposit success giữ nguyên reservation | Khi payment expire/fail, check-in/final-payment timeout, terminal status hoặc transfer out |
| `Trip.totalLoadedWeightKg` | Tổng weight parcel **đang vật lý trên xe**. Dùng alert "khoang gần đầy ≥80%" cho operator | Khi parcel LOADED (vật lý lên xe) | Khi parcel UNLOADED hoặc RETURN_INITIATED (vật lý rời xe) |

**Available capacity formula** (dùng khi user tạo parcel mới hoặc khi xem `GET /parcels/available-trips`):
```
availableCargo = Vehicle.maxCargoWeightKg
                 - Trip.estimatedPassengerLuggageKg
                 - Trip.reservedParcelWeightKg
                 - Trip.totalLoadedWeightKg
```

Volume capacity uses the same formula with
`maxCargoVolumeM3 - reservedParcelVolumeM3 - totalLoadedVolumeM3`; passenger luggage has no
volume term in v1.

Parcel và Trip là hai database độc lập nên không có transaction cross-service. Mỗi service phải CAS/idempotent trong database của mình; Parcel dùng stable `Idempotency-Key` cho mutation cargo và lưu trạng thái recovery khi mutation Trip thất bại.

**`reservedParcelWeightKg` events:**

```
ADD reservedParcelWeightKg/Volume theo cargo ước tính khi:
  1. Bắt đầu deposit payment → soft hold trong 15 phút.
     Payment success chuyển PENDING_PAYMENT → RESERVED nhưng không cộng lần hai.
  2. Atomic cargo transfer với `targetState=RESERVED` khi
     PENDING_OPERATOR_ACTION → RESERVED trên Trip mới.

REMEASURE reservation ước tính → thực tế khi:
  - CHECKED_IN được cân lại. Task settlement sở hữu mutation này.
  - Capacity fail → PENDING_OPERATOR_ACTION + pendingActionResumeStatus;
    operator override thành công mới resume settlement.

REMOVE reservation theo giá trị hiện đang giữ khi:
  1. PENDING_PAYMENT → EXPIRED (deposit payment fail/expire)
  2. RESERVED → REJECTED (quá latestCheckInAt)
  3. PENDING_FINAL_PAYMENT → REJECTED (quá finalPaymentDeadline)
  4. Parcel → CANCELLED trước khi load
  4. PENDING_OPERATOR_ACTION → RETURNED (operator trả hàng tại bến)
  5. PENDING_OPERATOR_ACTION → RESERVED (newTripId) — atomic transfer REMOVE old + ADD new
  6. PENDING_TRANSFER_CONFIRM → LOADED trên Trip_new — atomic transfer REMOVE old + ADD loaded new
  7. TRANSFER_ESCALATED → RETURNED — REMOVE từ Trip_old
  8. IN_TRANSIT → UNLOADED (parcel vật lý rời xe, capacity freed)
     (kể cả khi flow normal — về clean state, không nợ counter)
```

**`totalLoadedWeightKg` events (giữ nguyên, không đổi):**

```
ADD totalLoadedWeightKg/Volume theo cargo thực tế khi:
  - READY_TO_LOAD → LOADED (Assistant scan QR / explicit confirm tại bến)
  - Atomic cargo transfer với `targetState=LOADED` khi assigned target crew confirm
    PENDING_TRANSFER_CONFIRM → LOADED trên Trip mới

REMOVE totalLoadedWeightKg/Volume theo đúng frozen weight/volume của authoritative
`TripCargoParcel` ledger row (không đọc lại estimated fields mutable từ Parcel) khi:
  - LOADED/IN_TRANSIT → UNLOADED (Assistant dỡ tại bến đích hoặc Stop dropoff)
  - DELIVERY_REJECTED → RETURN_INITIATED (sau 15 phút undo window, hàng rời xe)
  - PENDING_OPERATOR_ACTION/TRANSFER_ESCALATED → RETURNED (hàng chính thức rời custody)
  - Atomic transfer sang Trip khác (REMOVE source + ADD target trong một Trip-local transaction)
```

> **Ranh giới ownership:** Task check-in/reweigh chỉ sở hữu estimated reservation → actual reservation. Task load chỉ sở hữu actual reservation → loaded cargo. Cả hai dùng CAS ở Parcel và mutation idempotent ở Trip để retry không cộng counter hai lần.
>
> **Atomic transfer contract:** Parcel gọi
> `POST /internal/v1/trips/{sourceTripId}/cargo/transfer` với exact body
> `{parcelId,targetTripId,targetState:"RESERVED"|"LOADED",allowCapacityOverflow}` và UUID-v4
> Idempotency-Key. Trip lock source/target theo UUID sort order, recheck một cargo ledger row của
> Parcel, release source và reserve/load target trong cùng local transaction. `RESERVED` luôn
> enforce capacity; `LOADED` chỉ cho overflow khi caller gửi `allowCapacityOverflow=true` cho
> Vehicle Substitution recovery. Parcel chỉ commit state/tripId sau HTTP success.
>
> **Confirm/timeout race:** crew confirm phải CAS một durable
> `transferConfirmationClaimId/ClaimedAt/ClaimedByUserId` trước external call, và chỉ được claim
> khi `now < transferRequestedAt + 30m`. Timeout tại equality chỉ CAS row chưa có claim. Trip call
> dùng persisted claim id làm Idempotency-Key và exact target
> `LOADED,allowCapacityOverflow=true`. Unknown transport outcome giữ claim để retry/recovery replay
> cùng key; stale-claim recovery hoàn tất Parcel sau khi Trip idempotently xác nhận cargo. Vì vậy
> crash giữa hai service không để cargo ở target trong khi Parcel bị timeout-escalate.

> **Day-32 operator recovery operation:** `PENDING_OPERATOR_ACTION` transfer and
> `PENDING_OPERATOR_ACTION|TRANSFER_ESCALATED` return use a separate durable
> `ParcelCargoRecoveryOperation`; they never reuse the Vehicle Substitution confirmation claim.
> The operation id is the caller UUID-v4 `Idempotency-Key` and is persisted before any Trip HTTP
> mutation. At most one `PENDING` operation exists per Parcel. It freezes
> `{operationType,sourceTripId,targetTripId,targetState,actorUserId,reason,refundAmountVnd,
> refundDueVnd,sourceStatus}` so a retry does not recompute money or target state.
>
> Trip success is followed by one Parcel-local transaction that CAS-completes the Parcel,
> marks the operation `COMPLETED`, and writes Outbox/stat side effects. A definitive Trip
> `404|409|422` marks the operation `FAILED` without changing Parcel or cargo. An unknown
> transport/`503` outcome keeps the operation `PENDING`; a recurring five-minute recovery job
> replays the exact persisted operation id and payload. This closes both crash windows and makes
> concurrent transfer-versus-return attempts resolve to exactly one active operation.

**Use cases (cách parcel được tạo):**

Hành khách tạo yêu cầu gửi hàng theo chuyến. Có **2 use case** đều được hỗ trợ trong v1:

**Use case A — Gửi hàng kèm chuyến đã đặt vé (passenger đi cùng hàng):**
- Trong chi tiết Booking, passenger bấm "Thêm hàng ký gửi" → chọn size + nhập info người nhận → submit
- Passenger lên xe cùng hàng tại pickup stop của booking

**Use case B — Gửi hàng không đi cùng (parcel-only, không có booking):**
- Passenger App có tab riêng "Gửi hàng" → nhập origin/destination/ngày → call **`GET /parcels/available-trips`** (endpoint riêng tách khỏi `/trips/search`)
- Endpoint trả về danh sách Trip còn cargo capacity theo full formula
  `maxCargoWeightKg - estimatedPassengerLuggageKg - reservedParcelWeightKg - totalLoadedWeightKg >= estimatedWeightKg`,
  filter theo route + ngày
- Passenger chọn chuyến → nhập info hàng + người nhận → thanh toán → hệ thống flag parcel này không có Booking liên quan, người gửi mang hàng đến bến xuất phát trước giờ khởi hành
- Không cần passenger có Booking trên chuyến đó

**`GET /parcels/available-trips` — search criteria spec:**
```
Query params:
  originCity    string (bắt buộc)       — tên tỉnh/thành phố gửi, ví dụ "Hồ Chí Minh"
  destCity      string (bắt buộc)       — tỉnh/thành phố nhận
  date          date ISO 8601 (bắt buộc)— ngày khởi hành
  weightKg      decimal (optional)      — trọng lượng ước tính để filter cargo capacity

Flow search:
  1. User nhập tên tỉnh/thành phố (text input với autocomplete city list)
  2. Backend query Station WHERE city/province ILIKE :originCity → trả list Station tại khu vực đó
     (cùng lúc hoặc lazy load tùy UX)
  3. User chọn Station cụ thể (ví dụ: "Bến xe Miền Đông" hoặc "Bến xe An Sương")
     → FE update query với stationId cụ thể
  4. Backend filter Trip:
       Route.originStationId IN (stations của originCity)
       Route.destinationStationId IN (stations của destCity)
       Trip.departureDateTime::date = :date
       Trip.status IN (SCHEDULED, BOARDING)
       Trip.maxCargoWeightKg
         - Trip.estimatedPassengerLuggageKg
         - Trip.reservedParcelWeightKg
         - Trip.totalLoadedWeightKg >= :weightKg (nếu có)
  5. Response: list Trip với { tripId, routeName, operatorName, departureDateTime,
       originStationName, destinationStationName, availableCargoKg, parcelFares: { SMALL, MEDIUM, LARGE } }

Lý do search theo city trước rồi đề xuất Station:
  Người gửi thường biết "gửi từ Sài Gòn đi Hà Nội" nhưng không biết chính xác
  bến nào — hệ thống liệt kê các bến đang có chuyến để user chọn phù hợp.
  Nhất quán với cách `/trips/search` hoạt động cho passenger booking.
```

**Lý do tách endpoint `/parcels/available-trips` thay vì dùng chung `/trips/search`:**
- Filter logic khác (cargo capacity vs seat availability)
- UX khác (không hiện sơ đồ ghế, không cho chọn pickup stop dọc tuyến — parcel chỉ giao bến đích)
- Permission khác (có thể mở rộng v2: parcel-only cho phép guest không cần account, search-trip cần auth)

**Channels khác:**
- **Operator Dashboard chỉ MONITOR parcel:** xem danh sách parcel của chuyến công ty mình, theo dõi trạng thái, review EXTRA_LARGE. Operator KHÔNG tạo parcel thay người dùng (consistency với no walk-in booking)
- Phụ xe xác nhận hàng đã nhận lên xe khi gặp người gửi tại bến. Khi đến điểm giao, phụ xe xác nhận "Đã giao hàng" trong app

**Operator parcel detail — contact info scope:**
Operator (cả OPERATOR_STAFF và OPERATOR_ADMIN) xem được **full contact info** của cả sender và recipient trong parcel detail:
- Sender: name, phone (để liên hệ khi có vấn đề tại bến gửi)
- Recipient: name, phone, email (để liên hệ khi PENDING_OPERATOR_ACTION, DELIVERY_REJECTED, cần giao lại)
- Lý do: Operator là bên chịu trách nhiệm vận chuyển hàng — không có contact info thì không thể xử lý exception case (hàng trên xe bị hủy chuyến, giao không thành). Đây là thông tin vận hành bắt buộc.
- **Không expose** contact info sender/recipient cho DRIVER/ASSISTANT qua Driver App (chỉ thấy name để đối chiếu hàng). DRIVER/ASSISTANT cần liên hệ thì thông qua Operator.

> **⚠️ Scoping note — bỏ walk-in parcel:** Người gửi BẮT BUỘC phải có Passenger account VietRide để tạo parcel request. Người gửi vãng lai không app → từ chối nhận hàng (tham khảo người gửi cài app + tạo account). Nhất quán với quyết định bỏ walk-in booking ở mục 4.3.

**Thông tin parcel request gồm:** tên/mô tả hàng, kích thước ba chiều, trọng lượng ước tính (kg), thông tin người gửi (tên, SĐT), thông tin người nhận (tên, SĐT, email), chuyến đi được gán, voucher optional và **phương thức nhận hàng (delivery method)**. Parcel Service generate `parcelCode`, derive `estimatedSizeCategory` và tính giá; client KHÔNG tự gửi code, size hay giá authoritative.

**QR code spec — hàng ký gửi:**
```
Parcel QR encode: plain string = parcelCode (ví dụ "VR-PCL-20260728-P7K3D9Q2")
  → KHÔNG encode parcelId UUID, JSON, token, hay encrypted payload
  → `parcelCode` format hiện tại: VR-PCL-yyyyMMdd-XXXXXXXX
     - VR-PCL = VietRide Parcel prefix, tách biệt với bookingCode/ticketCode
     - yyyyMMdd = ngày tạo parcel theo Asia/Bangkok
     - XXXXXXXX = 8 ký tự uppercase random/base32, unique trong DB
  → Backend vẫn nhận legacy VRP-yyyyMMdd-XXXXXXXX khi resolve QR cho dữ liệu cũ
  → Parcel Service lưu `parcelCode` string unique + indexed
  → Passenger App/email render QR động từ parcelCode, KHÔNG lưu ảnh QR trong DB
  → Assistant App scan QR, decode ra parcelCode và gọi
    POST /v1/assistant/trips/{tripId}/parcels/qr-scan
  → Parcel Service kiểm tra Assistant assignment + operator tenant trước khi resolve
  → QR scan chỉ đọc và trả parcelId/current status; KHÔNG tự check-in/load/unload/deliver
  → Assistant App gọi mutation endpoint tương ứng sau bước xác nhận của người dùng

Nếu scan ra code không thuộc trip/operator hiện tại:
  → không update status
  → trả `PARCEL_NOT_FOUND`; mã sai format trả `VALIDATION_ERROR`

Lý do dùng parcelCode thay vì parcelId UUID: dễ đọc khi support/manual search,
không expose UUID nội bộ, và nhất quán với booking QR dùng plain bookingCode.
```

**Size category derive từ chargeable weight — 4 mức:**

| Category | Mô tả thực tế | `chargeableWeightKg` |
|---|---|---|
| `SMALL` | Túi xách, hộp nhỏ — vừa để chân hoặc khoang trên | ≤ 5 kg |
| `MEDIUM` | Thùng carton vừa, vali nhỏ | ≤ 15 kg |
| `LARGE` | Thùng lớn, vali to | ≤ 30 kg |
| `EXTRA_LARGE` | Đồ cồng kềnh — cần operator review thủ công trước khi nhận (xem flow bên dưới) | > 30 kg |

`chargeableWeightKg = max(weightKg, lengthCm × widthCm × heightCm / 6000)`. Passenger và Assistant nhập số đo; backend derive category. Các field request `sizeCategory`/`actualSizeCategory` cũ chỉ được chấp nhận tạm thời để tương thích client và không được dùng làm nguồn authoritative.

> **`transportCompanyId`** không cần là field riêng trên Parcel — nhà xe đã được xác định **implicit qua `tripId`** (trip thuộc operator nào thì parcel cũng thuộc operator đó). Agent không cần tạo foreign key riêng.

**Guard — block tạo parcel trên trip đang IN_PROGRESS:**
`POST /v1/parcels` validate `Trip.status` trước khi tạo:
- `Trip.status = SCHEDULED | BOARDING` → allow (xe chưa chạy, hàng vẫn có thể load)
- `Trip.status = IN_PROGRESS` → **block** với error `TRIP_NOT_ACCEPTING_PARCEL`
  ("Chuyến xe đã khởi hành. Vui lòng chọn chuyến khác.")
- `Trip.status = COMPLETED | DISRUPTED | CANCELLED` → block với `BOOKING_TRIP_NOT_BOOKABLE`

**Deadline trước giờ đóng tải:**

```text
depositPaymentTimeoutMinutes = 15
checkInCloseMinutes = 30
finalPaymentTimeoutMinutes = 30
minimumFinalPaymentWindowMinutes = 10
loadCutoffMinutes = 10

loadCutoffAt = departureAt - loadCutoffMinutes
latestCheckInAt = min(departureAt - checkInCloseMinutes,
                      loadCutoffAt - minimumFinalPaymentWindowMinutes)
finalPaymentDeadline = min(reweighedAt + finalPaymentTimeoutMinutes,
                           loadCutoffAt)
```

Boundary là strict: check-in/payment phải xảy ra trước deadline. `settlementPolicyVersion` đã được dùng là immutable; thay đổi policy phải tạo version mới.

**EXTRA_LARGE approval flow:**

```
Backend derive estimatedSizeCategory = EXTRA_LARGE
  → Parcel status = PENDING_OPERATOR_REVIEW (chưa chuyển PENDING_PAYMENT/RESERVED)
  → Chưa charge phí, chưa khóa khoang hàng
  → Publish event ParcelReviewRequested → Notification Service alert operator
  → Operator dashboard hiển thị queue "Parcel chờ review"

Operator review trên dashboard:
  - APPROVE → Parcel status = PENDING_PAYMENT, publish `parcel.parcel.review_approved`, alert passenger "Đã duyệt — vui lòng thanh toán"; Passenger gọi deposit-payment để chọn Wallet/VNPay và tạo soft hold
  - REJECT → Parcel status = REJECTED, ghi rejectionReason, publish `parcel.parcel.rejected` với sender + immutable reason, alert passenger "Bị từ chối: <lý do>", không charge

Timeout 24 giờ không review:
  → CANCELLED với reason OPERATOR_REVIEW_TIMEOUT
  → Publish `parcel.parcel.cancelled` cho sender; không dùng `parcel.parcel.auto_rejected`
  → Passenger có thể tạo lại request mới hoặc liên hệ operator
```

**Status field bổ sung:**
- `reviewedAt` datetime nullable — khi operator quyết định
- `reviewedByUserId` FK nullable — operator nào duyệt
- `reviewDecision` enum: PENDING | APPROVED | REJECTED (chỉ dùng khi sizeCategory = EXTRA_LARGE)

Size khác (SMALL/MEDIUM/LARGE) **bỏ qua** flow review, vào `PENDING_PAYMENT`; deposit success đúng hạn chuyển `RESERVED`.

**LOADED → IN_TRANSIT trigger:**
```
Khi Trip.status chuyển sang IN_PROGRESS (chuyến bắt đầu khởi hành):
  → Trip-Route-Vehicle Service publish event TripStarted { tripId }
  → Parcel Service consume TripStarted:
      UPDATE Parcel SET status = IN_TRANSIT
      WHERE tripId = :tripId AND status = LOADED
```

**IN_TRANSIT → UNLOADED trigger (dỡ hàng tại bến đích hoặc Stop dọc tuyến):**
```
Precondition: TripStop.status = ARRIVED tại điểm xuống của parcel:
  - Nếu Parcel.dropoffStopId IS NULL → check TripStop tại destination station (terminal)
  - Nếu Parcel.dropoffStopId IS NOT NULL → check TripStop tại Stop đó
  (chỉ khi xe đã thực sự đến đúng stop mới enable nút dỡ hàng trong Driver App)

Cơ chế chính — Assistant thao tác trong Driver App:
  Option A (recommended): Scan QR của parcel → confirm "Đã dỡ xuống"
  Option B: Không có QR → chọn parcel từ danh sách → tap "Đã dỡ hàng" + xác nhận

→ Parcel CAS `IN_TRANSIT → UNLOADED`, rồi gọi Trip-owned idempotent cargo release bằng stable
  UUID-v4 key. Trip lấy weight/volume frozen từ `TripCargoParcel`, không nhận hoặc trừ lại
  estimated mutable fields từ Parcel. Một ledger row chỉ được release một lần; retry là no-op.

Operator override (exception handling only):
  → Operator bấm "Override — Đánh dấu đã dỡ" trên dashboard
  → PHẢI nhập lý do (reason text bắt buộc)
  → Hành động được ghi vào AuditLog: { userId: operatorId, action: PARCEL_UNLOAD_OVERRIDE,
      metadata: { parcelId, tripId, reason } }
  → Không dùng override cho bulk action — phải per-parcel
```

**Parcel behavior khi Trip CANCELLED (SCHEDULED hoặc BOARDING):**

Trigger: Parcel Service consume canonical
`trip.trip.cancelled {eventId,occurredAt,tripId,operatorId,cancelledAt,cancelReason}`.
Preview và execution dùng chung một classifier, tenant isolation và idempotency:

- Trước load:
  `PENDING_OPERATOR_REVIEW|PENDING_PAYMENT|PENDING|PENDING_ADDITIONAL_PAYMENT|RESERVED|CHECKED_IN|PENDING_FINAL_PAYMENT|READY_TO_LOAD`
  → `CANCELLED`, release cargo ledger đang active và refund đúng số tiền thực thu còn lại
  `max(depositPaidVnd + balancePaidVnd - refundedAmountVnd, 0)`.
- Đã nhận hàng vật lý: `LOADED|IN_TRANSIT`
  → `PENDING_OPERATOR_ACTION`; chưa refund và chưa release cargo vì kiện hàng vẫn nằm trên xe/bến.
- Terminal hoặc event replay → no-op; không phát lại refund/cargo mutation.

Operator manual cancel (`POST /v1/operator/parcels/{parcelId}/cancel`) hỗ trợ toàn bộ trạng thái
trước load ở trên, tenant-safe và UUID-v4 idempotent. `refundChoice=FULL|POLICY|NO` (legacy
`*_REFUND` aliases chỉ để compatibility). Với
`outstanding=max(depositPaidVnd + balancePaidVnd - refundedAmountVnd,0)`:

```
FULL   = outstanding
NO     = 0
POLICY = clamp(
           round(outstanding * (100 - noShowFeePercent) / 100,
                 MidpointRounding.AwayFromZero),
           0,
           outstanding)
```

`parcelNoShowPolicy=null` dùng default `noShowFeePercent=0`. Giá trị phải hữu hạn trong `[0,100]`;
policy JSON malformed/out-of-range là dependency/config failure `503 UPSTREAM_UNAVAILABLE`, không
commit cargo/state/refund. Không floor tiền theo 1.000 VND. Mọi trạng thái pre-load hợp lệ chuyển
`CANCELLED`; `REJECTED` chỉ dành cho review/timeout flow riêng, không phải manual cancellation.
Cargo release phải thành công trước khi commit trạng thái/refund fact.

Operator xử lý `PENDING_OPERATOR_ACTION`:

1. Trả hàng: Trip release cargo cũ; sau khi thành công Parcel chuyển `RETURNED` và publish
   `parcel.refund.initiated` cho đúng outstanding amount còn lại.
2. Chuyển chuyến: Parcel gọi duy nhất
   `POST /internal/v1/trips/{sourceTripId}/cargo/transfer` với
   `{parcelId,targetTripId,targetState:"RESERVED",allowCapacityOverflow:false}`. Trip lock source
   và target theo UUID tăng dần, release source + reserve target trong cùng local transaction.
   Chỉ sau response thành công Parcel mới đổi `tripId` và
   `PENDING_OPERATOR_ACTION → RESERVED`.

Both choices first claim a `ParcelCargoRecoveryOperation` using the public UUID-v4
`Idempotency-Key`. Return freezes the outstanding refund from the authoritative Parcel row before
calling Trip release; transfer freezes the source/target pair before calling atomic cargo
transfer. A second return/transfer cannot claim while another operation is `PENDING`. Unknown Trip
outcomes are replayed after five minutes with the same operation id; no retry may mint a new Trip
key for the active operation.

Hangfire escalation job (check mỗi 15 phút):
  SELECT Parcel WHERE status = PENDING_OPERATOR_ACTION
    AND updatedAt < now - interval '2 hours'
  → Publish replay-safe `parcel.parcel.pending_operator_action_realerted`
  → Re-alert operator; không auto-change status hoặc release/refund.

**Parcel chưa check-in hoặc chưa thanh toán số dư đúng hạn:**
```
Hangfire job (chạy mỗi 5 phút):
  - RESERVED và latestCheckInAt <= now
      → REJECTED/CHECK_IN_TIMEOUT
      → forfeitedDepositVnd = depositPaidVnd
      → release cargo
  - PENDING_FINAL_PAYMENT và finalPaymentDeadline <= now
      → REJECTED/FINAL_PAYMENT_TIMEOUT
      → forfeitedDepositVnd = depositPaidVnd
      → release cargo

Khi cân lại tạo `PENDING_FINAL_PAYMENT`, Parcel publish `parcel.parcel.final_payment_requested`
trong cùng transaction để sender nhận số dư phải trả và deadline authoritative.

Payment có paidAt trước deadline luôn thắng về nghiệp vụ. Nếu callback đến sau timeout, Parcel hủy
forfeiture rồi READY_TO_LOAD khi trip vẫn SCHEDULED/BOARDING, chưa qua loadCutoffAt và reserve cargo
thành công; nếu không còn phục vụ được thì CANCELLED và refund toàn bộ tiền đã thu.
Sau recovery, Parcel publish `parcel.parcel.settlement_recovered` để đính chính notification timeout
đã có thể được gửi trước đó; payload nêu trạng thái phục hồi và số tiền hoàn nếu kết quả là CANCELLED.
```

**Delivery method — chỉ 1 loại:**

| Loại | Mô tả |
|---|---|
| **Giao tại bến đích (Terminal Pickup)** | Người nhận tự đến bến xe đích để lấy hàng. Phụ xe bàn giao tại bến, không giao tận nơi. Đây là cách vận hành tiêu chuẩn của xe khách liên tỉnh Việt Nam |

> **Quyết định thiết kế:** Không có door-to-door parcel delivery — xe khách không thể di chuyển linh hoạt. Field `deliveryMethod` vẫn lưu trong DB với giá trị `TERMINAL_PICKUP` mặc định, mở rộng v2 nếu cần loại delivery khác (vd door-to-door).
>
> v1 hỗ trợ giao hàng tại Stop dọc tuyến thuộc RouteStop của trip (giống Booking dropoff). Reasoning: (1) `ParcelRouteFare` giữ nguyên giá theo route — không cần fare matrix, parcel "lock space" toàn chuyến giống ghế (operator chấp nhận cost); (2) UNLOADED flow tại stop dọc tuyến tái dùng cơ chế Assistant bấm "Đã đến [stop]" có sẵn (6.11) — TripStop.status = ARRIVED tại `Parcel.dropoffStopId` enable nút dỡ hàng cho parcel này; (3) Tracking Service tiếp tục broadcast cho parcel sender/recipient tới khi rời room — không cần state mới. Agent **PHẢI implement `Parcel.dropoffStopId` nullable** trong v1.

**Luồng xác nhận người nhận (email link — không dùng SMS):**

> **Design decision — email link thay OTP nhận hàng:** v1 dùng email link + `deliveryToken` 48h vì người nhận có thể không cài app, không có tài khoản VietRide, và chỉ cần browser để xác nhận. OTP phù hợp auth/login hơn; với parcel delivery, signed email link đơn giản hơn, ít bước hơn, vẫn audit được qua token hash + IP + timestamp.

```
Phụ xe bấm "Đã giao hàng"
  → Nếu có recipient email, hệ thống generate fresh deliveryToken UUID v4 cho lần issuance;
    mỗi Parcel chỉ có tối đa một token row active
  → Chuẩn hóa UUID dạng `D` lowercase, lưu duy nhất SHA-256 hex trong
    `parcel_delivery_tokens` với `expiresAt = now + 48h`; raw token chỉ tồn tại trong memory
  → Parcel gọi Notification `POST /internal/v1/emails` bằng Internal JWT + UUID-v4
    Idempotency-Key bằng token-row id để gửi email đến địa chỉ người nhận:
      Tiêu đề: "Hàng của bạn đã được giao - Xác nhận nhận hàng"
      Nội dung: link dạng
      {PUBLIC_APP_URL}/parcels/delivery/confirm?token=<url-encoded-uuid>
      Link có hiệu lực 48 giờ
  → Chỉ commit token row + Parcel transition sau khi Notification trả `202 Accepted`
  → Nếu không có recipient email, transition vẫn vào `DELIVERED_PENDING_CONFIRM` nhưng không tạo
    token; operator hoặc assigned Driver/Assistant phải dùng manual-confirm có audit note
  → Người nhận mở link trên browser (không cần cài app)
  → Trang xác nhận hiển thị: tên hàng, người gửi, mô tả
  → Người nhận bấm "Xác nhận đã nhận" hoặc "Từ chối nhận"
```

**Trường hợp "Từ chối nhận" hoặc hết hạn link:**
- Người nhận từ chối → parcel status chuyển sang `DELIVERY_REJECTED` → Notification Service alert operator → operator liên hệ xử lý thủ công (trả hàng hoặc giao lại)
- Link hết hạn 48 giờ → phụ xe hoặc operator có thể resend email từ dashboard (tạo token mới, token cũ bị revoke)
- Người nhận bấm nhầm "Từ chối" → trong 15 phút đầu còn có thể đổi ý (undo), sau đó cần liên hệ nhà xe

**Public delivery endpoints (không JWT; UUID-v4 Idempotency-Key required):**

```
POST /v1/parcels/delivery/confirm
  body { token }
  200 { parcelId, status:"DELIVERY_CONFIRMED", confirmedAt }

POST /v1/parcels/delivery/reject
  body { token, rejectionReason }       // trim 1..500
  200 { parcelId, status:"DELIVERY_REJECTED", rejectedAt, canUndoUntil }

POST /v1/parcels/delivery/undo-reject
  body { token }
  200 { parcelId, status:"DELIVERED_PENDING_CONFIRM", undoneAt }

Token tự nó là proof of authority. Không có combined `action=ACCEPT|REJECT` body. Retained
`POST /v1/assistant/parcels/{id}/confirm-delivery` và
`POST /v1/operator/parcels/{id}/confirm-delivery` là authenticated manual-confirm aliases; chúng
không thay thế ba public endpoint. New assigned-crew alias là
`POST /v1/crew/parcels/{id}/manual-confirm`.

Error responses:
  400 PARCEL_DELIVERY_TOKEN_INVALID
  400 PARCEL_DELIVERY_TOKEN_EXPIRED
  400 PARCEL_DELIVERY_TOKEN_REVOKED
  400 PARCEL_NOT_PENDING_CONFIRM
  422 VALIDATION_ERROR

Validation steps (BE):
  1. Parse UUID v4, normalize dạng `D` lowercase, SHA-256 UTF-8 → 64 lowercase hex
  2. SELECT ParcelDeliveryToken WHERE tokenHash = :hash
  3. Check expiresAt > now — nếu sai return TOKEN_EXPIRED
  4. Check revokedAt IS NULL — nếu set return TOKEN_REVOKED
  5. Check Parcel.status = DELIVERED_PENDING_CONFIRM — nếu khác return NOT_PENDING_CONFIRM
  6. Process endpoint:
     confirm → UPDATE status = DELIVERY_CONFIRMED, confirmedAt = now, confirmedByIp = req.ip
     reject → UPDATE status = DELIVERY_REJECTED, rejectedAt = now,
              rejectionReason = body.rejectionReason
              (Hangfire job sau 15 phút check → RETURN_INITIATED nếu không đổi ý)
     undo-reject → chỉ trong 15-minute window, restore DELIVERED_PENDING_CONFIRM
  7. Revoke token row khi flow không còn cho phép recipient action; publish
     ParcelDeliveryConfirmed / ParcelDeliveryRejected → Notification Service:
     ACCEPT → push sender "Hàng đã được [recipientName] nhận thành công."
     REJECT → push operator "Người nhận từ chối hàng [parcelCode]. Lý do: [reason]"

Rate limit:
  Redis key parcel:delivery_confirm:{tokenHash}, max 5 attempts / hour per token hash
  (không lưu raw token trong Redis).
  Token là UUID v4 (122 bit entropy) → guess attack không khả thi, chỉ chống automated abuse.

Resend endpoint — `POST /v1/operator/parcels/{parcelId}/resend-delivery-email`:
  Role: OPERATOR_STAFF/ADMIN của operator chuyến đó. Assigned DRIVER/ASSISTANT dùng exact crew
  alias `POST /v1/crew/parcels/{parcelId}/resend-delivery-email`.
  Logic:
    1. Validate Parcel.status = DELIVERED_PENDING_CONFIRM hoặc DELIVERY_REJECTED (chưa quá undo window)
    2. DELIVERY_REJECTED còn trong undo window được restore DELIVERED_PENDING_CONFIRM
    3. UPDATE active ParcelDeliveryToken.revokedAt = now
    4. Generate raw UUID v4 mới; INSERT hash/expiry vào `parcel_delivery_tokens`
    5. Gọi Notification internal email với Idempotency-Key = token-row id; chỉ commit sau `202`
    6. Audit log: {action: PARCEL_DELIVERY_RESEND, metadata: {parcelId, resentBy}}
```

**Timeout behavior — KHÔNG auto-confirm:**

```
Nếu recipient KHÔNG click link trong 48 giờ:
  → Active token row expire (`expiresAt < now`)
  → Parcel.status VẪN giữ DELIVERED_PENDING_CONFIRM (không auto-confirm)
  → KHÔNG tự động chuyển DELIVERY_CONFIRMED (tránh case recipient claim "tôi chưa xác nhận")
  → Operator nhận re-alert sau 7 ngày qua Hangfire job:

Hangfire job (Parcel Service, daily at 9am):
  SELECT Parcel WHERE status = DELIVERED_PENDING_CONFIRM
    AND active ParcelDeliveryToken.expiresAt < now - interval '7 days'
    AND lastReminderAt IS NULL OR lastReminderAt < now - interval '7 days'
  → Push operator notification + alert:
     "Parcel [parcelCode] đã giao 7 ngày trước nhưng người nhận chưa xác nhận.
      Vui lòng liên hệ recipient ([phone]) để xác nhận thủ công hoặc resend email."
  → UPDATE Parcel SET lastReminderAt = now

Operator action options khi receive re-alert:
  (a) Resend email — gọi POST /v1/operator/parcels/{id}/resend-delivery-email
  (b) Manual confirm — gọi POST /v1/operator/parcels/{id}/manual-confirm
      (body `{confirmNote}` trim 1..500; assigned DRIVER/ASSISTANT dùng
      `/v1/crew/parcels/{id}/manual-confirm`)
```

`DELIVERED_PENDING_CONFIRM` không phải source hợp lệ của operational `/return`; tại re-alert chỉ
có resend hoặc manual-confirm. Luồng return Day-32 chỉ nhận
`PENDING_OPERATOR_ACTION|TRANSFER_ESCALATED` và kết thúc ở `RETURNED`.

> **Lý do không auto-confirm:** Auto-confirm sau timeout tạo legal/dispute risk — recipient có thể claim "tôi không xác nhận, hệ thống tự confirm". Pattern đúng là **không có terminal state tự động** khi user inaction — operator can thiệp thủ công và để lại audit trail.

**Undo-reject mechanic (15 phút window):**
```
Người nhận bấm "Từ chối":
  → Parcel: status = DELIVERY_REJECTED, rejectedAt = now
  → KHÔNG trigger RETURN_INITIATED ngay — đợi 15 phút
  → Link xác nhận vẫn giữ nguyên valid token trong 15 phút

Hangfire job (check mỗi 1 phút):
  SELECT parcel WHERE status = DELIVERY_REJECTED
    AND rejectedAt < now - interval '15 minutes'
    AND status != RETURN_INITIATED
  → UPDATE status = RETURN_INITIATED
  → Publish event ParcelReturnInitiated → alert operator

Nếu người nhận đổi ý trong 15 phút:
  → Gọi explicit `POST /v1/parcels/delivery/undo-reject` bằng token còn hiệu lực
  → Parcel CAS `DELIVERY_REJECTED → DELIVERED_PENDING_CONFIRM`, clear rejection metadata
  → Sau đó người nhận mới gọi confirm bằng cùng link/token
  → Hangfire job thấy status không còn DELIVERY_REJECTED → skip
```

**Parcel entity — business requirements:**

- **Ảnh hàng:** `photoUrl` string nullable — tối đa một URL Firebase Storage. Người gửi có thể upload ảnh hàng khi tạo parcel request (để phụ xe đối chiếu khi nhận hàng). Optional, không bắt buộc. Client upload trực tiếp lên Firebase Storage, BE nhận URL string đã trim; URL phải là HTTPS, tối đa 2.048 ký tự và thuộc bucket cấu hình qua `FIREBASE_STORAGE_BUCKET`. Firebase Storage Rules giới hạn tối đa 5 MB và MIME `image/jpeg | image/png | image/webp`; Parcel Service không nhận hoặc kiểm tra file bytes. `photoUrl` được expose trong Parcel detail và danh sách parcel của Assistant đúng chuyến, không đưa vào RabbitMQ event.
- **Ảnh bằng chứng vận hành:** `checkInPhotoUrls` và `deliveryPhotoUrls` là hai mảng JSONB nullable, mỗi mảng tối đa 3 URL. Assistant dùng upload purpose `PARCEL_EVIDENCE_PHOTO`, upload trực tiếp vào `parcel-ops/{operatorId}/{assistantUserId}/{parcelId}/...`; Parcel validate HTTPS, bucket và owned path rồi lưu ảnh atomically cùng transition check-in/deliver. Body ảnh optional để giữ tương thích v1; ảnh chỉ expose qua Parcel detail cho caller đã có quyền xem Parcel và không đưa vào RabbitMQ event.
- **Phân loại hàng:** `estimatedSizeCategory` và `actualSizeCategory` (SMALL | MEDIUM | LARGE | EXTRA_LARGE) do backend derive từ chargeable weight; lưu riêng estimated/actual dimensions, weight, volume, dim weight và chargeable weight.
- **Tiền settlement v2:** `estimatedGrossPriceVnd`, `finalGrossPriceVnd`, `discountAmountVnd`, `estimatedTotalPriceVnd`, `finalTotalPriceVnd`, `depositPercent`, `depositRequiredVnd`, `depositPaidVnd`, `balanceRequiredVnd`, `balancePaidVnd`, `refundDueVnd`, `refundedAmountVnd`, `forfeitedDepositVnd`. Tất cả tiền là BIGINT VND; fractional calculation làm tròn gần nhất với `MidpointRounding.AwayFromZero`.
- **Payment/deadline:** `depositPaymentId`, `balancePaymentId`, `finalPaymentDeadline`, `checkedInAt`, `checkedInByUserId`, `reweighedAt`, `reweighedByUserId`. Deposit expiry authoritative theo `Payment.dueAt`; không cần duplicate deadline cọc trên Parcel.
- **Snapshot:** `pricePerKgVnd`, `minimumPriceVnd`, `dimWeightFactor`, `settlementPolicyVersion`. Version đã được sử dụng không được update; policy mới phải tạo version mới.
- **Operational recovery:** `pendingActionResumeStatus` xác định state cần resume sau capacity override/recovery; không tạo status column thứ hai.
- **Thông tin người gửi:** `senderUserId` FK → User (NOT NULL — người gửi PHẢI có tài khoản VietRide, consistent với no walk-in parcel). Dùng để authorize tracking, query "hàng tôi đã gửi" (`GET /v1/parcels/sent`), và cho Tracking Service verify parcel sender khi joinTripTracking.
- **Thông tin người nhận:** name (bắt buộc), phone (bắt buộc), email (**optional**). `recipientUserId` nullable link tới User — set khi người gửi nhập email và Identity Service lookup ra account hiện có. null = recipient không có tài khoản VietRide.
- **Điểm xuống hàng:** `dropoffStopId` nullable FK → Stop. Null = giao tại destination station mặc định (terminal). Not null = giao tại Stop dọc tuyến. Validation:
  - `Parcel.dropoffStopId` phải nằm trong RouteStop của route gắn với trip; nếu không return `STOP_NOT_FOUND`.
  - Stop đó phải có `allowDropoff = true`; nếu false return `STOP_NOT_DROPOFF_ALLOWED`.
  - Pricing không đổi — cùng `ParcelRouteFare` cho mọi điểm xuống (parcel lock cargo space toàn chuyến, giống Booking lock seat).

  **Email optional — hybrid delivery confirmation:**
  ```
  Trường hợp A — Người nhận CÓ email:
    → Sau khi UNLOADED → DELIVERED_PENDING_CONFIRM:
        Generate raw deliveryToken trong memory, persist SHA-256 hash/expiry row và gọi
        Notification internal email
    → Người nhận bấm link → DELIVERY_CONFIRMED hoặc DELIVERY_REJECTED

  Trường hợp B — Người nhận KHÔNG có email (email = null):
    → UNLOADED → DELIVERED_PENDING_CONFIRM như bình thường
    → KHÔNG gửi email (không có địa chỉ)
    → Assistant/Operator xác nhận thủ công trong Driver App / Operator Dashboard:
        Nút "Xác nhận đã giao (thủ công)" — enable khi Parcel.recipientEmail = null hoặc khi
        recipient có email nhưng token đã hết hạn/không phản hồi và crew/operator đã xác minh ngoài hệ thống
        Bắt buộc nhập lý do / ghi chú giao hàng (ví dụ "Đã giao cho người thân tại bến")
        → UPDATE Parcel SET status = DELIVERY_CONFIRMED, confirmedAt = now,
             confirmedByUserId = assistantId/operatorId, confirmNote = <text>
        → Ghi AuditLog: { action: PARCEL_MANUAL_CONFIRM, metadata: { parcelId, confirmNote } }
    → Người gửi nhận push notification: "Hàng của bạn đã được giao thành công."

  Trường hợp C — Người nhận có email nhưng KHÔNG xác nhận (không bấm link, link hết hạn):
    → Phụ xe hoặc operator có thể resend email (tạo token mới)
    → Hoặc dùng manual confirm như Trường hợp B nếu xác nhận qua điện thoại với người nhận

  Parcel entity:
    recipientEmail: string nullable (thay vì bắt buộc)
    recipientPhone: string NOT NULL (luôn bắt buộc — phụ xe gọi điện xác nhận)
    recipientName:  string NOT NULL
  ```
- **Delivery confirmation:** `ParcelDeliveryToken` history có
  `{id,parcelId,tokenHash,expiresAt,revokedAt,issuedByUserId,issueReason,createdAt,updatedAt}`.
  `tokenHash` là SHA-256 hex lowercase 64 ký tự; raw UUID không nằm trên `Parcel`, Outbox hoặc
  log. `issueReason=INITIAL_DELIVERY|RESEND|MIGRATION_BACKFILL`; unique token hash và chỉ một row
  chưa revoke per Parcel.
- **Transfer fields:** `transferTargetTripId`, `transferRequestedAt`, `transferConfirmedAt`,
  `transferConfirmedByUserId`, `transferConfirmationClaimId`,
  `transferConfirmationClaimedAt`, `transferConfirmationClaimedByUserId` nullable — dùng khi
  VehicleSubstitution hoặc DISRUPTED không có xe thay thế nhưng operator chuyển hàng sang trip
  khác. Chỉ Driver/Assistant của target trip được confirm; durable claim loại race với timeout và
  là recovery identity cho atomic Trip cargo transfer.
- **Return fields:** `returnReason`, `returnedAt`, `returnedByUserId` nullable — bắt buộc set khi status chuyển `RETURNED` từ `PENDING_OPERATOR_ACTION` hoặc `TRANSFER_ESCALATED`; ghi kèm `AuditLog` để đối soát.
- **Cargo recovery history:** `ParcelCargoRecoveryOperation` stores a UUID-v4 operation id,
  Parcel/operator, `TRANSFER|RETURN`, `PENDING|COMPLETED|FAILED`, source/target Trip,
  target state, actor, reason, frozen refund facts, source status, claim/completion timestamps and
  an optional failure code. A partial unique index permits at most one `PENDING` row per Parcel.
- **Status lifecycle:** xem ParcelStatus enum ở section 8.
- **Timestamps audit:** `confirmedAt`, `rejectedAt`, `rejectionReason` (optional free text), `confirmedByIp` (audit), `reviewedAt`/`reviewedByUserId`/`reviewDecision` (chỉ EXTRA_LARGE), `unloadedAt` nullable (cho cargo weight tracking — xem section 8).

**Parcel pricing — Operator set per route:**

Parcel có tính phí. Operator define bảng giá theo route + size category. Cần entity `ParcelRouteFare` với composite key (routeId + sizeCategory), giá BIGINT VND. Ví dụ tuyến SG→HN: SMALL 50k, MEDIUM 150k, LARGE 300k, EXTRA_LARGE để TBC (operator set sau khi approve).

**Payment flow cho parcel:**
- Parcel payment hỗ trợ **cả Wallet và VNPay trực tiếp** (hoàn thiện như Booking, không bắt user top-up ví trước).
- SMALL/MEDIUM/LARGE:
  1. Passenger submit parcel request + `paymentMethod = WALLET | VNPAY`
  2. Parcel Service tạo Parcel status = `PENDING_PAYMENT`
  3. Gọi Payment Service `/charge { referenceType: "PARCEL", parcelId, amount, method }`
  4. Nếu Wallet success → Payment `SUCCEEDED`, Parcel → `PENDING`
  5. Nếu VNPay → Payment `PENDING_REDIRECT`, Parcel giữ `PENDING_PAYMENT`, response trả `paymentRedirectUrl`
  6. VNPay callback success → Payment Service publish `PaymentSucceeded { referenceType=PARCEL, parcelId }` → Parcel Service update Parcel → `PENDING`
  7. VNPay fail hoặc tới authoritative payment deadline → Payment `FAILED/EXPIRED`, Parcel →
     `EXPIRED` (chưa nhận hàng, chưa refund)
- EXTRA_LARGE:
  1. Tạo parcel ban đầu status = `PENDING_OPERATOR_REVIEW`, chưa charge
  2. Operator APPROVE → chuyển sang `PENDING_PAYMENT`, rồi chạy cùng payment flow Wallet/VNPay như trên
  3. Operator REJECT trước payment → Parcel `REJECTED`, không charge
- Hoàn tiền: nếu parcel đã thanh toán rồi bị CANCELLED/REJECTED/DELIVERY_REJECTED/RETURNED theo policy → refund về Ví VietRide mặc định. VNPay Refund API vẫn là v2; v1 hoàn qua Wallet để đơn giản vận hành.

> >
> **Revenue entries (Parcel earning revenue):**
> Payment Service consume `payment.payment.succeeded` cho deposit/final payment:
> → INSERT PlatformWalletTransaction CREDIT đúng số tiền Payment đã thu và OperatorLedgerEntry audit tương ứng. Parcel chỉ cộng `depositPaidVnd` hoặc `balancePaidVnd` khi `paidAt` nằm trước deadline của purpose đó.
> → Balance capture quá hạn không được cộng vào Parcel; Payment Service sở hữu ledger và refund khoản late capture.
>
> **Refund entries (cross-ref đầy đủ cho mọi refund path):**
>
> Payment Service consume các event refund parcel:
> - Giá cuối giảm: `parcel.refund.initiated` với `amount=refundDueVnd`; refund không block `READY_TO_LOAD`.
> - Trip/system CANCELLED trước load: refund toàn bộ `depositPaidVnd + balancePaidVnd - refundedAmountVnd`.
> - CHECK_IN_TIMEOUT/FINAL_PAYMENT_TIMEOUT: không refund cọc; ghi `forfeitedDepositVnd=depositPaidVnd`, không cộng forfeiture lần hai vào collected amount.
> - PENDING_OPERATOR_ACTION → RETURNED (operator trả hàng tại bến): INSERT PlatformWalletTransaction DEBIT + OperatorLedgerEntry { entryType=PARCEL_REFUND, tripId, amount = -max(depositPaidVnd + balancePaidVnd - refundedAmountVnd, 0), note="Parcel returned at terminal after trip cancel/disrupt" } atomic với Wallet credit.
> - TRANSFER_ESCALATED → RETURNED: cùng pattern (refund 100% nếu parcel rời khỏi VietRide custody).
> - DELIVERY_REJECTED → RETURN_INITIATED (recipient từ chối + 15p undo): refund logic phụ thuộc operator (out of scope ledger v1 — operator settle thủ công với sender; có thể adjust ledger qua System Admin endpoint nếu cần).
> - EXTRA_LARGE → REJECTED bởi operator review: chưa charge (status từ PENDING_OPERATOR_REVIEW chưa qua PENDING_PAYMENT) → KHÔNG có ledger entry để rollback.
>
> Refund ledger entries là **audit-only** — KHÔNG update OperatorWallet ngay. PlatformWallet DEBIT phản ánh tiền rời holding pool để hoàn về PassengerWallet. Nếu refund xảy ra trước Trip terminal (phổ biến) → operator wallet chưa được credit cho trip này, không cần debit. Nếu refund xảy ra sau khi `OperatorTripSettlement` đã SETTLED (late refund, rare) → Admin manual debit wallet qua `POST /v1/admin/operators/{id}/wallet/adjust` và ghi PlatformWallet adjustment tương ứng. Voucher rollback (nếu parcel apply voucher v2): tương tự booking voucher rollback pattern.

`POST /v1/parcels` response khi cần thanh toán:
```
{
  parcelId,
  parcelCode,
  status,              // PENDING nếu Wallet success, PENDING_PAYMENT nếu VNPay redirect
  totalAmount,
  paymentRedirectUrl?  // chỉ có khi paymentMethod = VNPAY
}
```

**Operator setup:** Khi tạo Route, Operator configure ParcelRouteFare cho từng size. Nếu chưa config → parcel request trên route đó sẽ hiển thị error "Nhà xe chưa cấu hình giá hàng ký gửi cho tuyến này".

> **Lý do chọn email link thay SMS:** Email miễn phí hoàn toàn (SendGrid free 100/ngày), không cần cài app, chỉ cần browser. SMS Gateway (Twilio, ESMS.vn) tốn phí và phức tạp cấu hình. Zalo OA ZNS không có free tier production. Pattern email link phổ biến (tương tự xác nhận đơn hàng Shopee/Lazada).

### 6.7 Thông báo (Notifications)

Push notification qua Firebase FCM đến mobile app, đồng thời lưu vào notification history in-app.

**Opt-out scope:**
- v1: User KHÔNG opt-out được — mọi notification trong system là transactional (booking, payment, tracking, parcel, schedule/route change). Không có notification marketing/promotional cần opt-out.
- v2: Nếu thêm promotional notification (voucher mới, khuyến mãi cuối tuần) → thêm entity `NotificationPreference { userId, type, enabled }` + UI setting.

**Durability strategy:**

```
Inbound: RabbitMQ event consume
  → Notification Service nhận event với at-least-once guarantee từ RabbitMQ
  → INSERT Notification record (in-app history) trong DB transaction
  → ENQUEUE vào BullMQ `fcm-push` queue → ACK RabbitMQ message

Outbound: BullMQ → FCM
  → BullMQ worker pull job → gọi Firebase Admin SDK FCM API
  → Retry policy: 5s → 30s → 5m → DLQ (sau 3 lần fail)
  → INSERT NotificationDelivery { notificationId, fcmToken, status, retryCount, lastError, sentAt }
  → FCM trả `UNREGISTERED` / `INVALID_ARGUMENT` → cleanup UserDevice (set isActive=false)
  → DLQ → Sentry alert + manual investigation (rare)

Trade-off:
  - In-app history luôn được lưu (DB write trước khi enqueue BullMQ)
  - Push FCM = best-effort delivery (DLQ rare, mất 1-2 push chấp nhận được)
  - User mở app vẫn thấy notification trong list (in-app history) dù miss push
  - KHÔNG cần Outbox cho Notification Service — RabbitMQ at-least-once + BullMQ retry + NotificationDelivery audit đã đủ
```

Lý do không dùng Outbox: Outbox phù hợp khi service **publish** event quan trọng (booking confirmed, payment succeeded). Notification Service chỉ **consume** event và side-effect là push FCM — mất push không hỏng data integrity. Notification database (in-app history) đã được persist trong cùng DB transaction với BullMQ enqueue (atomic). Thêm Outbox = tăng complexity cho marginal benefit.

**Thông báo hành khách nhận:**

| Trigger | Nội dung |
|---|---|
| Đặt vé thành công | Xác nhận booking + mã vé |
| Hủy vé | Thông báo hủy + số tiền hoàn về ví |
| Xe sắp đến điểm đón | "Xe của bạn sẽ đến điểm đón trong X phút" |
| Thay đổi lộ trình | Thông báo lộ trình mới + yêu cầu xác nhận |
| Hủy chuyến từ nhà xe | Thông báo hủy + thông tin hoàn tiền |
| Thay đổi giờ khởi hành | Giờ mới |
| Trạng thái hàng ký gửi | Đã nhận lên xe / Đang giao / Đã giao / Từ chối |

**Thông báo tài xế/phụ xe nhận:**

| Trigger | Nội dung |
|---|---|
| Phân công chuyến mới | Thông tin chuyến, giờ, xe |
| Thay đổi lịch trình / lộ trình | Chi tiết thay đổi từ operator |
| Hành khách chưa lên xe | Cảnh báo khi xe sắp rời điểm dừng còn người chưa tick |
| Xe lệch tuyến alert | Hệ thống phát hiện lệch lộ trình |

**Thông báo operator nhận:**

| Trigger | Nội dung |
|---|---|
| Xe lệch tuyến | GPS phát hiện xe ra khỏi lộ trình kế hoạch |
| Xe trễ giờ (delayed) | Xe đến điểm dừng trễ hơn ETA kế hoạch quá ngưỡng (configurable, mặc định 30 phút) |
| Khoang hàng gần đầy | Tổng tải trọng parcel đã loaded đạt ≥ ngưỡng cảnh báo (configurable, mặc định 80% maxCargoWeightKg) |
| Tài xế báo sự cố | Driver bấm "Báo sự cố" trong app (category + description + photo, xem 4.2) — payload kèm category, description, ảnh, GPS, tripId. Operator quyết định action tiếp theo (alternative route / vehicle sub / cancel). |

### 6.8 RAG AI Assistant

Một service AI thống nhất phục vụ nhiều đối tượng, phân quyền theo role. **Passenger RAG/CSKH là in-scope v1** — RAG không còn là internal-only. Mỗi role chỉ query được các category tài liệu phù hợp. Mọi câu trả lời kèm trích dẫn nguồn tài liệu (tên document, section).

| Đối tượng | Giao diện | Truy cập nội dung |
|---|---|---|
| **Hành khách** | Chatbot CSKH trong Passenger App | FAQ, chính sách đặt/hủy vé, quy trình hàng ký gửi |
| **Tài xế / Phụ xe** | Tab hỗ trợ trong Driver App | FAQ, thông tin chuyến, quy trình vận hành cơ bản |
| **Operator Staff/Admin** | Sidebar trong Operator Web | Toàn bộ quy trình nội bộ, chính sách tuyến, hướng dẫn hệ thống |
| **System Admin** | Admin Web | Toàn bộ knowledge base không giới hạn |

Lịch sử chat lưu lại per session để audit. System Admin phê duyệt tài liệu trước khi đưa vào knowledge base.

**Access level semantics:**
- `PUBLIC`: FAQ/chính sách dành cho passenger, ví dụ đặt/hủy vé, hoàn tiền, hàng ký gửi, tracking. Passenger chỉ được query tài liệu `PUBLIC`.
- `OPERATOR`: quy trình vận hành nội bộ cho Driver/Assistant/Operator Staff/Admin.
- `ADMIN`: tài liệu quản trị platform, cấu hình hệ thống, audit/RAG operations; chỉ System Admin query.

**RAG Service — entity requirements (PostgreSQL + pgvector):**

- **KnowledgeDocument:** file gốc upload (PDF, DOCX, TXT, MARKDOWN). Fields cần có: title, description (nullable), fileUrl (Firebase Storage signed URL), fileType enum, `accessLevel` enum (PUBLIC | OPERATOR | ADMIN — quyết định role nào query được; `PUBLIC` là FAQ/chính sách passenger in-scope v1), status enum (PENDING_REVIEW | APPROVED | REJECTED | ARCHIVED), uploadedByUserId, approvedByUserId nullable, approvedAt nullable, timestamps.
- **KnowledgeChunk:** đoạn text được tách từ document (unit retrieval). Fields: documentId FK, chunkIndex (0-indexed), content (text), tokenCount (kiểm soát context window), `embedding` **vector(1536)** dùng pgvector (OpenAI text-embedding-3-small). Cần pgvector index `USING ivfflat (embedding vector_cosine_ops)` cho similarity search performance.
- **RagConversation:** 1 session chat. Fields: userId, role enum (PASSENGER | DRIVER | ASSISTANT | OPERATOR_STAFF | OPERATOR_ADMIN | SYSTEM_ADMIN — để filter accessLevel khi query chunk), startedAt, lastMessageAt.
- **RagMessage:** từng turn trong conversation. Fields: conversationId FK, role enum (USER | ASSISTANT), content (text), `citedChunkIds` UUID[] (audit trail — các chunk dùng để generate câu trả lời), tokensUsed nullable.

**Access control filter trong retrieval query:**
- `PASSENGER` → chỉ `PUBLIC`
- `DRIVER` / `ASSISTANT` / `OPERATOR_STAFF` / `OPERATOR_ADMIN` → `PUBLIC` + `OPERATOR`
- `SYSTEM_ADMIN` → `PUBLIC` + `OPERATOR` + `ADMIN`

Passenger không query được tài liệu OPERATOR/ADMIN. Driver/Assistant không query được tài liệu ADMIN.

**RAG Ingest Pipeline — flow từ file upload đến vector store:**

```
1. System Admin upload file lên Admin Web
   → POST /rag/documents { title, description, accessLevel } + file binary
   → RAG Service:
       - Upload file lên Firebase Storage → lấy fileUrl
       - INSERT KnowledgeDocument { status = PENDING_REVIEW }
       - Return documentId

2. System Admin review và approve (hoặc auto-approve nếu System Admin tự upload)
   → PUT /rag/documents/{id}/approve
   → UPDATE KnowledgeDocument status = APPROVED
   → Ghi local work item `rag.document.ingest_requested` trong cùng RAG database transaction

   `rag.document.ingest_requested` là lệnh xử lý nội bộ của RAG, không publish lên RabbitMQ và
   không tạo Notification. v1 không có `rag.document.approved` integration event vì chỉ
   System Admin quản lý tài liệu và không cần tự nhận thông báo cho thao tác duyệt.

3. Background ingest worker trong RAG Service, triggered by local `rag.document.ingest_requested`:
   a. Download file từ Firebase Storage
   b. Extract text:
        PDF → pdf-parse library (Node.js)
        DOCX → mammoth library
        TXT/MARKDOWN → đọc trực tiếp
   c. Chunk text: chia đoạn ~500 token, overlap 50 token giữa các chunk
        (overlap để tránh mất context ở ranh giới chunk)
   d. FOR EACH chunk:
        - Gọi OpenAI Embedding API model `text-embedding-3-small` → vector 1536 chiều
          (Anthropic không có standalone embedding API; chọn OpenAI vì Voyage AI và Anthropic-recommended provider cost cao hơn cho scale capstone.
           `text-embedding-3-small` rẻ ~5x so với ada-002, dimension giống = 1536 nên schema không đổi.)
        - INSERT KnowledgeChunk { documentId, chunkIndex, content, tokenCount, embedding }
   e. UPDATE KnowledgeDocument: đánh dấu embedding complete

4. Query flow (khi user hỏi chatbot):
   a. User message → embed bằng cùng model → query vector
   b. pgvector similarity search: SELECT chunk ORDER BY embedding <=> queryVector LIMIT 5
      WHERE document.accessLevel IN (roles_allowed_for_user)
   c. Build context prompt: [system prompt] + [top-5 chunks] + [conversation history] + [user question]
   d. Call LLM API (claude-sonnet-4-6) với stream = true → SSE response về client
   e. INSERT RagMessage { role=USER }, INSERT RagMessage { role=ASSISTANT, citedChunkIds=[...] }
```

> **Firebase Storage access control cho RAG docs:** File upload lưu trong path `rag/documents/{documentId}/{filename}`. Signed URL TTL 1 giờ — generated khi Admin cần preview, không expose trực tiếp. Client KHÔNG có access trực tiếp vào Firebase Storage bucket — chỉ nhận content qua RAG query API.

### 6.9 Saga Pattern — Booking Flow (Hybrid: Sync core + Async side-effects)

Booking flow chạm nhiều service (Booking, Trip-Route-Vehicle, Payment, Wallet, Notification). Không thể dùng 1 DB transaction global → dùng **Saga pattern**: chuỗi bước có compensation khi fail.

**Decision: Hybrid pattern**
- **Sync (HTTP REST) cho core path:** Booking → Trip lock seat → Payment charge — vì cần response ngay cho user "đặt vé thành công"
- **Async (RabbitMQ event) cho side-effects:** Notification, Wallet log, Analytics — không cần user chờ

**Happy path:**

```
1. Client POST /bookings { tripId, seats: [5,6,7], voucherId, paymentMethod }
   → Gateway forward Booking Service (kèm Internal JWT)

2. Booking Service (sync):
   a. Validate request (5 ghế max, voucher hợp lệ)
   b. HTTP POST Trip-Route-Vehicle /trips/{tripId}/lock-seats
      → Trip-Route-Vehicle:
        - Check TripSeat status AVAILABLE cho ghế [5,6,7]
        - SET status = HELD, redisKey seat_lock:{tripId}:{seatNum} TTL 10 phút
        - Return seatLockToken
      → fail (ghế đã hết): return error → Booking return error cho user, END
   c. Tính totalAmount (Booking capture `pricingAt` đúng một lần ở đầu handler, truyền cùng giá trị
      vào Trip và reuse cho toàn bộ request):
      - pickupStationId != null (terminal pickup) → dùng Trip.baseFare
      - pickupStopId != null (along-route pickup) → dùng `fareFromThisStop` đã được Trip resolve
        trong snapshot tại `pricingAt`; Booking không tự query/re-resolve TripStopFare
      - Explicit-pricing precedence tại Trip: persisted TripStopFare chỉ authoritative khi
        `source=MANUAL_OVERRIDE` → active RouteStopFareTemplate half-open tại `pricingAt` → Trip.baseFare
      - Legacy `TEMPLATE_SNAPSHOT` vẫn readable, non-authoritative khi request có `pricingAt`, và chỉ
        tham gia resolution cho legacy compatibility khi omitted `pricingAt`; Day 22 không tạo snapshot row mới
      Nếu có voucherCode (validate voucher applicability + consent):
        1. SELECT Voucher WHERE code = :voucherCode AND isActive AND validFrom <= now < validUntil
        2. Check Voucher.applicableOperatorIds: null hoặc contains Trip.operatorId; sai → VOUCHER_NOT_APPLICABLE
        3. Check Voucher.applicableRouteIds: null hoặc contains Trip.routeId; sai → VOUCHER_NOT_APPLICABLE
        4. Check usage limits (totalUsageLimit, perUserLimit per User) qua VoucherUsage count
        5. Check minOrderAmount <= preDiscountTotal; sai → VOUCHER_MIN_ORDER_NOT_MET
        6. **Voucher.fundingType = OPERATOR_FUNDED:**
           SELECT OperatorVoucherConsent WHERE voucherId = :voucherId AND operatorId = Trip.operatorId;
           - Không có record HOẶC status != ACCEPTED → return error VOUCHER_NOT_APPLICABLE
             ("Voucher hiện không khả dụng cho nhà xe này" — không reveal opt-in mechanics cho passenger)
           - status = ACCEPTED → continue apply
           - Voucher.fundingType = VIETRIDE_FUNDED: SKIP consent check (áp global)
        7. Tính discountAmount theo voucher type (PERCENT_OFF / FIXED_AMOUNT) + cap maxDiscountAmount
      Sau đó trừ voucherDiscount, làm tròn đến đồng gần nhất nếu ra số lẻ (BSOT v1.11.0 — không floor 1000)
   d. Tạo Booking record status = PENDING_PAYMENT, tạo 3 Passenger record (chỉ operational: seatNumber + boardingStatus default PENDING)
   e. HTTP POST Payment Service /charge { bookingId, amount, method, dueAt=seatLock.expiresAt }
      → Payment:
        - method = WALLET: deduct wallet balance, log transaction
        - method = VNPAY: tạo VNPay redirect URL, status = PENDING_REDIRECT
      → success → return paymentResult
      → fail (insufficient wallet, VNPay error):
         COMPENSATE → HTTP POST Trip-Route-Vehicle /trips/{tripId}/release-seats
                    → Booking status = CANCELLED, return error cho user, END
   f. Nếu wallet payment: Booking status = CONFIRMED, TripSeat status = BOOKED, persist DB
      Nếu VNPay: Booking status = PENDING_PAYMENT, return redirect URL cho client
   g. Booking publish event BookingConfirmed (qua Outbox pattern — xem dưới)

3. Async consumers (parallel, via RabbitMQ):
   - Notification Service: gửi push "Đặt vé thành công + mã vé"
   - Payment & Wallet Service: WalletTransaction đã được log ở bước 2e (cùng DB transaction với deduct — KHÔNG cần event riêng)
   - Booking Service tự consume `BookingConfirmed` event: UPSERT BookingStats counter table
   - INSERT PlatformWalletTransaction { type=CREDIT, referenceType=BOOKING_PAYMENT_HOLD,
       amount=Booking.totalAmount, referenceId=bookingId }
     atomic với UPDATE PlatformWallet balance (xem 4.6). Nếu payment method = WALLET, đây là movement
     từ PassengerWallet liability sang platform holding pool; nếu VNPAY, đây là tiền VietRide đã thu qua merchant.
   - INSERT OperatorLedgerEntry { entryType=BOOKING_REVENUE, tripId=Booking.tripId, amount=+Booking.totalAmount, ... } — audit-only ledger entry, KHÔNG credit OperatorWallet ngay (xem 4.6 — wallet credit chỉ xảy ra khi `OperatorTripSettlement` SETTLED sau Trip terminal + 7-day hold).
     Nếu voucher applied:
       - VIETRIDE_FUNDED → thêm entry VOUCHER_VIETRIDE_FUNDED_CREDIT (+discountAmount).
       - OPERATOR_FUNDED → thêm entry VOUCHER_OPERATOR_FUNDED_AUDIT (amount=0, audit-only).
```

> Booking Service tự duy trì `BookingStats` counter table (xem entity requirements ở section 8) — UPSERT khi consume các event lifecycle (BookingConfirmed, BookingCancelled, BookingNoShow, BookingCompleted). Operator Dashboard và System Admin KHÔNG query DB trực tiếp; gọi reporting endpoints trên Booking Service qua Gateway: `GET /v1/operator/booking-stats`, `GET /v1/admin/booking-stats/aggregate` (SUM cross-operator). Không có Analytics service riêng.

**VNPay callback flow:**

> **Design decision — tách IPN và ReturnUrl:** VNPay có 2 cơ chế hoàn toàn khác nhau: `vnp_IpnUrl` (server-to-server, VNPay gọi BE sau khi thanh toán xong — không có user trong loop, phải return `{"RspCode":"00","Message":"Confirm Success"}`) và `vnp_ReturnUrl` (browser redirect về — chỉ hiển thị kết quả cho user). Dùng chung 1 endpoint là anti-pattern vì signature method, payload và response format khác nhau. IPN endpoint phải được whitelist IP VNPay tại Nginx level (VNPay cung cấp danh sách IP public cố định). ReturnUrl là GET vì browser redirect bằng GET.

```
4a. VNPay server gọi POST /v1/payments/vnpay-ipn (IPN — server-to-server, không có user)
    → Payment Service verify HMAC-SHA512 signature
    → Nếu success: update Payment status = SUCCEEDED, publish event PaymentSucceeded
    → Booking Service nghe event: update Booking → CONFIRMED, TripSeat → BOOKED, publish BookingConfirmed
    → Nếu fail: publish PaymentFailed → Booking compensate (release seat, status = CANCELLED)
    → Return {"RspCode":"00","Message":"Confirm Success"} cho VNPay (bắt buộc theo VNPay spec)
    → Idempotent: nếu Payment đã SUCCEEDED, return {"RspCode":"00","Message":"Confirm Success"} luôn

4b. Browser redirect về HTTPS `https://app.vietride.online/payments/return?vnp_ResponseCode=00&...`
    → HTTPS bridge giữ nguyên query, polling Payment status hiện tại (KHÔNG xử lý business logic)
    → trên Passenger App mở `vietride://payments/return?...`; nếu không mở được thì hiển thị
      success/fail fallback trên web
    → Operator subscription vẫn truyền HTTPS `returnUrl` của Operator Web, không mở app
    → Lưu ý: user có thể tắt browser trước khi redirect xảy ra — đây là lý do IPN (4a) mới là
      nơi xử lý business logic thực sự
```

**Outbox Pattern (đảm bảo event không mất):**

```
Trong DB transaction của Booking Service:
  - INSERT/UPDATE Booking
  - INSERT vào bảng OutboxEvent { eventType, payload, status = PENDING }
  - COMMIT

Background worker (Outbox worker):
  - SELECT OutboxEvent WHERE status = PENDING
  - Publish lên RabbitMQ
  - UPDATE status = PUBLISHED hoặc retry nếu fail

Worker implementation theo service stack:
  - .NET services: BackgroundService/IHostedService poll mỗi 5s
  - NestJS services: BullMQ scheduled job poll mỗi 5s
  - KHÔNG dùng Hangfire cho Outbox polling (Hangfire chỉ dành cho business scheduled jobs)

→ Đảm bảo: nếu DB commit thành công thì event chắc chắn được publish (eventually).
   Tránh case: DB commit OK nhưng RabbitMQ down → event mất.
```

**Compensation events (rollback):**

| Bước fail | Compensation action |
|---|---|
| Seat lock fail | Return error, không tạo Booking |
| Payment fail (wallet/VNPay) | Release seat, set Booking CANCELLED |
| VNPay tới authoritative deadline | Hangfire job: release seat, set Booking EXPIRED |
| User hủy vé chủ động | Release seat, refund wallet (theo policy 6.2) |

### 6.10 Station vs Stop — Entity Model

VietRide tách Station/Stop riêng biệt và thêm mapping OperatorStation:

**Station (Bến xe lớn — đầu/cuối tuyến, canonical cấp platform) — requirements:**
- Tên canonical, slug/code unique, KHÔNG có `operatorId` (một bến vật lý có thể được nhiều nhà xe khai thác)
- Địa chỉ chi tiết (address, city, province) + tọa độ (latitude, longitude)
- `operatingHours` JSON theo định dạng `{"mon": "06:00-22:00", "tue": "...", ...}` — thời gian **local ICT**
- Liên hệ: contactPhone, contactEmail
- `facilities` JSON array nullable — vd `["waiting_room", "parking", "ticket_counter"]`
- `supportsShuttle boolean default false` — Station có hỗ trợ shuttle service hay không (chỉ bến chính lớn). Set per Station bởi Operator hoặc System Admin. Xem section 6.14.
- Soft delete (isActive boolean)

**Stop vs Station — phân biệt shuttle:** Chỉ Station có `supportsShuttle = true` mới là điểm shuttle. Stop KHÔNG có shuttle.

**OperatorStation (nhà xe khai thác Station nào) — requirements:**
- `{ operatorId, stationId }` composite unique — một operator link một Station tối đa 1 lần
- `displayNameOverride` nullable — tên hiển thị riêng nếu nhà xe muốn ghi "Quầy VietRide - Bến xe Miền Đông"
- `counterLocation` nullable — quầy/khu vực đón khách trong bến
- `contactPhone` nullable — hotline riêng của nhà xe tại bến
- `instructions` nullable — hướng dẫn lên xe/gửi hàng tại bến
- `isActive` boolean — operator có đang khai thác bến này không

> **Decision:** Station là địa điểm vật lý canonical cấp platform (ví dụ "Bến xe Miền Đông" chỉ có 1 record). **Operator tự tạo Station — KHÔNG cần System Admin duyệt.** Tránh duplicate qua UI autocomplete (xem 4.3): operator search trước, link Station hiện có nếu match; chỉ tạo mới khi thật sự chưa có. System Admin có role data-quality cleanup (merge duplicate, normalize) — không gatekeep. OperatorStation biểu diễn nhà xe nào dùng bến đó. Search endpoint: `GET /v1/stations/search?q=<text>&lat=&lng=` (public — passenger app cũng dùng để search trip).

**Stop (Điểm dừng dọc tuyến) — requirements:**
- Tên, operatorId, latitude, longitude, address
- `description` nullable — mô tả định danh thực tế ("Trước cây xăng XYZ", "Ngã 4 ABC")
- `replacedByStopId` nullable FK → Stop — operator manual link khi disable Stop (xem Conventions section 8)
- `googlePlaceId` nullable — nếu Stop được tạo từ Google Maps Places suggest, lưu placeId để re-fetch metadata
- `sharedSuggestion` boolean default false — nếu true, Stop này hiển thị trong suggest cross-operator (operator khác có thể chọn tái sử dụng)
- Soft delete (isActive boolean)

**UX tạo Stop (Operator Web):**
- Operator tạo Stop bằng cách **chọn từ Google Maps Places suggest**. Operator nhập text search ("quán cà phê XYZ", "ngã 4 Hàng Xanh"), Google Maps Places API trả về danh sách candidate (mỗi candidate gồm name, address, lat/lng, placeId).
- Operator chọn 1 suggestion → hệ thống tạo Stop entity với coords + name + address + googlePlaceId từ selection.
- Vai trò: Stop là điểm dừng do operator ký với bên thứ ba (quán ăn, điểm dừng, cây xăng) — bên thứ ba đồng ý cho xe ghé đón/trả khách.
- Stop có thể suggest cross-operator (operator khác đang khai thác cùng khu vực có thể chọn lại Stop đã có thay vì tạo duplicate).

**Relationships (mô tả business, không phải DB schema):**

- **Route** thuộc 1 Operator, có Station khởi hành (`originStationId` BẮT BUỘC) + Station đích (`destinationStationId` BẮT BUỘC), self-reference `returnRouteId` nullable (link tuyến chiều về để hỗ trợ DriverSchedule round-trip). Constraint app-layer: operator phải có `OperatorStation.isActive = true` cho cả origin/destination station trước khi tạo route. Có `baseFare BIGINT` (giá terminal → terminal mặc định), `totalDistanceKm`, `estimatedDurationMinutes`. Soft delete.
- **RouteStop** là junction table giữa Route và Stop (chỉ intermediate stops, không bao gồm origin/destination Station): routeId + stopId là composite key, có `orderIndex`, `estimatedDurationFromOriginMinutes`, `distanceFromOriginKm` decimal nullable do Operator nhập thủ công khi cấu hình Route trên Operator Web (dùng cho proportional DISRUPTED refund — xem formula tại 6.12.1). **`allowPickup` boolean default true** + **`allowDropoff` boolean default true** — phân loại vai trò stop (pickup-only / dropoff-only / both). DB CHECK constraint: `allowPickup OR allowDropoff` (ít nhất 1 phải true). Google Maps API không bắt buộc trong v1/capstone; nếu thiếu km thì DISRUPTED refund fallback sang stop-order ratio. Một Stop có thể thuộc nhiều Route với role khác nhau (vd Stop X làm pickup-only ở Route A nhưng dropoff-only ở Route B).
- **Booking** reference pickup/dropoff: 4 FK columns `pickupStationId`/`pickupStopId`/`dropoffStationId`/`dropoffStopId` mutually exclusive. Xem chi tiết note Decision phía dưới.

> Booking dùng **4 cột FK riêng**, mutually exclusive:
> - `pickupStationId` nullable FK → Station và `pickupStopId` nullable FK → Stop — **exactly one not null** (pickup luôn phải có giá trị)
> - `dropoffStationId` nullable FK → Station và `dropoffStopId` nullable FK → Stop — **at most one not null** (cả 2 null = mặc định = terminal đích, không cần lưu explicit)
> - DB-level CHECK constraint enforce 2 quy tắc trên
>
> Lý do: strict FK (DB integrity), JOIN dễ (LEFT JOIN từng Station/Stop riêng), type-safe trong EF Core (4 navigation property riêng). Phương án polymorphic (1 cột + type discriminator) bị loại vì không có FK constraint thực sự ở DB level, phải validate ở app layer.

**Lý do tách Station và Stop:**
1. Station có metadata phong phú (facilities, operating hours), Stop chỉ có name + coords
2. Station có thể được nhiều Operator share (Bến xe Miền Đông có 50+ nhà xe), nên Station là canonical platform record; OperatorStation link nhà xe vào bến đó
3. Stop là operator-private, mỗi nhà xe định nghĩa Stop riêng phù hợp tuyến
4. Báo cáo và UX phân biệt rõ "bến" vs "điểm dừng"

### 6.11 DriverSchedule — Assignment với Round-trip pairing

**Scope:** Entity lưu **assignment** driver/assistant ↔ vehicle ↔ route, lặp lại theo tuần. Dùng để Hangfire generate Trip + truy vấn history các chuyến driver/assistant đã lái. **KHÔNG quản lý ca làm, giờ làm, lương.**

Requirements:
- Thuộc 1 operator, link đến driver (User role=DRIVER), assistant (User role=ASSISTANT, nullable), route, vehicle (nullable). Vì `Trip.vehicleId` bắt buộc, schedule active chưa có vehicle không generate Trip; mỗi attempted date ghi `TripGenerationSkipLog` reason `OTHER` với message no-vehicle cho đến khi được assign.
- **Recurring pattern:** `dayOfWeek` JSON array `[1,3,5]` (1=T2, 2=T3, ..., 7=CN), `departureTime` TIME **local ICT** (operator nhập "08:00" → store "08:00:00")
- **Valid window:** `validFrom` DATE, `validUntil` DATE nullable
- `isActive` boolean — toggle off để dừng generate Trip mới mà không xóa schedule

**Pattern round-trip qua `Route.returnRouteId`:**

```
Route SG→HN  { id=R1, originStation=BX-Miền-Đông, destinationStation=BX-Mỹ-Đình, returnRouteId=R2 }
Route HN→SG  { id=R2, originStation=BX-Mỹ-Đình, destinationStation=BX-Miền-Đông, returnRouteId=R1 }

Operator tạo DriverSchedule cho tài xế A chiều đi:
  { driverId: A, routeId: R1, dayOfWeek: [1,3,5], departureTime: "08:00", vehicleId: V1 }

UI hỏi: "Bạn có muốn tạo lịch chiều về (HN→SG) cho tài xế A?"
  → Nếu YES: operator nhập departureTime + dayOfWeek chiều về
  → System tạo DriverSchedule thứ 2:
    { driverId: A, routeId: R2, dayOfWeek: [2,4,6], departureTime: "20:00", vehicleId: V1 }
```

**TripStop — entity requirements:**

`TripStop` là snapshot các điểm dừng của một chuyến cụ thể. Khác với `TripStopFare` (chỉ tồn tại khi operator muốn override `Route.baseFare` cho stop cụ thể), `TripStop` tồn tại cho **mọi chuyến có RouteStop entries** để Driver App hiển thị danh sách stop còn lại và Tracking Service tính ETA per stop.

Fields cần có: tripId + stopId composite key, `orderIndex` (1-indexed, thứ tự dừng trên chuyến), `estimatedArrivalTime` (static baseline — tính từ Trip.departureDateTime + RouteStop.estimatedDurationFromOriginMinutes, **KHÔNG bao giờ update sau khi generate**), `actualArrivalTime` nullable (khi xe thực sự đến — xem note bên dưới về cơ chế set), `actualDepartureTime` nullable (durable Day-24 stop-departure anchor), `status` enum (PENDING | ARRIVED | SKIPPED — SKIPPED khi stop bị disable lúc generate Trip), **`allowPickup` boolean + `allowDropoff` boolean**, **`distanceFromOriginKm` decimal nullable** (snapshot từ RouteStop dùng cho DISRUPTED refund).

> **Cơ chế set `TripStop.actualArrivalTime` — Assistant explicit confirm:**
> Assistant bấm nút "Đã đến [tên stop]" trong Driver App khi xe dừng tại điểm dừng đó → API call `POST /v1/driver/trips/{tripId}/stops/{stopId}/arrive` → Trip-Route-Vehicle Service set `TripStop.actualArrivalTime = now`, `TripStop.status = ARRIVED`. Nút này chỉ enable khi Trip.status = IN_PROGRESS.
>
> **Lý do chọn explicit confirm thay vì GPS auto-detect:** GPS auto-detect tạo cross-service write phức tạp (Tracking Service NestJS phải write sang Trip-Route-Vehicle ASP.NET Core DB qua HTTP/event, coupling chặt). GPS có thể giật (xe dừng đèn đỏ gần stop) → false positive. Explicit confirm đơn giản, nhất quán với các confirm action khác của Assistant (LOADED, UNLOADED). DISRUPTED refund luôn có fallback stop-order ratio nếu `actualArrivalTime` chưa được set — không bị block.
>
> **Endpoint:** `POST /v1/driver/trips/{tripId}/stops/{stopId}/arrive` — role DRIVER hoặc ASSISTANT của trip đó, precondition Trip.status = IN_PROGRESS và TripStop.status = PENDING.

> **Day-24 departure endpoint:** bodyless `POST /v1/driver/trips/{tripId}/stops/{stopId}/depart` —
> assigned DRIVER/ASSISTANT only, tenant-scoped, required UUID-v4 Idempotency-Key, precondition
> `Trip.status=IN_PROGRESS`, `TripStop.status=ARRIVED`, and nullable
> `TripStop.actualDepartureTime IS NULL`. Trip and TripStop are lock/CAS rechecked before
> persisting one departure timestamp. Trip then calls Booking's exact pending-count predicate;
> only a positive count publishes `trip.stop.departed_with_pending`. GPS is a future adapter to
> this command.
> Booking counts exactly `Booking.status=CONFIRMED AND Passenger.boardingStatus=PENDING AND
> Booking.tripId=:tripId AND Booking.pickupStopId=:stopId AND Booking.operatorId=:operatorId`.
> The seam uses only a valid Internal JWT, returns raw zero for no match, rejects malformed/all-zero
> UUID input with `422 VALIDATION_ERROR`, and performs no Trip/Stop lookup, tenant/caller-service
> authorization, or absent-reference `403`/`404` validation.
> Public success is `200 ApiResponse<DepartStopResponse>` with data exactly
> `{tripId,stopId,departedAt,pendingPassengerCount,eventEmitted}`. Wrong Trip state is
> `422 TRIP_NOT_IN_PROGRESS`, `PENDING|SKIPPED` is `422 TRIP_STOP_NOT_ARRIVED`, a new key after
> departure is `409 TRIP_STOP_ALREADY_DEPARTED`, and Booking dependency failure is
> `502 UPSTREAM_UNAVAILABLE`. Same-key replay is byte-identical; fingerprint mismatch is
> `422 IDEMPOTENCY_KEY_MISMATCH`.

> Hangfire job generate TripStop khi tạo Trip, copy từ RouteStop. Route change (alternative route) cập nhật TripStop per trip, không ảnh hưởng Route gốc.

**Hai-layer ETA — static vs dynamic:**
```
Layer 1 — Static ETA (trong DB):
  TripStop.estimatedArrivalTime = Trip.departureDateTime + RouteStop.estimatedDurationFromOriginMinutes
  → Set một lần khi generate Trip, KHÔNG thay đổi trong suốt chuyến
  → Dùng làm baseline đo deviation (xe trễ bao nhiêu so với kế hoạch)
  → Passenger App hiển thị trước khi chuyến bắt đầu (không có GPS)

Layer 2 — Dynamic ETA (trong Redis, Tracking Service maintain):
  Redis key: tracking:eta:{tripId}:{stopId}  (TTL 60s)
  → Tracking Service update khi GPS thỏa điều kiện (xe di chuyển >500m hoặc ETA < 15 phút)
  → Tracking Service KHÔNG ghi đè TripStop.estimatedArrivalTime trong DB
  → Passenger App đọc dynamic ETA từ Socket.IO broadcast (không từ TripStop entity)

Delayed detection (Hangfire/BullMQ mỗi 5 phút):
  dynamicETA = Redis eta:{tripId}:{nextStopId}
  staticETA  = TripStop.estimatedArrivalTime
  if dynamicETA - staticETA > 30 phút → publish TripDelayed event

Lý do không update DB: nếu TripStop.estimatedArrivalTime bị update liên tục theo GPS
thì mất baseline để đo "xe trễ bao nhiêu" — không còn gì để so sánh.
```

> **Chốt: DELAYED chỉ tồn tại ở Redis + Socket.IO event (Option A) — không thêm `Trip.isDelayed` vào DB.**
> `TripDelayed` event được publish qua Outbox → RabbitMQ → Notification Service (push passenger + operator). Trạng thái "đang trễ" chỉ tồn tại trong Redis key `tracking:eta:{tripId}:{stopId}` (TTL 60s, Tracking Service maintain). Không có `Trip.isDelayed boolean` trong DB.
>
> **Acceptable trade-off:** Nếu Operator refresh trang dashboard sau khi miss push notification, họ có thể không thấy DELAYED badge ngay. Để xử lý: Dashboard gọi `GET /v1/tracking/trips/{tripId}/eta` (query Tracking Service → đọc Redis) khi load trang — nếu dynamicETA > staticETA + 30 phút thì dashboard tự render DELAYED chip. Không cần DB field; REST query được cung cấp bởi Tracking Service REST endpoint (bên cạnh Socket.IO).
>
> **Lý do không thêm `Trip.isDelayed` vào DB:** (1) delayed là trạng thái tạm thời — xe có thể bắt kịp lịch; (2) thêm field tạo write path phức tạp (Tracking Service NestJS phải HTTP call sang Trip-Route-Vehicle ASP.NET Core để update DB mỗi khi ETA thay đổi); (3) REST fallback endpoint đủ để handle refresh case. Agent KHÔNG implement `Trip.isDelayed`.

**Trip BOARDING — trigger:**

`TripStatus: SCHEDULED → BOARDING` được trigger như sau:

```
Cơ chế: Hangfire job (recommended — tự động, không cần manual action):
  Job chạy mỗi 15 phút:
    SELECT trip WHERE status = SCHEDULED
      AND departureDateTime <= now + interval '30 minutes'
    → UPDATE status = BOARDING
    → Publish event TripBoardingStarted → Notification Service
        → Gửi push "Chuyến X sẽ khởi hành trong 30 phút — bắt đầu lên xe"

Operator/Driver cũng có thể manual trigger sớm hơn từ dashboard/app nếu cần
(ví dụ xe vào bến sớm hơn dự kiến)
```

**Trip IN_PROGRESS — trigger (2 cơ chế PRIMARY + SECONDARY):**

```
PRIMARY — Driver bấm "Bắt đầu chuyến" trong Driver App:
  Điều kiện tiên quyết: Trip.status = BOARDING
    (nút bị disable nếu Trip chưa BOARDING — ngăn Driver start trip quá sớm)
  → Trip.status = IN_PROGRESS, Trip.actualDepartureTime = now
  → Publish TripStarted event (Outbox pattern)
  → Parcel Service consume TripStarted: LOADED → IN_TRANSIT
  → Tracking Service: bắt đầu nhận GPS broadcast + active push to passengers

SECONDARY — Hangfire fallback (chạy mỗi 5 phút):
  Kịch bản: Driver quên bấm, app crash, hoặc xe xuất phát muộn vượt window
  SELECT Trip WHERE status = BOARDING
    AND departureDateTime < now - interval '30 minutes'
  → Trip.status = IN_PROGRESS (auto), Trip.actualDepartureTime = now
  → Publish TripStarted event
  (Window 30 phút: đủ cho xe trễ thực tế VN, tránh Trip treo BOARDING mãi)
```

> **Tại sao không dùng GPS làm PRIMARY trigger?** Tracking Service chỉ nhận GPS khi Trip đã IN_PROGRESS — nếu GPS là trigger thì circular dependency (cần IN_PROGRESS để accept GPS, cần GPS để set IN_PROGRESS). GPS có thể drift hoặc Driver bật app muộn → không reliable làm PRIMARY. Tracking Service bắt đầu active GPS session sau khi nhận TripStarted event.

**Auto-generate Trip — 2 trigger (immediate on-create + recurring weekly):**

```
TRIGGER A — On-create/update DriverSchedule (immediate):
  Khi operator tạo mới DriverSchedule HOẶC update `isActive` từ false → true:
    Trip-Route-Vehicle Service ngay lập tức enqueue Hangfire one-off job (BackgroundJob.Enqueue)
    Job logic giống TRIGGER B nhưng scope chỉ DriverSchedule vừa tạo/update
    → Generate Trip cho 14 ngày kế tiếp tính từ now

  Lý do: nếu operator tạo schedule giữa tuần (vd thứ Tư), phải có trip ngay cho 14 ngày tới,
  không chờ Sunday 23:00 (passenger không tìm được chuyến → mất doanh thu, UX kém).

TRIGGER B — Hangfire recurring weekly job:
  Job chạy mỗi CN 23:00:
    FOR EACH DriverSchedule WHERE isActive = true AND validUntil >= today:
      FOR EACH ngày trong 14 ngày tới khớp dayOfWeek:
        Check không trùng Trip đã tồn tại (driverId + departureDateTime)
        Tạo Trip {
          operatorId, routeId, driverId, assistantId, vehicleId
          departureDateTime = ngày + departureTime
          status = SCHEDULED
        }
        Generate TripSeat dựa trên Vehicle.seatLayoutJson
        Generate TripStop dựa trên RouteStop (copy orderIndex, allowPickup, allowDropoff,
          distanceFromOriginKm + tính estimatedArrivalTime) — snapshot 2 flag

Idempotent: cả 2 trigger đều check (driverId + departureDateTime) tồn tại trước khi INSERT
→ chạy nhiều lần không tạo duplicate (TRIGGER A + B có thể overlap trong tuần đầu, an toàn).

Edge case — đổi vehicleId trên DriverSchedule sau khi đã có Trip generated:
  `FUTURE_ONLY` giữ nguyên mọi Trip generated; Trip mới dùng vehicle mới.
  `ALL_PENDING` preflight rồi cascade atomically vào Trip SCHEDULED/BOARDING theo 6.11.1.
  `vehicleId:null + FUTURE_ONLY` chỉ clear schedule và skip generation; `ALL_PENDING` reject 422
  vì Trip.vehicleId NOT NULL, không partial/silent apply.
```

**Constraints:**
- Không cho phép 2 DriverSchedule active có cùng `driverId` + cùng `dayOfWeek` overlap + giờ chạy chồng chéo (validate khi tạo)
- **Vehicle conflict check:** Không cho phép 2 DriverSchedule active có cùng `vehicleId` + cùng `dayOfWeek` + giờ chạy chồng chéo. Một xe không thể chạy 2 tuyến cùng lúc. Validate khi Operator tạo hoặc cập nhật DriverSchedule — return lỗi rõ ràng "Xe [licensePlate] đã được assign cho tuyến [route] vào [dayOfWeek] [time]".
- Khi `isActive = false` hoặc qua `validUntil`: ngừng generate Trip mới, Trip đã generate vẫn giữ
- Operator có thể manual tạo Trip bổ sung ngoài DriverSchedule (chuyến tăng cường lễ Tết)

> **Vehicle conflict khi Hangfire generate Trip:** Ngoài validate lúc tạo DriverSchedule, job generate Trip cũng check `vehicleId + departureDateTime` không trùng trước khi INSERT Trip mới — tránh race condition nếu 2 schedule tạo cùng lúc.

#### 6.11.1 DriverSchedule Edit Flow — Confirm popup

Khi operator edit DriverSchedule (đổi `departureTime`, `dayOfWeek`, `driverUserId`,
`assistantUserId`, `vehicleId`, `validUntil`, hoặc `isActive`), hệ thống **PHẢI hiển thị confirm
popup** trên Operator Web yêu cầu chọn scope apply, vì có thể đã có Trip generated từ schedule cũ.
`routeId` và `validFrom` không editable. Canonical backend contract và order bên dưới supersede
pseudo-code cũ trong section này khi có khác biệt.

**UI Confirm popup:**

```
┌─────────────────────────────────────────────────────────┐
│ ⚠️ Lịch này đã có [N] chuyến đã được generate           │
│    ([K] chuyến trong số đó có booking CONFIRMED)        │
│                                                         │
│ Bạn muốn áp thay đổi cho:                              │
│                                                         │
│  ○ Chỉ các chuyến tương lai (sau hôm nay)              │
│    → Trip đã generated giữ nguyên (theo lịch cũ)        │
│    → Hangfire job lần sau dùng lịch mới                 │
│                                                         │
│  ○ Tất cả chuyến chưa IN_PROGRESS                       │
│    → Trip SCHEDULED/BOARDING sẽ update theo lịch mới   │
│    → Booking ảnh hưởng → trigger SCHEDULE_CHANGE flow  │
│      tự động (passenger nhận notify + xác nhận)        │
│                                                         │
│            [Huỷ]    [Tiếp tục]                          │
└─────────────────────────────────────────────────────────┘
```

**Endpoint:**

```
PATCH /v1/operator/driver-schedules/{scheduleId}?applyTo=FUTURE_ONLY|ALL_PENDING
Auth: OPERATOR_ADMIN; Idempotency-Key UUID v4 required

Body: { departureTime?, dayOfWeek?, driverUserId?, assistantUserId?, vehicleId?, validUntil?, isActive? }
```

Omitted nghĩa là unchanged. Explicit null chỉ clear `assistantUserId`, `vehicleId`, hoặc
`validUntil`; `validUntil:null` khôi phục open-ended. Null cho field còn lại, missing/invalid
`applyTo`, empty/unknown-only/malformed body trả `422 VALIDATION_ERROR`. Existing `/crew` là alias
deprecated một release, map vào cùng command với `ALL_PENDING`; không có business handler thứ hai.

**Logic per `applyTo=FUTURE_ONLY` (đơn giản, default suggest):**
- UPDATE DriverSchedule với fields mới sau validation
- Trip đã generate (status IN (SCHEDULED, BOARDING, IN_PROGRESS)) giữ nguyên — không touch
- Không gọi Booking vì không mutate generated Trip
- Nếu active và có vehicle, Hangfire generate chỉ ngày future chưa được cover theo lịch mới
- Nếu `vehicleId:null`, clear schedule vehicle nhưng không generate Trip mới; mỗi attempted date
  ghi existing `TripGenerationSkipLog` reason `OTHER` với message xác định chưa assign vehicle.
  Assign vehicle lại thì generation tiếp tục cho uncovered dates.
- Không có notification cho passenger (vì Trip cũ không đổi)

**`applyTo=ALL_PENDING` (apply triệt để, có cascade):**

```
FOR EACH Trip thuộc DriverSchedule này WHERE status IN (SCHEDULED, BOARDING):
  delta = | new_departureDateTime - old_departureDateTime |   (tính per Trip)

  CASE 1: departureTime thay đổi:
    severity = MINOR if cùng ngày và delta ≤ 2h
              MEDIUM if cùng ngày và 2h < delta < 6h
              MAJOR  if delta ≥ 6h hoặc đổi sang ngày khác (do dayOfWeek change)
    Apply SCHEDULE_CHANGE flow chuẩn (xem 6.13):
      → UPDATE Trip.departureDateTime + recompute static planned
        TripStop.estimatedArrivalTime baseline. GPS/Tracking dynamic ETA không update field này.
      → Trip stage `trip.trip.schedule_changed` trong local Outbox.
      → Booking consume sau commit cho từng Booking CONFIRMED: MINOR publish informational,
        MEDIUM/MAJOR mới INSERT BookingPendingAction + publish required fact. Booking status khác
        CONFIRMED không emit schedule fact. Trip không ghi Booking DB; Notification chỉ consume
        Booking fact.

  CASE 2: vehicleId thay đổi (chưa IN_PROGRESS) — Vehicle SWAP, không phải Substitution:
    → Apply **Vehicle Swap flow (lightweight — KHÔNG dùng Vehicle Substitution full flow)**:
        - Trip đã generate có status IN (SCHEDULED, BOARDING) chưa IN_PROGRESS → chưa có physical vehicle assignment, swap đơn giản.
        - UPDATE Trip.vehicleId = newVehicleId
        - Reconcile TripSeat bằng normalized seatNumber. Compatibility-only rank chính xác:
          `STANDARD < SLEEPER_UPPER < SLEEPER_LOWER < VIP`; rank không dùng cho pricing;
          `DRIVER_AREA` không phải passenger seat. Absent=`SEAT_REMOVED`; disabled hoặc
          `DRIVER_AREA`=`SEAT_DISABLED`; lower rank=`SEAT_TYPE_DOWNGRADED`; equal/higher compatible.
        - `SCHEDULED`: incompatible HELD block `TRIP_VEHICLE_SWAP_HELD_SEAT_CONFLICT`;
          incompatible BOOKED chỉ tạo `PENDING_SEAT_ASSIGNMENT` khi strict deadline viable.
          `BOARDING`: mọi incompatible HELD/BOOKED block `TRIP_VEHICLE_SWAP_TOO_LATE`.
        - Publish exact `trip.trip.vehicle_swapped`
          `{eventId,occurredAt,tripId,operatorId,oldVehicleId,newVehicleId,oldVehiclePlateNumber,
          newVehiclePlateNumber,departureDateTime,driverUserId,assistantUserId,
          seatImpacts:[{bookingId,seatNumbers,reason}]}`. `assistantUserId` luôn present nullable;
          reason chỉ `SEAT_REMOVED|SEAT_DISABLED|SEAT_TYPE_DOWNGRADED`.
        - Booking consume để tạo passenger pending action/fact. Notification dùng Trip fact chỉ
          notify crew; không notify passenger trực tiếp.
        - Audit action `TRIP_VEHICLE_SWAPPED` / cascade `DRIVER_SCHEDULE_CASCADE_APPLIED`.
    → **KHÔNG tạo BookingTransfer record** (entity đó cho substitution in-progress).
    → **KHÔNG dùng Trip.source = VEHICLE_SUBSTITUTION** (đó cho 6.12 in-progress flow).

  CASE 3: driverUserId/assistantUserId thay đổi:
    → UPDATE Trip.driverUserId/assistantUserId
    → Push notification driver/assistant mới + cũ
    → Không ảnh hưởng booking (passenger không cần xác nhận đổi tài xế)

  CASE 4: dayOfWeek thay đổi (vd remove T6 khỏi [T2,T4,T6]):
    → Trip nào còn match dayOfWeek mới → giữ
    → Trip nào KHÔNG còn match (vd Trip vào T6) →
        → Trip publish exact `trip.trip.cancelled`
          `{eventId,occurredAt,tripId,operatorId,cancelledAt,cancelReason}` với
          `cancelReason=DRIVER_SCHEDULE_DAY_REMOVED`, `cancelledAt=occurredAt`.
        → Booking cancel `PENDING_PAYMENT` refundAmount=0; cancel `CONFIRMED` refund 100% immutable
          persisted totalAmount; publish existing `booking.booking.cancelled` để Payment refund và
          Notification inform. Payment/Notification không consume Trip cancellation trực tiếp.

Chỉ sau toàn-batch preflight mới mở một local transaction; lock/reload/revalidate fixed order
schedule → Trips sorted `(departureDateTime,tripId)` → seats → stops; UPDATE DriverSchedule +
Trip cascades + audit + Outbox; save/commit
đúng một lần. Bất kỳ Trip fail thì rollback toàn batch. Không giữ DB transaction qua HTTP.
```

**Validation:**
- Fixed order: tenant schedule load/masked 404 → normalize/actual changes → no-op → scalar/window
  + null-vehicle rules → tenant/Identity references → schedule/vehicle/crew overlap → apply branch.
- `ALL_PENDING + vehicleId:null` trả `422 VALIDATION_ERROR` trước Booking fetch/mutation.
- `ALL_PENDING` enumerate Trip `SCHEDULED|BOARDING` deterministic và fetch tất cả Booking impact
  trước transaction. Trip có CONFIRMED Booking và `departureDateTime - now < 2h` block toàn request
  với `DRIVER_SCHEDULE_EDIT_TOO_LATE`. Exact boundary 2h không block bởi rule này.
- Conflict precedence của DriverSchedule vehicle cascade: `TRIP_VEHICLE_SWAP_HELD_SEAT_CONFLICT`
  rồi `TRIP_VEHICLE_SWAP_TOO_LATE`; không có `TRIP_ROUTE_CHANGE_BOOKINGS_EXIST` vì route immutable.
- Shorten `validUntil` hoặc `isActive=false` chỉ stop generation, không cancel/mutate generated
  Trip ở cả hai apply mode. Clear validUntil/reactivate chỉ generate uncovered future dates.

**Audit:** real changes dùng `DRIVER_SCHEDULE_EDITED` ở append-only driver-schedule audit và các
Trip cascade dùng `DRIVER_SCHEDULE_CASCADE_APPLIED`; metadata chuẩn
`{changedFields,before,after,requestId}` không chứa raw Idempotency-Key. No-op không audit/event.

**Error code mới:** `DRIVER_SCHEDULE_EDIT_TOO_LATE`.

### 6.12 Trip DISRUPTED — Vehicle Substitution (substitute vehicle, KHÔNG ghép chuyến)

> **Day-34 approved contract freeze (2026-07-25):** Nội dung normative dưới đây supersede toàn bộ
> proposal legacy đã đóng bên dưới. Vehicle Substitution luôn tạo một Trip thay thế dành riêng;
> Parcel transfer/count/behavior được defer sang Day 35.

**Lifecycle và recovery timeline**

- Trip cũ phải `IN_PROGRESS`; mọi trạng thái khác trả substitution-only
  `TRIP_NOT_SUBSTITUTABLE` (`409`). Existing `TRIP_NOT_IN_PROGRESS` (`422`) được preserved cho depart-stop,
  arrival, incident và mọi pre-Day-34 lifecycle context khác.
- Handler lock/reload Trip cũ, capture đúng một `disruptedAt`, và yêu cầu
  `estimatedRecoveryDepartureAt` strictly later than `disruptedAt`. Bằng hoặc sớm hơn trả
  `422 VALIDATION_ERROR` với exact field error
  `fields.estimatedRecoveryDepartureAt = ["must be later than disruptedAt"]`; không ghi Trip child,
  audit hoặc Outbox.
- Trip cũ chuyển terminal `DISRUPTED`, `hasSubstitution=true`. Trip mới có
  `status = BOARDING`, `source=VEHICLE_SUBSTITUTION`, và
  `departureDateTime=estimatedRecoveryDepartureAt`. Existing assigned-driver start flow chuyển
  `BOARDING -> IN_PROGRESS` và capture `actualDepartureTime`; không thêm Trip status.
- `recoveryDelay = estimatedRecoveryDepartureAt - disruptedAt`. Replacement destination ETA và
  mỗi copied old `PENDING` TripStop `estimatedArrivalTime` bằng old baseline tương ứng +
  `recoveryDelay`; không copy stop non-`PENDING`.

**HTTP và ownership**

`POST /v1/operator/trips/{tripId}/substitute-vehicle` là `OPERATOR_ADMIN`-only, UUID-v4
idempotent, strict body đúng
`{replacementVehicleId,estimatedRecoveryDepartureAt,reason,notifyPassengers,replacementCrew}`.
Crew absent/null copy old driver/assistant; crew present đúng `{driverId,assistantId}` và phải qua
active-role, operator-ownership, Trip-conflict validation. Unknown/legacy fields bị reject.
Success đúng
`{substitutionId,oldTripId,oldTripStatus,newTripId,newTripStatus,newTripDepartureDateTime,transferStatus,affectedBookingCount,affectedPassengerCount,pendingSeatAssignmentCount}`;
statuses là `DISRUPTED`/`BOARDING`, transfer `QUEUED`, và không có Parcel count.

Booking sở hữu Internal-JWT-only raw impact seam, với exact success
`{oldTripId,operatorId,bookings:[{bookingId,bookingStatus,passengers:[{passengerId,boardingStatus,originalSeatNumber}]}]}`.
Chỉ Booking `CONFIRMED|PARTIAL_NO_SHOW` và Passenger `BOARDED|PENDING`; sort `bookingId` rồi
`passengerId`; empty `200 {bookings:[]}`. `originalSeatNumber` nullable nên chained substitution
vẫn eligible; Booking không trả `seatType`. Khi `originalSeatNumber` non-null, Trip lookup
`TripSeat` của Trip cũ để lấy preferred seat type. Khi seat gốc null hoặc không có old TripSeat
match, passenger không có preferred type và deterministic allocation fallback theo remaining
passenger-seat order, sau đó trả null khi hết ghế. Trip sở hữu replacement layout, TripSeat và
mapping; không service nào ghi DB khác.

**Facts, transfer persistence và confirmation**

Trip emit exactly one `trip.trip.vehicle_substituted` với exact payload
`{eventId,occurredAt,substitutionId,disruptedAt,operatorId,oldTripId,oldTripStatus,oldVehicleId,newTripId,newTripStatus,newVehicleId,newVehiclePlateNumber,newTripDepartureDateTime,actorUserId,reason,notifyPassengers,mappings:[{bookingId,passengerId,originalSeatNumber,newSeatNumber,originalBoardingStatus}]}`.
`occurredAt=disruptedAt`, `substitutionId=eventId`, both seat values nullable,
`originalBoardingStatus=BOARDED|PENDING`. Cùng Trip-local transaction emit canonical
`trip.trip.disrupted {hasSubstitution:true}` với EventId khác. Mỗi fact giữ
`payload.eventId == Outbox row id == RabbitMQ MessageId`; retry/restart giữ identity/routing key.

`passengers.seat_number` và cả hai BookingTransfer seat-history values nullable. Confirmation enum
đúng `PENDING_CONFIRM|CONFIRMED|NOT_REQUIRED`; transfer có non-null `confirmation_status`, nullable
`confirmed_at`, logical nullable `confirmed_by_user_id`, unique
`(passenger_id,original_trip_id,new_trip_id)`. Mapped old `BOARDED` tạo `PENDING_CONFIRM`; mapped old
`PENDING` tạo `NOT_REQUIRED`; `NO_SHOW` không có mapping/transfer. Booking giữ nguyên
`CONFIRMED|PARTIAL_NO_SHOW`; duplicate delivery không tạo effect.

Migration `Down()` backfill null Passenger seat bằng recent non-null `new_seat_number`, otherwise
recent non-null `original_seat_number`, ordered by `transferred_at DESC, id DESC`;
fail `Down()` nếu còn null trước `SET NOT NULL` và không invent sentinel.

Bodyless confirmation endpoint dành cho assigned `DRIVER|ASSISTANT`, UUID-v4 idempotent. Matching
active transfer phải có non-null `newSeatNumber`; first confirmation chỉ set row đó `CONFIRMED`,
replay/already-confirmed trả persisted `200`, không sửa Passenger boarding history/Ticket usage.
Errors: `404 BOOKING_TRANSFER_NOT_FOUND`, `409 BOOKING_TRANSFER_SEAT_PENDING`,
`422 VALIDATION_ERROR`, `403 FORBIDDEN`.

Booking emit exactly one `booking.booking.transferred` per eligible Booking per substitution, kể cả
`notifyPassengers=false`, với exact payload
`{eventId,occurredAt,sourceSubstitutionEventId,bookingId,recipientUserId,operatorId,oldTripId,newTripId,newVehicleId,newVehiclePlateNumber,newTripDepartureDateTime,notifyPassengers,transfers:[{passengerId,originalSeatNumber,newSeatNumber,confirmationStatus}]}`.
Recipient đúng `Booking.passengerUserId`, không Passenger PII/alternate recipient. Notification
dedupe by MessageId/EventId: true tạo đúng một Booking-owner `VEHICLE_SUBSTITUTED`; false không tạo
notification/push.

#### 6.12.1 Trip DISRUPTED không substitute — Operator hủy IN_PROGRESS

Trường hợp Trip đang IN_PROGRESS nhưng operator **không thể** điều xe thay thế (thiên tai, không còn xe rảnh, driver khẩn cấp, hành khách đồng ý dừng chuyến tại chỗ). Operator chọn phương án "Hủy chuyến không thay thế" thay vì Vehicle Substitution.

**Trigger:**
```
Operator bấm "Hủy chuyến không thay thế" trong dashboard (chỉ enable khi Trip.status = IN_PROGRESS)
  → UI bắt buộc nhập reason text + xác nhận 2 bước
  → POST /v1/operator/trips/{tripId}/disrupt-no-substitution
      Role: OPERATOR_ADMIN; UUID-v4 Idempotency-Key required
      Body exact: { reason: string trim 1..500 }
      SCHEDULED/BOARDING → 422 TRIP_NOT_IN_PROGRESS
      COMPLETED/CANCELLED/DISRUPTED → 409 TRIP_ALREADY_TERMINAL
  → Trip-Route-Vehicle Service:
      UPDATE Trip SET status = DISRUPTED, hasSubstitution = false,
                      disruptedAt = now, disruptionReason = reason
      Publish exact `trip.trip.disrupted`
        {eventId,occurredAt,tripId,operatorId,terminalAt,hasSubstitution:false,reason}
```

**Booking refund proportional formula:**

Booking Service consume `TripDisrupted { hasSubstitution: false }`:

Booking MUST fetch the authoritative raw
`GET /internal/v1/trips/{tripId}` snapshot before calculating each affected Booking. The snapshot
supplies nullable `totalDistanceKm`, ordered stops, stop status, nullable
`distanceFromOriginKm`, and `orderIndex`. A timeout, 5xx, malformed response, or missing Trip is a
retryable consumer failure that proceeds through broker retry/DLQ; it MUST NOT be interpreted as
"no stop arrived" or produce a 100% refund.

**Step 1 — Tính `traveledRatio` (tỷ lệ chặng đã đi tính từ pickup point của booking đến stop xa nhất xe đã ARRIVED):**

```
PRIMARY path chỉ khi `totalDistanceKm`, pickup distance cần dùng, và distance của reached stop xa
nhất đều có mặt, đồng thời denominator dương. Nếu bất kỳ input cần thiết nào null/không hợp lệ,
dùng toàn bộ FALLBACK path; không trộn distance với order:

  pickupDistance = distance từ origin Station đến pickup point của booking:
    - Nếu Booking.pickupStationId = Route.originStationId (terminal pickup) → pickupDistance = 0
    - Nếu Booking.pickupStopId NOT NULL → pickupDistance = TripStop.distanceFromOriginKm
      của stop đó (snapshot copy từ RouteStop khi generate Trip)

  reachedDistance = max(TripStop.distanceFromOriginKm) WHERE TripStop.status = ARRIVED
    AND TripStop.tripId = trip.id
    (nếu không có stop nào ARRIVED — xe chưa qua stop nào → reachedDistance = 0)

  totalDistance = Route.totalDistanceKm
    (distance từ origin Station đến destination Station, lấy từ Route entity)

  bookingTravelDistance = max(reachedDistance - pickupDistance, 0)
    (chặng booking thực sự đã đi: từ pickup đến stop ARRIVED xa nhất)

  bookingTotalDistance = totalDistance - pickupDistance
    (chặng đầy đủ booking cam kết đi: từ pickup đến destination)

  IF bookingTotalDistance <= 0:
    traveledRatio = 0   (edge case: pickup = destination, không xảy ra với Booking hợp lệ)
  ELSE:
    traveledRatio = bookingTravelDistance / bookingTotalDistance
                    (kẹp về [0, 1])

FALLBACK path (TripStop.distanceFromOriginKm NULL — Operator chưa nhập):

  pickupOrder = orderIndex của pickup stop (0 nếu terminal pickup)
  reachedOrder = max(TripStop.orderIndex) WHERE TripStop.status = ARRIVED (0 nếu chưa stop nào ARRIVED)
  totalOrder = max(TripStop.orderIndex) trong RouteStop của trip này + 1
              (cộng 1 vì destination station tính là 1 "stop" cuối tuyến)

  bookingTravelOrder = max(reachedOrder - pickupOrder, 0)
  bookingTotalOrder = totalOrder - pickupOrder

  IF bookingTotalOrder <= 0:
    traveledRatio = 0
  ELSE:
    traveledRatio = clamp(bookingTravelOrder / bookingTotalOrder, 0, 1)
```

**Step 2 — Tính `refundAmount`:**

```
refundAmount = round(Booking.totalAmount × (1 - traveledRatio), đồng gần nhất,
                     MidpointRounding.AwayFromZero)  -- BSOT v1.11.0: không floor 1000
              kẹp về [0, Booking.totalAmount]
```

**Step 3 — Update DB + publish events:**

```
UPDATE Booking SET status = DISRUPTED,
                   cancellationReason = OPERATOR_DISRUPTED_IN_PROGRESS,
                   refundOverride = true
Trong cùng Booking-local transaction, publish hai facts với hai fresh stable EventId riêng:
  1. `booking.booking.cancelled`
     {eventId,occurredAt,bookingId,userId,refundAmount,refundOverride:true,
      cancellationReason:"OPERATOR_DISRUPTED_IN_PROGRESS",bookingCode,...}
     → sole Payment refund trigger. Notification suppress generic BOOKING_CANCELLED cho reason này.
  2. `booking.booking.disrupted`
     {eventId,occurredAt,bookingId,bookingCode,tripId,operatorId,userId,traveledRatio,
      refundAmount,cancellationReason:"OPERATOR_DISRUPTED_IN_PROGRESS"}
     → Notification-only passenger fact; Payment không bind.
Mỗi payload.eventId == Outbox row id == RabbitMQ MessageId.

Payment consume `booking.booking.cancelled`:
      1. Credit Wallet user với refundAmount (atomic Wallet UPDATE + INSERT WalletTransaction)
      2. DEBIT PlatformWallet holding pool:
         INSERT PlatformWalletTransaction { type=DEBIT, referenceType=BOOKING_REFUND,
           amount=refundAmount, referenceId=bookingId,
           note="DISRUPTED no-substitution proportional refund" }
      3. INSERT OperatorLedgerEntry — audit-only:
         { entryType=BOOKING_REFUND, tripId=Trip.id, amount = -refundAmount, referenceType=BOOKING,
           referenceId=bookingId, note="DISRUPTED no-substitution proportional refund" }
         → Trip DISRUPTED là Trip terminal → đã/sẽ có `OperatorTripSettlement` được tạo
            cho trip này. Ledger refund entry này sẽ được tính vào SUM khi compute netAmount
            tại settle time (giảm số tiền operator nhận về ví). Đảm bảo settlement consistency:
            operator không nhận đủ doanh thu cho chặng chưa đi (fair với passenger nhận lại tiền).
      4. Voucher rollback (nếu booking đã apply voucher):
         - VIETRIDE_FUNDED: INSERT ADJUSTMENT entry amount = -discountAmount (refund credit cũ)
         - OPERATOR_FUNDED: chỉ DELETE VoucherUsage (audit entry amount=0 không ảnh hưởng balance)
  → `payment.wallet.credited` back → Booking Service CAS `DISRUPTED → REFUNDED`
Notification consume duy nhất `booking.booking.disrupted` và push passenger với traveledRatio:
  "Chuyến [X] bị gián đoạn không thể tiếp tục. Hoàn [refundAmount] VND về Ví VietRide
   (đã đi được ~[traveledRatio × 100]% lộ trình của bạn)."
```

**Worked examples:**

```
Example 1 — Terminal pickup, xe đi 60% chặng rồi DISRUPTED:
  Route: SG (0km) → NT (450km) → ĐN (970km) → HN (1700km), totalDistance = 1700
  Booking: pickup tại Bến SG (terminal), totalAmount = 400,000 VND
  Xe ARRIVED tại ĐN (970km) rồi hỏng tại km 1050 (chưa đến stop tiếp theo)

  pickupDistance = 0
  reachedDistance = 970 (max ARRIVED stop)
  bookingTravelDistance = 970 - 0 = 970
  bookingTotalDistance = 1700 - 0 = 1700
  traveledRatio = 970/1700 ≈ 0.5706
  refundAmount = round(400,000 × (1 - 0.5706), đồng) = round(171,764.7) = 171,765 VND  -- BSOT v1.11.0

Example 2 — Along-route pickup (NT), xe DISRUPTED trước khi đến ĐN:
  Same route, Booking: pickup tại NT (450km), totalAmount = 300,000 VND (giá NT→HN qua TripStopFare)
  Xe ARRIVED tại NT (đón booking này) + chạy tiếp đến km 700 rồi hỏng

  pickupDistance = 450
  reachedDistance = 450 (chỉ có NT là stop ARRIVED — chưa tới ĐN)
  bookingTravelDistance = max(450 - 450, 0) = 0
  bookingTotalDistance = 1700 - 450 = 1250
  traveledRatio = 0 / 1250 = 0
  refundAmount = round(300,000 × 1.0, đồng) = 300,000 VND (hoàn 100%)

  → Booking pickup tại NT, xe rời NT chưa qua stop nào sau đó → coi như chưa đi được gì cho booking này → hoàn 100%

Example 3 — FALLBACK (operator chưa nhập distance), xe đi qua 2/4 stop sau pickup:
  Route: SG → NT → ĐN → Vinh → HN (4 stop dọc tuyến, totalOrder = 4 + 1 = 5 tính cả destination)
  Booking: pickup tại SG (terminal, pickupOrder = 0), totalAmount = 500,000
  Xe ARRIVED đến ĐN (orderIndex = 2), hỏng trước Vinh
  Operator chưa nhập distanceFromOriginKm → fallback

  pickupOrder = 0
  reachedOrder = 2
  totalOrder = 5
  bookingTravelOrder = 2 - 0 = 2
  bookingTotalOrder = 5 - 0 = 5
  traveledRatio = 2/5 = 0.4
  refundAmount = round(500,000 × 0.6, đồng) = 300,000 VND
```

**Edge cases:**

| Edge case | Xử lý |
|---|---|
| Xe DISRUPTED ngay khi xuất phát, chưa qua stop nào | `reachedDistance = 0` (hoặc `reachedOrder = 0`) → `traveledRatio = 0` → refund 100% |
| Booking pickup stop chưa được ARRIVED khi DISRUPTED | `bookingTravelDistance = 0` → refund 100% (passenger chưa lên xe được) |
| Trip không có stop nào (express SG→HN) | Fallback path: `totalOrder = 0 + 1 = 1`, `reachedOrder = 0` → `traveledRatio = 0` → refund 100% (xe hỏng giữa đường nhưng không có waypoint đo được tiến độ) |
| Round-trip booking (outbound + return là 2 booking riêng) | Áp dụng formula **độc lập per Booking**. DISRUPTED ở outbound trip chỉ ảnh hưởng outbound Booking. Return Booking không ảnh hưởng (xem 6.1 round-trip rules). |
| Passenger NO_SHOW trên trip này trước khi DISRUPTED | Booking đã chuyển NO_SHOW trước (terminal status) → KHÔNG match filter `status IN (CONFIRMED, PARTIAL_NO_SHOW)` → không refund |
| PARTIAL_NO_SHOW booking | Vẫn áp formula với `Booking.totalAmount` đầy đủ (passenger BOARDED chiếm 1 phần, NO_SHOW chiếm phần kia — operator chấp nhận hoàn `totalAmount × (1 - traveledRatio)` để đơn giản, không tính per-passenger) |

**Parcel behavior khi DISRUPTED no-substitution:**
- Tái sử dụng nguyên vẹn Day-32 classifier:
  - `PENDING_OPERATOR_REVIEW|PENDING_PAYMENT|PENDING|PENDING_ADDITIONAL_PAYMENT|RESERVED|CHECKED_IN|PENDING_FINAL_PAYMENT|READY_TO_LOAD`
    → `CANCELLED`, release active cargo và refund outstanding collected amount.
  - `LOADED|IN_TRANSIT` → `PENDING_OPERATOR_ACTION`, chưa release/refund.
  - Terminal/replay → no-op.

**Phân biệt với Vehicle Substitution:**
| | Vehicle Substitution | No-substitution Disrupted |
|---|---|---|
| `Trip.status` | DISRUPTED | DISRUPTED |
| `Trip.hasSubstitution` | true | false |
| BookingTransfer records | có | không |
| Booking outcome | Chuyển sang Trip_new, giữ CONFIRMED | → DISRUPTED → REFUNDED proportional |
| `cancellationReason` | (không set — booking không cancel) | `OPERATOR_DISRUPTED_IN_PROGRESS` |
| Parcel outcome | `LOADED|IN_TRANSIT → PENDING_TRANSFER_CONFIRM` lên xe mới | Dùng Day-32 classifier: pre-load `→ CANCELLED`; `LOADED|IN_TRANSIT → PENDING_OPERATOR_ACTION`; terminal/replay no-op |

> **Lưu ý field cần thêm:** `Trip.disruptedAt datetime nullable` (set khi DISRUPTED, dùng cho reporting/audit), `Trip.disruptionReason text nullable` (operator nhập khi trigger).

### 6.13 Schedule Change — Operator đổi giờ khởi hành (3 mức)

**Producer và cutoff:** PATCH `/v1/operator/driver-schedules/{scheduleId}?applyTo=FUTURE_ONLY|ALL_PENDING`
là producer duy nhất của schedule change; chỉ nhánh `ALL_PENDING` được cascade
`Trip.departureDateTime`. Không có endpoint đổi lịch riêng trên Trip và không cần Gateway route
mới. Handler capture clock đúng một lần. Với mỗi Trip có Booking `CONFIRMED`, cả
`oldDeparture - now` và computed `newDeparture - now` phải `>= 2h`; equality được phép, còn bất kỳ
giá trị nào `< 2h` trả `409 DRIVER_SCHEDULE_EDIT_TOO_LATE` trước transaction.

**Phân loại theo `delta = |newDeparture - oldDeparture|` và ngày lịch ICT:**

| Severity | Điều kiện | Refund khi passenger reject | Xử lý Booking `CONFIRMED` |
|---|---|---|---|
| **MINOR** | cùng ngày ICT và `delta <= 2h` | 0% | Không tạo `BookingPendingAction`; publish informational fact. |
| **MEDIUM** | cùng ngày ICT và `delta > 2h && delta < 6h` | 50% | Tạo `SCHEDULE_CHANGE` severity MEDIUM. |
| **MAJOR** | `delta >= 6h` hoặc đổi ngày ICT | 100% | Tạo `SCHEDULE_CHANGE` severity MAJOR. |

**Projection và causal sequencing trong Booking:**

- `trip_snapshot_departure` là lịch sử immutable; không sửa khi Trip đổi giờ.
- `trip_current_departure` là projection mutable, backfill ban đầu từ snapshot. Consumer áp dụng
  `trip.trip.schedule_changed` cho Booking `PENDING_PAYMENT|CONFIRMED`: nếu current bằng
  `oldDeparture` thì CAS sang `newDeparture`; nếu current đã bằng `newDeparture` thì duplicate
  no-op; nếu khác cả hai thì retry rồi quarantine, không overwrite mù.
- Chỉ Booking `CONFIRMED` publish passenger fact: MINOR phát
  `booking.booking.schedule_change_informational`; MEDIUM/MAJOR supersede action active cũ rồi tạo
  đúng một active `SCHEDULE_CHANGE` và phát `booking.booking.schedule_change_required`.
  `PENDING_PAYMENT` chỉ cập nhật projection. Notification không consume Trip fact trực tiếp.
- Query operator giữ key cũ: `date` lọc half-open ICT day trên `trip_current_departure`, còn
  `sortBy=departureAt` sort projection này rồi `id` cùng chiều `sortDir`. Response list/detail đặt
  `trip.currentDepartureAt` cạnh immutable `trip.departureAt`; không có top-level duplicate hoặc
  sort key `currentDepartureAt`.
- Deadline của `STOP_DISABLED` dùng `trip_current_departure`, không dùng snapshot cũ.

**Deadline và metadata freeze cho MEDIUM/MAJOR:**

```text
if occurredAt is more than 24h before newDeparture:
  initialDeadline = min(occurredAt + 24h, newDeparture - 2h)
else:
  initialDeadline = max(occurredAt + 60m, newDeparture - 30m)

terminalDeadline = newDeparture - 30m for MAJOR; null for MEDIUM
effectiveCutoff = terminalDeadline when MAJOR and initialDeadline < terminalDeadline;
                  otherwise initialDeadline
```

Action metadata freeze đúng
`{sourceEventId,oldDeparture,newDeparture,severity,initialDeadline,terminalDeadline,
refundBasisAmount,refundPercent,refundAmount}`. `refundBasisAmount` luôn là immutable
`Booking.totalAmount`; `refundPercent` là 50 hoặc 100; `refundAmount` làm tròn đến đồng gần nhất
bằng `MidpointRounding.AwayFromZero`. Không dùng giá hiện tại từ Trip hay Payment làm refund basis.

**Passenger resolution:** POST `/v1/bookings/{bookingId}/pending-actions/{actionId}/resolve` chỉ
cho JWT role `PASSENGER` và đúng Booking owner, body đúng `{ action: ACCEPTED|REJECTED, note? }`,
UUID-v4 `Idempotency-Key` bắt buộc. Persisted reason phải là `SCHEDULE_CHANGE`; `selectedStopId`
invalid và operator seat assignment không thuộc contract này. Same-key/same-payload replay
byte-identical trước khi đọc terminal state; new key mới nhận conflict terminal. Request tại
`now == effectiveCutoff` vẫn hợp lệ; chỉ `now > effectiveCutoff` là expired và job sở hữu outcome.

- `ACCEPTED`: resolve action với `resolvedAction=ACCEPTED`; Booking giữ `CONFIRMED`.
- `REJECTED`: trong một Booking transaction, resolve action, set Booking `CANCELLED` với
  `refundOverride=true`, `cancellationReason=SCHEDULE_CHANGED`, append status history, và enqueue đúng một authoritative
  `booking.booking.cancelled` chứa frozen `refundAmount`. MEDIUM hoàn 50%; MAJOR hoàn 100%.

**Scheduled phases:** Day-22 `PendingActionRealertJob` không đổi: chạy tại action occurrence `+2h`
cho unresolved `PENDING_SEAT_ASSIGNMENT` và MEDIUM/MAJOR `SCHEDULE_CHANGE`, mỗi intended phase at
most once. Day-23 `ScheduleChangeAutoAcceptJob` là job riêng:

- MEDIUM finalize `ACCEPTED` tại `initialDeadline + 1s`.
- MAJOR khi `initialDeadline < terminalDeadline`: tại initial phase có thể phát đúng một re-alert
  với identity riêng, rồi finalize `ACCEPTED` tại `terminalDeadline + 1s`. Nếu job lag đã qua
  terminal deadline thì đi thẳng final phase, không phát initial-phase fact.
- MAJOR khi `initialDeadline >= terminalDeadline`: không có initial-phase re-alert và chỉ finalize
  khi strict-after `initialDeadline`.
- Không phase nào cancel Booking hoặc tạo refund. Chỉ MEDIUM final và MAJOR terminal/direct phát
  `booking.booking.pending_action_auto_resolved` với exact payload
  `{eventId,occurredAt,bookingId,tripId,userId,pendingActionId,resolvedAction,severity,
  oldDeparture,newDeparture}`, trong đó `resolvedAction=ACCEPTED`.

Mọi Day-23 Outbox fact dùng một identity xuyên suốt:
`payload.eventId == outbox_events.id == RabbitMQ MessageId`. `booking.booking.cancelled` producer
mới luôn có fresh UUID-v4 `eventId` và producer-captured offset-date-time `occurredAt`; consumer chỉ
fallback theo `bookingId` cho exact legacy payload thiếu đồng thời cả hai field, không chấp nhận
partial identity. Notification map required/re-alerted/auto-resolved schedule facts sang type hiện
có `TRIP_SCHEDULE_CHANGED` và dedupe theo MessageId.

### 6.14 Shuttle Service (Xe trung chuyển)

**Mô hình:** Xe trung chuyển gom hành khách nội thành về bến chính → khách lên xe chính. Chiều về (khách xuống tại bến chính) → shuttle đưa khách đến địa chỉ.

**Scope:**
- Chỉ tổ chức tại **Station chính lớn** (TP.HCM, Đà Lạt, Hà Nội...). KHÔNG tổ chức tại Stop dọc tuyến — khách along-route stop tự đến điểm dừng.
- Entity `Station` cần flag `supportsShuttle boolean` (default false). Operator (hoặc System Admin) bật/tắt per Station.
- Shuttle áp dụng cho:
  1. **Passenger booking pickup** tại Station có `supportsShuttle = true` → có thể chọn shuttle pickup từ địa chỉ.
  2. **Passenger booking dropoff** tại Station có `supportsShuttle = true` → có thể chọn shuttle dropoff đến địa chỉ.
  3. **Route change fallback** (xem 6.4): khi auto-fallback đến destination tuyến thay thế, shuttle đưa passenger về stop ban đầu.

**Tracking:**
- Shuttle vehicle phải **trackable** — GPS broadcast như xe chính (Tracking Service join room `shuttle:{shuttleTripId}`).
- **Manifest:** hệ thống biết shuttle chở user nào, địa chỉ đón/trả từng người.
- Shuttle **linh hoạt** — KHÔNG có RouteStop sequence cứng. Hoạt động trong vài phường/quận xung quanh Station chính. Driver shuttle nhận manifest và tự lên đường đi theo thứ tự thuận tiện (hoặc theo gợi ý Google Maps).

**Entity `ShuttleTrip` (thuộc Trip-Route-Vehicle Service):**
```
ShuttleTrip {
  id UUID
  operatorId FK
  mainTripId FK → Trip   // shuttle gắn với chuyến chính (chiều đi gom về bến, chiều về tỏa từ bến)
  stationId FK → Station // Station chính shuttle phục vụ
  direction enum: INBOUND_TO_STATION | OUTBOUND_FROM_STATION
                  // INBOUND = gom khách nội thành về bến để lên xe chính
                  // OUTBOUND = đưa khách từ bến đến địa chỉ sau khi xuống xe chính
  driverUserId FK → User (role DRIVER, có thể là driver shuttle riêng)
  vehicleId FK → Vehicle  // xe shuttle (size nhỏ, ví dụ 16 chỗ)
  status enum: SCHEDULED | IN_PROGRESS | COMPLETED | CANCELLED
  scheduledDepartureTime datetime
  actualDepartureTime datetime nullable
  completedAt datetime nullable
  notes text nullable
  timestamps
}

ShuttlePassenger (manifest entry — link passenger booking with shuttle) {
  id UUID
  shuttleTripId FK → ShuttleTrip NULLABLE  // null khi passenger đăng ký shuttle nhưng operator chưa tạo ShuttleTrip
  mainTripId FK → Trip  // link với main Trip để operator group requests theo trip
  bookingId FK → Booking  // hoặc nullable nếu passenger phát sinh từ route-change fallback không có booking gốc trên shuttle
  direction enum: INBOUND_TO_STATION | OUTBOUND_FROM_STATION  // hint cho operator group shuttle
  pickupAddress text     // địa chỉ đón (INBOUND) hoặc dropoff (OUTBOUND)
  pickupLat decimal, pickupLng decimal
  scheduledPickupTime datetime nullable
  status enum: PENDING_ASSIGNMENT | PENDING | PICKED_UP | DELIVERED | NO_SHOW | CANCELLED
       // PENDING_ASSIGNMENT = đã đăng ký, chờ operator tạo ShuttleTrip
       // PENDING = đã assigned vào ShuttleTrip, chờ pickup
  pickedUpAt datetime nullable
  deliveredAt datetime nullable
  cancelReason text nullable  // operator cancel = "Không đủ ngưỡng" hoặc passenger cancel
  timestamps
}
```

**Flow basics:**

1. **Passenger booking shuttle (trigger ShuttlePassenger creation):**
   - Khi passenger chọn shuttle pickup/dropoff tại Station có `supportsShuttle = true` trong flow đặt vé.
   - Passenger nhập địa chỉ + lat/lng (qua Google Maps Places API autocomplete).
   - Hệ thống tạo `ShuttlePassenger` record **chưa link với ShuttleTrip** (`shuttleTripId = null`) — chỉ ghi nhận passenger có nhu cầu shuttle cho main Trip này.
   - Booking confirm bình thường (passenger không phải chờ shuttle có/không — vé chính độc lập).

2. **Operator review nhu cầu shuttle (Operator Dashboard):**
   - Operator có view "Shuttle requests pending" — list các ShuttlePassenger chưa assigned `shuttleTripId` group theo `mainTripId`.
   - Mỗi entry hiển thị: mainTrip info (route, departureDateTime), số passenger đăng ký shuttle, danh sách địa chỉ pickup/dropoff trên bản đồ.
   - Operator quyết định **thủ công** có tổ chức shuttle hay không cho main Trip đó.

3. **Create ShuttleTrip (fully manual):**
   - Operator bấm "Tạo shuttle trip" → form chọn driver shuttle, vehicle nhỏ, scheduledDepartureTime, direction (INBOUND_TO_STATION trước main Trip departure, hoặc OUTBOUND_FROM_STATION sau main Trip arrival).
   - System tạo `ShuttleTrip` record + assign tất cả `ShuttlePassenger` matching (mainTripId + direction) vào ShuttleTrip mới.
   - Validate vehicle conflict (xe shuttle không trùng main Trip cùng thời điểm).
   - Validate driver shuttle không trùng schedule khác.

4. **Operator quyết định KHÔNG tổ chức shuttle:**
   - Operator bấm "Hủy nhu cầu shuttle" cho main Trip → push notification cho các passenger:
     "Nhà xe không tổ chức shuttle cho chuyến [X]. Vui lòng tự đến bến [station name]. Vé chính của bạn không bị ảnh hưởng."
   - Update ShuttlePassenger.status = CANCELLED (chưa assigned shuttleTripId).
   - Booking chính vẫn CONFIRMED (passenger tự đến bến, không refund vì shuttle là dịch vụ miễn phí v1).

5. **Passenger tracking shuttle:**
   - Sau khi ShuttleTrip được tạo + assigned, Passenger App hiển thị tab "Theo dõi shuttle" với GPS realtime + ETA đến địa chỉ của mình.
   - Trước khi ShuttleTrip tạo, Passenger App hiển thị "Đang chờ nhà xe xác nhận shuttle".

6. **Driver shuttle:**
   - Driver App cho shuttle hiển thị manifest (danh sách địa chỉ + tên buyer + SĐT), GPS tracking, tick "đã đón" / "đã giao" per passenger.

**Fare shuttle:**

- v1: shuttle **miễn phí** khi đính kèm booking chính (operator chịu chi phí để hỗ trợ vận hành bến).
- v2: operator có thể config phí shuttle theo zone/khoảng cách.

**v2 — Auto-trigger ShuttleTrip (defer):**

- Add `Operator.shuttleMinPassengers` int default 5 (số passenger tối thiểu để tự tạo shuttle).
- Add `Operator.shuttleAutoTriggerCutoffHours` int default 6 (thời điểm trước main Trip departure để auto-trigger).
- Hangfire job (mỗi 30 phút): nếu mainTrip còn ≥ `shuttleAutoTriggerCutoffHours` giờ và `ShuttlePassenger COUNT ≥ shuttleMinPassengers` → tự tạo ShuttleTrip skeleton (chưa assign driver/vehicle) → alert operator "Đủ ngưỡng shuttle, cần assign driver/vehicle".
- Nếu cutoff đến mà chưa đủ ngưỡng → auto-cancel ShuttlePassenger requests + push notification.
- **v1 KHÔNG implement** — operator fully manual quyết định.

### 6.15 Day 40 — Admin Users, Station Cleanup và Platform Reports

#### Admin User lifecycle và serialization

System Admin có danh mục User toàn platform với search/filter/paging/sort và optional
`includeDeleted`; response chỉ chứa profile/lifecycle fields, không chứa password hash, OAuth
subject, token, OTP hoặc failed-login internals.

Manual lock chỉ cho `ACTIVE -> LOCKED`; target đã `LOCKED` là ensure-locked success. Lock revoke mọi
active RefreshToken bằng `ADMIN_REVOKE` và ghi `LOCK_USER` trong cùng transaction. Unlock restore
đúng `lockedFromStatus` (`ACTIVE` hoặc `PENDING_EMAIL_VERIFICATION`), reset DB + Redis lockout state,
clear origin, ghi `UNLOCK_USER`, không phục hồi token đã revoke và không promote pending account.
Self-lock/self-unlock bị cấm. Hai endpoint bắt buộc shared Idempotency-Key v2; completed replay không
dispatch command hoặc ghi audit lần hai. Day 40 không có access-token denylist.

Tất cả flow sau phải dùng cùng PostgreSQL per-User serialization protocol: password login, Google
login của existing/matched User, refresh, forgot/reset password, password-reset OTP failure,
failed-login persistence và admin lock/unlock. Lookup ngoài transaction chỉ là routing hint. Mỗi
flow lock/reload `users` bằng `FOR UPDATE`, recheck status và không quyết định từ EF tracked snapshot.
Canonical lock order: User UUID ascending -> EmailVerificationToken UUID -> refresh-token/family
UUID -> ActivityLog/Outbox. Token/OTP/password state chỉ được trả sau commit.

Password login đúng verify lại hash dưới row lock; login sai dùng fresh-scope persister theo
`userId`. Refresh dùng fresh executor lock User trước token, rotate/revoke family và commit outcome
trước khi trả lỗi. Forgot/reset password lock User trước OTP; lock-first không tạo/consume OTP hoặc
đổi password. Failed-login persister chỉ xử lý `ACTIVE` hoặc passenger
`PENDING_EMAIL_VERIFICATION`; auto-lock lưu origin vừa reload. Những race hợp lệ đều tuyến tính:
auth-first rồi lock revoke token vừa tạo, lock-first không token; unlock-first rồi failed-login tính
attempt mới, failed-login-first rồi unlock clean counter; không outcome nào verify email ngầm.

#### Immutable ActivityLog

Admin query ActivityLog theo actor/action/UTC `[from,to)`, order `createdAt DESC,id DESC`.
`ActivityLog.userId` là actor. Metadata lock/unlock chỉ chứa target/status fields; IP/user-agent là
cột riêng. Station consumers dùng `sourceEventId` unique cho dedupe. Repository chỉ Add/read và
PostgreSQL trigger chặn direct `UPDATE`/`DELETE`. Action mới: `UNLOCK_USER`, `STATION_MERGED`,
`STATION_NORMALIZED`.

#### Station normalize/merge

Admin Station PATCH giữ request hiện hữu và deterministic slug từ `name+city+province`; collision
dùng station-ID hash suffix. Normalize Station đã merged bị từ chối. Update và
`trip.station.normalized` Outbox là một transaction.

Merge nhận primary + duplicate, lock cả hai theo UUID ascending và recheck. Primary thắng
`name,slug,city,province`; `addressStreet,locationId,contactPhone,contactEmail,operatingHours,
facilities` chỉ fill khi primary null; coordinates là một cặp; `supportsShuttle` dùng OR. Cùng Trip
DB transaction relink OperatorStation, Route origin/destination,
AlternativeRoute destination, ShuttleTrip Station và redirect cũ. OperatorStation collision giữ
primary mapping, OR active, fill nullable config rồi bỏ duplicate mapping. Preflight Route
origin=destination/domain violation trả `STATION_MERGE_CONFLICT` và rollback toàn bộ. Duplicate được
deactivate, soft-delete, set `mergedIntoStationId=primary`; mọi chain flatten trực tiếp.

Internal Station lookup trả canonical active hoặc merged redirect (`isMerged`,
`canonicalStationId`); ordinary soft-delete/missing vẫn 404. `trip.station.merged` mang actor,
nullable request context, before/after allow-list snapshots và relink counts; snapshot không có
contact phone/email. Booking và Identity dùng durable queue riêng; operational logs không in full
payload, IP hoặc user-agent.

Booking lưu `booking_station_redirects` local, không cross-DB FK. Consumer xử lý replay, out-of-order
chain và poison cycle/self/over-32-hop; redirect + processed marker + active Booking relink cùng
transaction. Mọi Station writer (`CreateBooking`, round trip, edit pickup/dropoff) và consumer dùng
transaction advisory locks trên union Station IDs, UUID ascending; canonicalize request và fresh
Trip snapshot trước validation, edit reload Booking `FOR UPDATE`. Writer-first/consumer-first đều
kết thúc với không active Booking nào trỏ duplicate; Booking terminal/history không rewrite.

Identity consumer ghi `STATION_MERGED`/`STATION_NORMALIZED` với actor từ event, source event unique,
IP/user-agent ở cột audit; missing actor retry/DLQ, không tạo fake User.

#### Platform earned report

`GET /v1/admin/reports/platform` do Payment orchestration, `SYSTEM_ADMIN` only, bắt buộc RFC3339 UTC
`from/to`, half-open `[from,to)`, tối đa 366 ngày. Payment không đọc foreign DB:

- Booking source: `COMPLETED`, anchor `completedAt`, count + `SUM(totalAmount)`.
- Trip source: `COMPLETED`, anchor `completedAt`, count.
- Parcel source: `DELIVERY_CONFIRMED`, anchor `confirmedAt`, count + signed
  `SUM(depositPaidVnd + balancePaidVnd - refundedAmountVnd)`; forfeited deposit được báo cáo riêng và không cộng hai lần.

Ba source HTTP call chạy song song, timeout 5 giây. `SUM(BIGINT)` PostgreSQL là NUMERIC và phải
checked-convert group/total về Int64; Payment checked mọi phép cộng. Overflow trả
`REPORT_VALUE_OVERFLOW`, không wrap/saturate/partial. Missing operator summary giữ tên null.
`byOperator` là union IDs, sort net revenue giảm dần rồi ID; totals bằng sum breakdown; Parcel/net
revenue có thể âm. Timeout/upstream 5xx khác/payload invalid trả `UPSTREAM_UNAVAILABLE`, không cache
hoặc Payment write.

Day 40 dùng live query với partial indexes trên terminal timestamp + operator. Stats materialization,
Redis cache, Excel export và occupancy/cancellation/no-show analytics defer Day 42. Auto-merge bằng
geo/fuzzy matching vẫn v2; Day 40 chỉ System Admin merge thủ công.

---

## 7. Non-Functional Requirements

| Yêu cầu | Thông số |
|---|---|
| Concurrent users | ≥ 500 (booking + tracking đồng thời) |
| API response time | < 2 giây (p95) REST endpoints |
| GPS update latency | < 3 giây end-to-end (driver emit → passenger nhận) |
| Seat locking TTL | 10 phút (Redis TTL — hết timeout tự giải phóng ghế) |
| JWT access token TTL | 15 phút |
| Refresh token TTL | 30 ngày |
| Internal JWT TTL | 120 giây (service-to-service — tolerance cho Polly retry) |
| Email OTP TTL | 5 phút |
| Parcel delivery link TTL | 48 giờ |
| Edit pickup/dropoff cutoff | **2 giờ trước departure time** (hardcode toàn hệ thống) |
| Max seats per booking | **5 ghế** (ngăn đầu cơ) |
| VNPay payment timeout | Persisted `dueAt`; legacy fallback 15 phút. Booking dùng Trip seat-lock expiry (10 phút mặc định) |
| Wallet data type | BIGINT, đơn vị VND — không dùng float/decimal |
| Wallet top-up min | **10,000 VND** |
| Wallet top-up max | **Không giới hạn** — VNPay tự enforce theo policy của họ |
| Money rounding | Giữ đến đơn vị ĐỒNG — không floor 1,000; phép tính ra số lẻ làm tròn đến đồng gần nhất trước khi write DB (BSOT v1.11.0, quyết định 2026-06-12) |
| Data durability | Không mất dữ liệu booking/trip khi network lỗi (Outbox pattern cho critical events) |
| Multi-tenancy | Operator chỉ thấy dữ liệu `operatorId` của mình, enforce ở service layer (Internal JWT carry `operatorId`) |
| Soft delete | Dùng `isActive`/`deletedAt` thay hard delete cho: Operator, User, Station, Stop, Route, Vehicle |
| Timezone | Storage = UTC. Display = ICT (UTC+7). Tất cả API trả ISO 8601 với offset |
| Idempotency | Booking + Payment endpoints accept header `Idempotency-Key` (UUID) — Redis store key→response, TTL 24h |

---

## 8. Những điểm quan trọng cho Agent khác

### Enum cần define khi thiết kế DB (gợi ý khởi đầu)

> Agent thiết kế DB schema cần tự define enum values dựa trên business flows ở Section 6.

```
TripStatus:        SCHEDULED → BOARDING → IN_PROGRESS → COMPLETED
                   SCHEDULED → CANCELLED   (operator hủy trước BOARDING)
                   BOARDING  → CANCELLED   (operator hủy sau khi mở boarding nhưng trước IN_PROGRESS)
                   IN_PROGRESS → DISRUPTED (terminal — 2 case)
                   BOARDING trigger: Hangfire job auto-set 30 phút trước departureDateTime
                   DISRUPTED có 2 case (phân biệt qua presence của BookingTransfer record):
                     Case 1 — Vehicle Substitution: xe hỏng giữa đường, Operator điều xe khác.
                       Trip_old.status=DISRUPTED, BookingTransfer records created, không refund
                       (passenger được chuyển sang Trip_new). Xem 6.12.
                     Case 2 — Operator hủy IN_PROGRESS (bất khả kháng, không substitute): thiên tai,
                       tài xế khẩn cấp. Trip.status=DISRUPTED, KHÔNG có BookingTransfer.
                       Auto-refund proportional theo distanceFromOriginKm (fallback: stop-order) — xem section 8 decisions.
                       cancellationReason=OPERATOR_DISRUPTED_IN_PROGRESS.
                   Trip.source: MANUAL | AUTO_FROM_SCHEDULE | VEHICLE_SUBSTITUTION — phân biệt chuyến manual create vs Hangfire generate vs vehicle substitution. VEHICLE_SUBSTITUTION được set bởi 6.12 flow khi tạo Trip_new — exempt khỏi maxTripsPerMonth + không increment currentTripsThisMonth.
                   DELAYED là overlay flag (xe vẫn IN_PROGRESS nhưng trễ ETA), không phải status riêng

BookingStatus:     PENDING_PAYMENT → CONFIRMED → COMPLETED
                                  ↘ EXPIRED   (authoritative Payment deadline; captured late payment được refund)
                   CONFIRMED → CANCELLED → REFUNDED  (hủy chủ động + tiền về ví)
                   CONFIRMED → NO_SHOW               (tất cả passenger no-show, không hoàn tiền)
                   CONFIRMED → PARTIAL_NO_SHOW → COMPLETED  (Trip hoàn thành bình thường)
                                             ↘ DISRUPTED → REFUNDED
                                               (Trip DISRUPTED no-substitution; proportional refund trên full Booking.totalAmount)
                   CONFIRMED → CANCELLED              (operator hủy chuyến TRƯỚC IN_PROGRESS → sau đó REFUNDED khi wallet credited)
                   CONFIRMED → DISRUPTED → REFUNDED   (Trip bị DISRUPTED IN_PROGRESS không có vehicle substitution
                                                        → auto-refund proportional → REFUNDED khi wallet credited.
                                                        DISRUPTED chỉ dùng khi chuyến đã IN_PROGRESS bị gián đoạn không thay thế xe.
                                                        cancellationReason = OPERATOR_DISRUPTED_IN_PROGRESS.
                                                        Trigger: Booking Service consume TripDisrupted { hasSubstitution=false } → set DISRUPTED.
                                                        Booking Service consume WalletCredited → set REFUNDED.
                                                        Phân biệt với CANCELLED: CANCELLED chỉ dùng khi trip hủy trước IN_PROGRESS.)
                   REFUNDED trigger: Booking Service consume WalletCredited event
                   PARTIAL_NO_SHOW trigger: Hangfire job — tất cả Passenger trong booking có boardingStatus
                     IN (BOARDED, NO_SHOW) AND ít nhất 1 BOARDED AND ít nhất 1 NO_SHOW
                     (along-route: sau khi xe rời pickup stop > 15 phút; terminal: sau Trip.actualDepartureTime + 15 phút)
                   cancellationReason: USER_INITIATED | OPERATOR_CANCELLED_TRIP | OPERATOR_DISRUPTED_IN_PROGRESS
                                       | SCHEDULE_CHANGED | ROUTE_CHANGED_REFUSED | VEHICLE_SUBSTITUTION_DOWNGRADE
                                       | VEHICLE_SUBSTITUTION_NO_SEAT | STOP_DISABLED_REFUSED
                                   ⚠️ VEHICLE_SUBSTITUTION_DOWNGRADE và VEHICLE_SUBSTITUTION_NO_SEAT
                                      CHỈ dùng khi booking bị AUTO-CANCEL do xe thay thế không đáp ứng
                                      điều kiện (downgrade không chấp nhận / không đủ ghế).
                                      Substitution thành công (booking chuyển sang Trip_new) → Booking
                                      KHÔNG bị cancel, 2 giá trị này KHÔNG được set.
                   refundOverride: bool (true khi cancellation do operator → refund 100% bất kể cutoff)
                   bookingGroupId: UUID nullable — NULL cho single booking; shared giữa Booking_outbound + Booking_return
                   tripDirection: enum nullable (OUTBOUND | RETURN) — NULL cho single booking
                   (Pending confirmation dùng entity BookingPendingAction riêng — không có field trực tiếp trên Booking)

TopUpRequestStatus: PENDING → SUCCEEDED | FAILED | EXPIRED (15 phút timeout)

WalletTransactionType:  CREDIT | DEBIT
WalletTransactionRef:   TOP_UP | BOOKING_PAYMENT | BOOKING_REFUND | PARCEL_PAYMENT | PARCEL_REFUND | MANUAL_ADJUSTMENT
PlatformWalletTransactionType: CREDIT | DEBIT
PlatformWalletTransactionRef:  BOOKING_PAYMENT_HOLD | PARCEL_PAYMENT_HOLD | BOOKING_REFUND | PARCEL_REFUND
                               | TRIP_SETTLEMENT | SUBSCRIPTION_PAYMENT | MANUAL_ADJUSTMENT

KnowledgeDocumentStatus: PENDING_REVIEW | APPROVED | REJECTED | ARCHIVED
KnowledgeDocumentAccess: PUBLIC | OPERATOR | ADMIN
RagMessageRole:          USER | ASSISTANT

PaymentStatus:
  Wallet payment:    (INSERT với status=SUCCEEDED trực tiếp) → REFUNDED
                     Không qua PENDING_REDIRECT — wallet deduct + Payment INSERT xảy ra
                     trong cùng 1 DB transaction. Không có intermediate state.

  VNPay payment:     PENDING_REDIRECT → SUCCEEDED → REFUNDED
                                       ↘ FAILED
                                       ↘ EXPIRED

  REFUNDED trigger: Payment Service consume WalletCredited event
                     (referenceType=BOOKING_REFUND hoặc PARCEL_REFUND) → UPDATE Payment SET
                     status=REFUNDED, refundedAt=now WHERE bookingId/parcelId = :referenceId.
                     Align với BookingStatus.REFUNDED. Reporting filter Payment.REFUNDED để tính
                     refunded revenue. v1 chỉ trigger từ wallet refund, KHÔNG từ VNPay Refund API
                     (v2 feature).

ParcelStatus:
                   PENDING_OPERATOR_REVIEW    → PENDING_PAYMENT | REJECTED | CANCELLED
                   PENDING_PAYMENT            → RESERVED | EXPIRED | CANCELLED
                   PENDING (legacy)            → CANCELLED | PENDING_OPERATOR_ACTION
                   PENDING_ADDITIONAL_PAYMENT (legacy) → CANCELLED | PENDING_OPERATOR_ACTION | REJECTED
                   RESERVED                   → CHECKED_IN | REJECTED | CANCELLED | PENDING_OPERATOR_ACTION
                   CHECKED_IN                 → PENDING_FINAL_PAYMENT | READY_TO_LOAD | PENDING_OPERATOR_ACTION
                   PENDING_FINAL_PAYMENT      → READY_TO_LOAD | REJECTED | CANCELLED
                   READY_TO_LOAD              → LOADED | CANCELLED
                   LOADED                     → IN_TRANSIT | PENDING_TRANSFER_CONFIRM | PENDING_OPERATOR_ACTION
                   IN_TRANSIT                 → UNLOADED | PENDING_TRANSFER_CONFIRM | PENDING_OPERATOR_ACTION
                   PENDING_TRANSFER_CONFIRM   → LOADED | TRANSFER_ESCALATED   (Driver/Assistant target trip confirm / 30 phút timeout)
                   TRANSFER_ESCALATED         → PENDING_TRANSFER_CONFIRM | RETURNED
                   UNLOADED                   → DELIVERED_PENDING_CONFIRM
                   DELIVERED_PENDING_CONFIRM  → DELIVERY_CONFIRMED | DELIVERY_REJECTED
                   DELIVERY_REJECTED          → RETURN_INITIATED              (Hangfire sau 15 phút undo window)
                   PENDING_OPERATOR_ACTION    → pendingActionResumeStatus | RETURNED

                   Terminal: DELIVERY_CONFIRMED, RETURN_INITIATED, CANCELLED, EXPIRED, REJECTED, RETURNED.

                   Counter update (Trip.reservedParcelWeightKg + Trip.totalLoadedWeightKg) per transition — xem chi tiết section 6.6 phần (e).

VehicleStatus:     ACTIVE | MAINTENANCE | OFF_DUTY | RETIRED
                   ACTIVE = đang hoạt động, có thể assign vào DriverSchedule và Trip
                   MAINTENANCE = đang bảo dưỡng, Operator manual set khi đưa xe đi sửa.
                                  Hangfire job KHÔNG generate Trip cho vehicle MAINTENANCE.
                                  Trip đã generate với vehicle này: Operator phải swap vehicle manually.
                   OFF_DUTY = tạm nghỉ (không bảo dưỡng nhưng không chạy). Operator manual set.
                                  Tương tự MAINTENANCE về behavior, khác semantic (xe vẫn ổn, chỉ không xếp lịch).
                   RETIRED = đã thanh lý, không thể reactivate. Operator manual set, không generate Trip mới.
                              Soft delete — giữ record cho audit (Trip lịch sử vẫn ref vehicle này).
                   Transition: ACTIVE ↔ MAINTENANCE ↔ OFF_DUTY (free). ACTIVE/MAINTENANCE/OFF_DUTY → RETIRED (terminal).
                   Operator chịu trách nhiệm set status — KHÔNG có auto-transition trong v1.
OperatorRegistrationStatus:
                   PENDING | APPROVED | REJECTED | SUSPENDED
                   PENDING → APPROVED | REJECTED
                   APPROVED → SUSPENDED
                   SUSPENDED → APPROVED
                   REJECTED terminal cho request hiện tại
                   (Không dùng LOCKED cho Operator — LOCKED chỉ dành cho User.status)

UserStatus:        PENDING_EMAIL_VERIFICATION | PENDING_INITIAL_PASSWORD | ACTIVE | LOCKED | DELETED
                   PENDING_EMAIL_VERIFICATION → ACTIVE (passenger self-register verify OTP thành công)
                   PENDING_INITIAL_PASSWORD → ACTIVE (Driver/Assistant/OperatorStaff/sub-admin set password lần đầu qua email link)
                   ACTIVE → LOCKED (password lockout hoặc System Admin khóa)
                   LOCKED → ACTIVE (System Admin mở khóa)
                   ACTIVE/LOCKED → DELETED (soft delete/anonymize)
                   DELETED terminal trong v1
StopType:          KHÔNG cần — dùng 4 cột FK riêng cho pickup/dropoff (pickupStationId/pickupStopId/dropoffStationId/dropoffStopId),
                   mutually exclusive Station vs Stop. Không có discriminator enum trên Booking.
SeatStatus:        AVAILABLE | HELD | BOOKED | UNAVAILABLE
                   UNAVAILABLE = ghế hỏng physical (ghế bị disable từ seatLayoutJson.seats[].disabled=true)
                                 hoặc operator tạm khóa per-trip (sự cố nội thất sau khi sinh TripSeat)
                                 — không bao gồm BOOKED hay HELD
VoucherType:       PERCENT_OFF | FIXED_AMOUNT
VoucherFundingType: VIETRIDE_FUNDED | OPERATOR_FUNDED
OperatorVoucherConsentStatus: PENDING | ACCEPTED | REJECTED
DeliveryMethod:    TERMINAL_PICKUP  (v1 chỉ 1 giá trị, mở rộng v2)

Role:              PASSENGER | DRIVER | ASSISTANT | OPERATOR_STAFF | OPERATOR_ADMIN | SYSTEM_ADMIN
```

### Quyết định kiến trúc & scope (quick-reference)

| Quyết định | Giá trị |
|---|---|
| **Services** | 8 service nghiệp vụ + 1 API Gateway (NestJS). Identity+User gộp 1. Trip+Route+Vehicle gộp 1. Không có Search service riêng |
| **Service-to-service auth** | Internal JWT — Gateway ký, TTL 120s, HS256 shared secret |
| **Saga pattern** | Hybrid — sync HTTP cho core booking path, async RabbitMQ cho side-effects. Outbox đảm bảo durability |
| **Station vs Stop** | 2 entity riêng. Station = bến canonical cấp platform (không có operatorId). Stop = điểm dừng dọc tuyến thuộc operator. `Route.returnRouteId` self-FK link chiều về |
| **Voucher** | Platform-wide (System Admin tạo, cả `VIETRIDE_FUNDED` + `OPERATOR_FUNDED`) + operator self-create (OPERATOR_ADMIN tự tạo operator-owned `OPERATOR_FUNDED` voucher scoped công ty mình, self-consented, full CRUD). Xem 4.4 funding block |
| **Cancellation policy** | Operator-configured. Lưu `Operator.cancellationPolicy` JSONB array `[{hoursBeforeDeparture, feePercent}]`. Không có mức hardcoded toàn platform. |
| **Operator settlement model** | v1 wallet-internal (KHÔNG bank transfer). Doanh thu vào `PlatformWallet` holding pool, hold 7 ngày sau Trip terminal → Monday weekly auto-settle bằng cách debit `PlatformWallet` + credit `OperatorWallet`. Admin manual settle per-trip cũng support. Bank Withdrawal là v2. Xem 4.6. |
| **Parcel size** | Category enum (SMALL/MEDIUM/LARGE/EXTRA_LARGE) — không dùng dimensions |
| **`transportCompanyId` trên Parcel** | Không có — implicit qua `tripId` |
| **Không dùng** | Elasticsearch (→ pgvector), MongoDB (→ PostgreSQL JSONB), SMS (→ email link), Branch entity |
| **Frontend** | NextJS (App Router) cho Operator Web + Admin Web. Passenger App và Driver App là 2 app riêng |
| **File storage** | Firebase Storage (gộp project FCM) |
| **Timezone** | Storage UTC · Display ICT (UTC+7) · API ISO 8601 với offset · `departureTime TIME` lưu local ICT |
| **MediatR** | Pin v11.x (v12+ commercial license) |
| **LLM** | `claude-sonnet-4-6` |
| **BỎ** | Walk-in booking, CSV import, walk-in parcel — mọi booking/parcel chỉ qua Passenger App |

### Conventions quan trọng

**Data types:**
- **Tiền (VND):** lưu `BIGINT`, đơn vị đồng. Giữ nguyên đến đồng — KHÔNG floor 1,000 (BSOT v1.11.0); phép tính ra số lẻ làm tròn đến đồng gần nhất (287,341.6 → 287,342). Áp dụng cho fare, discount, refund. Không dùng DECIMAL/FLOAT.
- **Timestamps:** lưu UTC, trả về ISO 8601 với offset. `departureTime TIME` lưu local ICT.
- **Soft delete:** dùng `isActive`/`deletedAt` thay hard delete cho Operator, User, Station, Stop, Route, Vehicle.

**Entity design rules:**

| Entity / field | Rule |
|---|---|
| **Station vs Stop** | Station = bến canonical cấp platform, không có `operatorId`, có `supportsShuttle` flag. `OperatorStation` = mapping nhà xe khai thác bến. Stop = điểm dừng dọc tuyến do operator ký với bên thứ ba (quán ăn, điểm dừng), tạo bằng Google Maps Places suggest trên Operator Web. Stop có thể được suggest cross-operator. KHÔNG có shuttle tại Stop. `RouteStop` junction nối Route ↔ Stop (có `orderIndex`, `estimatedDurationFromOriginMinutes`, `distanceFromOriginKm` nullable — operator nhập thủ công). |
| **Route.returnRouteId** | Nullable self-FK — link tuyến chiều về, dùng cho DriverSchedule round-trip UX. |
| **Booking → Passenger** | 1-to-many, max 5. `Passenger` là **operational record** chỉ lưu seatNumber + boardingStatus + boardedAt + boardedAtStopId. KHÔNG lưu nhân thân (no fullName/phone/idNumber). Booking chỉ lưu thông tin người mua (buyer). DB constraint COUNT ≤ 5. |
| **Booking pickup/dropoff FK** | 4 cột: `pickupStationId` + `pickupStopId` (mutually exclusive, exactly one not null) và `dropoffStationId` + `dropoffStopId` (mutually exclusive, cả 2 null = default terminal đích). DB CHECK constraint. |
| **Booking schedule projection** | `tripSnapshotDeparture` / `trip_snapshot_departure` là immutable history snapshot. Nullable `tripCurrentDeparture` / `trip_current_departure` được backfill từ snapshot và chỉ tiến theo CAS `trip.trip.schedule_changed`; Booking `PENDING_PAYMENT|CONFIRMED` đều cập nhật projection, nhưng chỉ `CONFIRMED` tạo schedule facts/action. Query hiện tại và `STOP_DISABLED` deadline dùng current projection. |
| **Trip pickup/dropoff control** | KHÔNG có flag `allowAlongRoutePickup` / `allowAlongRouteDropoff` ở Trip. KHÔNG có `defaultAllowAlongRoute*` ở Route. `RouteStop` entries là single source of truth, với **2 flag per entry: `allowPickup` + `allowDropoff` (default cả 2 = true, DB CHECK ít nhất 1 = true)** — phân loại pickup-only / dropoff-only / both. TripStop snapshot 2 flag khi Hangfire generate Trip (immutable). Validation booking + parcel: pickupStopId cần `allowPickup=true`; dropoffStopId cần `allowDropoff=true`. Edge case operational stop defer v2 qua `RouteStop.isPublicVisible`. |
| **Trip fare** | `Trip.baseFare` = giá vé theo chặng/tuyến (áp dụng từ đầu tuyến hoặc bất kỳ along-route stop nào). Booking capture/reuse một handler-start `pricingAt` và dùng `fareFromThisStop` đã được Trip resolve trong snapshot cho pickup stop; explicit precedence là persisted `TripStopFare` có `source=MANUAL_OVERRIDE` → active `RouteStopFareTemplate` half-open tại `pricingAt` → `Trip.baseFare`. Legacy `TEMPLATE_SNAPSHOT` vẫn readable, non-authoritative khi có `pricingAt`, và chỉ tham gia resolution cho legacy compatibility khi omitted `pricingAt`; Day 22 không tạo snapshot row mới. Dropoff miễn phí tại mọi stop, không ảnh hưởng giá. |
| **Seat lock along-route** | Ghế lock từ đầu chuyến, không phân theo segment. Chi phí operator chấp nhận. |
| **DriverSchedule** | `dayOfWeek` JSON array + `departureTime TIME`. Hangfire generate Trip 14 ngày kế tiếp qua 2 trigger: (1) immediate on-create/activate, (2) weekly job CN 23:00. Idempotent check (driverId + departureDateTime). Không cho 2 schedule active cùng driverId overlap giờ. Vehicle conflict cũng check. |
| **Alternative Route** | Tối đa 2 per Route chính. Stop sequence riêng hoàn toàn — không reuse `RouteStop`. Quan hệ: `Route → AlternativeRoute → AlternativeRouteStop → Stop`. |
| **Stop độc lập** | Một Stop có thể thuộc nhiều Route. Canonical disable là bodyless `DELETE /v1/operator/stops/{stopId}?replacedByStopId=` cho `OPERATOR_ADMIN`, required UUID-v4 `Idempotency-Key`; set `isActive=false`, giữ `deletedAt`, không xóa RouteStop, và publish `trip.stop.disabled`. `replacedByStopId` nullable self-FK phải active/cùng operator/non-self/cycle-free. Retained PATCH là details-update-only, không đổi `isActive`/`deletedAt` và không emit disable event. Async `booking.stop_disabled.affected` là sole impact source; không có synchronous count/warning seam. Auto-suggest geo proximity defer v2. |
| **Trip cargo counters** | `Trip.totalLoadedWeightKg/VolumeM3` là cargo vật lý trên xe: Task load chuyển actual reservation → loaded cargo; unload/recovery release như flow hiện hữu. `Trip.reservedParcelWeightKg/VolumeM3` giữ mềm estimated cargo khi deposit payment bắt đầu, giữ qua `RESERVED`, rồi Task reweigh chuyển estimated → actual; payment/check-in/final-payment timeout hoặc terminal trước load release. **Available capacity = Vehicle max - estimated passenger luggage - reserved parcel cargo - loaded cargo**. Parcel/Trip dùng local transaction + CAS/idempotent internal mutation, không có cross-DB transaction. |
| **TripStop** | `{tripId, stopId, orderIndex, estimatedArrivalTime (static planned baseline), actualArrivalTime nullable, actualDepartureTime nullable, status PENDING\|ARRIVED\|SKIPPED, distanceFromOriginKm nullable}`. `actualDepartureTime` is the durable Day-24 stop-departure anchor; arrival/status remain authoritative for no-show. Approved pre-departure Route edit hoặc DriverSchedule `ALL_PENDING` cascade có thể recompute baseline; GPS/Tracking dynamic ETA không bao giờ update field này. Copy `distanceFromOriginKm` từ RouteStop khi generate — dùng cho DISRUPTED refund mà không cần join ngược. |
| **Trip.source** | `MANUAL \| AUTO_FROM_SCHEDULE \| VEHICLE_SUBSTITUTION`. Manual create: Operator nhập form → hệ thống generate TripSeat + TripStop; Day 22 không copy fare-template thành TripStopFare mới. Initial status: SCHEDULED nếu departure > 30 phút; BOARDING ngay nếu ≤ 30 phút (publish TripBoardingStarted ngay, không đợi Hangfire). Reject nếu departure trong quá khứ. **VEHICLE_SUBSTITUTION**: Trip_new tạo từ 6.12 Vehicle Substitution flow — Trip Service set source = VEHICLE_SUBSTITUTION explicitly khi INSERT; counter check `maxTripsPerMonth` skip + `currentTripsThisMonth` không increment (xem 4.5 c.0). |
| **RouteStopFareTemplate** | `{routeId, stopId, fareFromThisStop BIGINT, effectiveFrom datetime, effectiveUntil datetime nullable}` — **Exception override** cho stop có giá khác `Route.baseFare`. Operator chỉ tạo entry cho stop muốn config giá riêng — stop không có entry dùng baseFare. Day 22 không tạo `TEMPLATE_SNAPSHOT` khi Hangfire/manual generate Trip; legacy snapshot chỉ còn readable ở request không có `pricingAt`. Chỉ explicit operator per-Trip fare override tạo `MANUAL_OVERRIDE`. Với một handler-start `pricingAt`, precedence là `MANUAL_OVERRIDE` → active template half-open window → `Trip.baseFare`. Trip DB dùng `btree_gist` exclusion guard để không có overlapping window cùng `(routeId,stopId)`. |
| **Pricing config — future-dated + manual override** | Mọi entity pricing (`Route.baseFare`, `RouteStopFareTemplate`, `ParcelRouteFare`) hỗ trợ **effectiveFrom** và **effectiveUntil** datetime. Operator có thể config giá trước nhiều tháng (vd set giá Tết từ tháng 10). Đến thời điểm hiệu lực, hệ thống tự dùng giá mới. Operator có thể **manual override** giá per trip tại thời điểm tạo/sửa trip (không bị khóa bởi schedule). KHÔNG hardcode pricing trong code. |
| **Voucher** | Platform-wide. Fields: code, type, value, minOrderAmount, maxDiscountAmount, totalUsageLimit, perUserLimit, validFrom, validUntil, applicableOperatorIds, applicableRouteIds, isActive, **`fundingType` enum `VIETRIDE_FUNDED \| OPERATOR_FUNDED`**. `VoucherUsage` track per (voucherId, userId, bookingId, bookingGroupId, **`fundedBy` snapshot enum** = voucher.fundingType tại thời điểm apply — dùng cho settlement reconcile). DELETE khi CANCELLED/REFUNDED. Round-trip: 2 VoucherUsage records, limit check bằng `COUNT(DISTINCT bookingGroupId)`. **`OperatorVoucherConsent` entity**: track operator opt-in cho voucher OPERATOR_FUNDED — schema chi tiết ở Booking Service entity list (cùng service với Voucher để strict FK). Validation khi apply voucher: nếu `fundingType=OPERATOR_FUNDED` → check `OperatorVoucherConsent.status=ACCEPTED` cho `Trip.operatorId`; sai → error `VOUCHER_NOT_APPLICABLE`. |
| **BookingPendingAction** | `{id, bookingId FK, reason (ROUTE_CHANGE\|SEAT_DOWNGRADE\|SCHEDULE_CHANGE\|PENDING_SEAT_ASSIGNMENT\|STOP_DISABLED), severity (nullable, dùng cho SCHEDULE_CHANGE: MEDIUM\|MAJOR — KHÔNG include MINOR vì MINOR không persist record), deadline, resolvedAt nullable, resolvedAction nullable, metadata JSONB, createdAt}`. Partial unique: `UNIQUE(bookingId) WHERE resolvedAt IS NULL` — chỉ 1 active per booking. Action mới phát sinh: close action cũ với `SUPERSEDED` trước khi INSERT mới. Schedule metadata freeze exact `sourceEventId`, `oldDeparture`, `newDeparture`, `severity`, `initialDeadline`, nullable `terminalDeadline`, `refundBasisAmount`, `refundPercent`, `refundAmount`; refund basis là immutable `Booking.totalAmount`, money BIGINT và phép chia làm tròn `MidpointRounding.AwayFromZero`. Xem flow theo từng reason tại section 6.4 (ROUTE_CHANGE), 6.4.1 (STOP_DISABLED), 6.12 (PENDING_SEAT_ASSIGNMENT), 6.13 (SCHEDULE_CHANGE). |
| **Voucher code generation** | Code unique indexed. Admin tạo voucher có 2 cách: (a) nhập code thủ công (vd "TET2026"), (b) bấm "Auto generate" → BE gen 8 ký tự uppercase base32 unique (vd "VC7K2X9P"). UI radio chọn mode. BE validate uniqueness ở cả 2 case. |
| **Invoice (subscription)** | Entity thuộc Payment & Wallet Service. Track invoice VietRide xuất cho operator cho mỗi kỳ subscription (xem 4.5). v1: PDF only, không integrate e-invoice provider. v2: tích hợp VNPT/Misa/Viettel. |
| **BookingTransfer** | `{id, bookingId, passengerId, originalTripId, newTripId, originalSeatNumber nullable, newSeatNumber nullable, confirmationStatus PENDING_CONFIRM\|CONFIRMED\|NOT_REQUIRED, confirmedAt nullable, confirmedByUserId nullable, transferredAt, transferredByUserId, note nullable}`. **1 record per Passenger** của Booking — multi-passenger booking sẽ tạo nhiều BookingTransfer cùng `bookingId` khác `passengerId`. Created khi Vehicle Substitution (6.12); confirmation fields follow the frozen passenger-confirmation rules in that section. |
| **Round-trip** | `Booking.bookingGroupId` UUID nullable (shared giữa 2 booking). `Booking.tripDirection` nullable (OUTBOUND\|RETURN). Single booking cả 2 = NULL. Non-unique index trên bookingGroupId. |
| **ParcelRouteFare** | `{routeId, sizeCategory}` composite PK, `priceVnd BIGINT`. Operator define per route per size. |
| **Parcel.recipientUserId** | Nullable FK → User. Null = người nhận không có account (email link only). `senderUserId` NOT NULL. |

**Tenancy & inter-service:**
- `operatorId` là tenant identifier — mọi query phải filter theo `operatorId` từ Internal JWT.
- Inter-service: HTTP REST đồng bộ (kèm Internal JWT) hoặc RabbitMQ bất đồng bộ.
- MediatR chỉ dùng in-process trong .NET service, không dùng cho inter-service.

**Reliability:**
- **Outbox:** mỗi service có bảng `OutboxEvent {eventType, payload JSONB, status PENDING|PUBLISHING|PUBLISHED|FAILED, retryCount, lastError, createdAt, publishedAt}`. .NET dùng `BackgroundService` poll mỗi 5s. NestJS dùng BullMQ scheduled job poll mỗi 5s. Notification Service không có Outbox (chỉ consume).
- **Compensation:** Payment fail → HTTP release seat về Trip-Route-Vehicle. Authoritative Payment
  deadline → Hangfire release seat + EXPIRED; capture sau đó không hồi sinh Booking và được hoàn
  idempotent về ví.
- **Idempotency:** `Idempotency-Key` header (UUID) cho các mutation endpoints quan trọng. Redis `<service>:idem:{key}` TTL 24h.
- **Hangfire scope (.NET) — chỉ dùng cho business scheduled jobs** (không dùng cho Outbox polling):

| Service | Job |
|---|---|
| Booking | Seat release tại authoritative Payment deadline · schedule-change auto-accept · PENDING_SEAT_ASSIGNMENT escalation (interval 15 phút) |
| Trip-Route-Vehicle | Auto-BOARDING 30 phút trước departure · COMPLETED fallback +30 phút sau ETA · Generate Trip từ DriverSchedule (CN 23:00) |
| Parcel | Undo-reject 15 phút · cancel EXTRA_LARGE review timeout 24h · reject/forfeit RESERVED quá `latestCheckInAt` · reject/forfeit PENDING_FINAL_PAYMENT quá `finalPaymentDeadline` (interval 5 phút) · PENDING_TRANSFER_CONFIRM escalation 30 phút · stale Day-32 cargo-recovery operation replay 5 phút · PENDING_OPERATOR_ACTION re-alert 2h |
| Payment | PENDING_REDIRECT expired khi `dueAt ?? createdAt + 15 phút <= now` · TopUpRequest expired 15 phút · **Trip settlement eligibility flag (daily 02:00)** — set `OperatorTripSettlement.status=ELIGIBLE` khi `eligibleAt <= now` · **Trip settlement weekly auto-settle (Monday 09:00 weekly)** — debit PlatformWallet + credit OperatorWallet cho mọi settlement ELIGIBLE · Subscription trial expire check (daily 00:30) · Trial expiring T-3 days warn (daily 09:00) · Subscription upgrade attempt hết hạn sau 15 phút và reconciliation chạy mỗi phút · Subscription paid invoice generation post-payment-success (event-driven, không phải scheduled — nhưng retry via Hangfire nếu PDF gen fail) |
| Identity | OTP expired cleanup (optional) · FCM token stale cleanup (weekly) |

**Redis namespace conventions (canonical — tránh conflict cross-service):**

Mọi key dùng pattern `<service>:<purpose>:<id>` để namespace per service. Cùng 1 Redis instance chia sẻ, không có DB number isolation.

| Key pattern | Owner service | Purpose | TTL |
|---|---|---|---|
| `identity:otp_rate:{email}` | Identity | Rate limit OTP request (max 3/giờ) | 1 giờ |
| `identity:pwd_reset_rate:{email}` | Identity | Rate limit password reset (max 3/giờ) | 1 giờ |
| `identity:jwks_cache` | Gateway, Tracking | JWKS public key cache | 1 giờ |
| `identity:login_lockout:{userId}` | Identity | Counter failed login (window 15 phút) | 15 phút |
| `gateway:rate_limit:{ip}:{route}` | Gateway | API rate limit per IP per route | 1 phút |
| `gateway:internal_jwt:{kid}` | Gateway | Internal JWT signing key cache (nếu rotate) | 1 giờ |
| `booking:seat_lock:{tripId}:{seatNumber}` | Booking | Seat hold trong checkout flow | 10 phút |
| `booking:idem:{idempotencyKey}` | Booking | Idempotency-Key response cache | 24 giờ |
| `payment:idem:{idempotencyKey}` | Payment | Idempotency-Key response cache | 24 giờ |
| `payment:vnpay_ipn:{vnpTxnRef}` | Payment | Dedupe IPN callback | 24 giờ |
| `parcel:idem:{idempotencyKey}` | Parcel | Idempotency-Key response cache | 24 giờ |
| `tracking:latest:{tripId}` | Tracking | Last known GPS position per trip | 5 phút |
| `tracking:gps_buffer:{tripId}` | Tracking | GPS trail buffer (list) trước batch write | đến khi flush |
| `tracking:eta:{tripId}:{stopId}` | Tracking | Dynamic ETA cached | 60 giây |
| `tracking:off_route_since:{tripId}` | Tracking | Off-route timer (start time) | đến khi clear |
| `tracking:active_trips` | Tracking | Set of active tripIds (membership) | — |
| `tracking:approaching_notified:{tripId}:{bookingId}:w{1\|2}` | Tracking | Dedupe approaching alert (2 waves) | đến hết chuyến |
| `notification:fcm_token_blacklist:{token}` | Notification | Cache invalid FCM tokens | 1 ngày |
| `rag:embed_cache:{queryHash}` | RAG AI | Cache query embedding | 1 ngày |

Mỗi service chỉ được phép read/write key bắt đầu bằng prefix service của mình. Không cross-service read/write Redis trực tiếp (tránh tight coupling). Ngoại lệ: `identity:jwks_cache` được Gateway và Tracking đọc (read-only public key).

**External integrations:**
- **VNPay IPN** (`POST /v1/payments/vnpay-ipn`, `/vnpay-topup-ipn`): verify HMAC-SHA512. Idempotent — Payment đã SUCCEEDED thì return `{"RspCode":"00"}` không update lại. IP whitelist tại Nginx. ReturnUrl chỉ query status, không xử lý business logic.
- **GPS persistence:** latest position lưu Redis TTL 5 phút + buffer list per trip. BullMQ batch write vào PostgreSQL `GpsTrail` mỗi 5 phút.
- **Parcel deliveryToken:** revoke token cũ khi resend — không để nhiều token active cùng lúc.
- **Tracking room authorization:** client emit `joinTripTracking {tripId}` → Tracking Service verify user là passenger/driver/assistant/parcel sender/parcel recipient/operator của trip đó → join room `trip:{tripId}`. ACCESS_DENIED nếu không match.

### Code & API Conventions

**Solution / project layout (.NET services):**
- Mỗi service là 1 solution riêng với folder layout: `src/<Service>.Api`, `src/<Service>.Application` (CQRS handlers, DTOs, validators), `src/<Service>.Domain` (entities, value objects, domain events), `src/<Service>.Infrastructure` (EF Core, external integrations). `tests/<Service>.UnitTests`, `tests/<Service>.IntegrationTests`.
- Tên solution: `VietRide.<Service>.sln` (vd `VietRide.Booking.sln`).

**Project layout (NestJS services):**
- Folder convention: `src/modules/<feature>/` chứa controller, service, dto, entity. `src/common/` chứa Guard, Interceptor, Filter shared. `src/config/` chứa env loader.
- Per service: `<service-name>` lowercase (vd `tracking-service`, `notification-service`).

**API endpoint conventions:**
- **Public API:** prefix `/v1/` (vd `POST /v1/bookings`, `GET /v1/parcels/available-trips`). Versioning bắt buộc cho mọi public endpoint.
- **Internal service-to-service API:** prefix `/internal/v1/` (vd `POST /internal/v1/trips/{id}/lock-seats`). Internal endpoints require Internal JWT verification, **NOT exposed qua Gateway** ra public. Mỗi service có middleware reject request đến `/internal/*` nếu không có valid Internal JWT.
- **Health endpoints:** `/health` (liveness — luôn 200 nếu process alive), `/ready` (readiness — check DB + RabbitMQ + Redis connection). Không version prefix, không require auth.

**Naming conventions:**
- REST resources: plural noun lowercase (vd `/bookings`, `/trips`, `/parcels`, `/wallet/transactions`)
- Action endpoints: verb-noun với hyphen (vd `POST /bookings/{id}/cancel`, `POST /trips/{id}/lock-seats`)
- Query params: camelCase (vd `?passengerUserId=xxx&from=2026-01-01`)
- JSON body fields: camelCase (vd `{ "tripId": "...", "totalAmount": 350000 }`)
- DB column names: snake_case (vd `passenger_user_id`, `total_amount`) — EF Core/Prisma tự map giữa camelCase property và snake_case column qua naming policy.

**Error response — ADR 0004 ApiResponse error envelope:**
- Content-Type: `application/json`
- Body shape: `{ success: false, statusCode, error: { code, message, fields? }, meta: { traceId, timestamp } }`
- `error.code` là string canonical viết SCREAMING_SNAKE_CASE — frontend dev dùng để map UI message
- Error code list khởi đầu (mở rộng khi gặp use case mới):
  - **Auth:** `AUTH_INVALID_CREDENTIALS`, `AUTH_TOKEN_EXPIRED`, `AUTH_TOKEN_INVALID`, `AUTH_GOOGLE_TOKEN_INVALID`, `AUTH_EMAIL_NOT_VERIFIED`, `AUTH_ACCOUNT_LOCKED`, `AUTH_OTP_INVALID`, `AUTH_OTP_EXPIRED`, `AUTH_EMAIL_ALREADY_REGISTERED`, `AUTH_PHONE_ALREADY_REGISTERED`, `AUTH_PHONE_REQUIRED`, `AUTH_PHONE_INVALID_FORMAT`
  - **Booking:** `BOOKING_SEAT_UNAVAILABLE`, `BOOKING_TRIP_NOT_BOOKABLE`, `BOOKING_CUTOFF_EXCEEDED`, `BOOKING_MAX_SEATS_EXCEEDED`, `BOOKING_NOT_FOUND`, `BOOKING_NOT_CANCELLABLE`, `BOOKING_EDIT_PICKUP_PRICE_INCREASE`, `BOOKING_NOT_FOR_THIS_TRIP` (QR scan booking khác trip), `BOOKING_PASSENGER_ALREADY_BOARDED`, `BOOKING_ROUND_TRIP_INVALID` (return trip không hợp lệ — không return route, departure trùng outbound, etc.)
  - **Voucher:** `VOUCHER_NOT_FOUND`, `VOUCHER_EXPIRED`, `VOUCHER_NOT_APPLICABLE`, `VOUCHER_USAGE_LIMIT_REACHED`, `VOUCHER_USER_LIMIT_REACHED`, `VOUCHER_MIN_ORDER_NOT_MET`
  - **Payment:** `PAYMENT_INSUFFICIENT_WALLET`, `PAYMENT_VNPAY_ERROR`, `PAYMENT_TIMEOUT`, `PAYMENT_ALREADY_PROCESSED`, `PAYMENT_SIGNATURE_INVALID` (VNPay HMAC verify fail)
  - **Wallet:** `WALLET_INSUFFICIENT_BALANCE`, `WALLET_TOP_UP_FAILED`, `WALLET_TOP_UP_AMOUNT_TOO_LOW`
  - **Trip:** `TRIP_NOT_FOUND`, `TRIP_NOT_EDITABLE`, `TRIP_VEHICLE_CONFLICT`, `TRIP_DRIVER_CONFLICT`, `TRIP_ROUTE_CHANGE_BOOKINGS_EXIST`, `TRIP_VEHICLE_SWAP_HELD_SEAT_CONFLICT`, `TRIP_VEHICLE_SWAP_TOO_LATE`, `TRIP_NOT_ACCEPTING_PARCEL` (Trip IN_PROGRESS — không nhận parcel mới), `TRIP_NOT_IN_PROGRESS`, `TRIP_STOP_NOT_ARRIVED`, `TRIP_STOP_ALREADY_DEPARTED`, `DRIVER_SCHEDULE_EDIT_TOO_LATE`
  - **Parcel:** `PARCEL_NOT_FOUND`, `PARCEL_CAPACITY_EXCEEDED`, `PARCEL_PRICING_NOT_CONFIGURED`, `PARCEL_DELIVERY_TOKEN_INVALID`, `PARCEL_DELIVERY_TOKEN_EXPIRED`, `PARCEL_NOT_TRANSFERABLE` (parcel ở status sai khi confirm transfer), `PARCEL_ADDITIONAL_PAYMENT_REQUIRED` (cân lại > ước lượng, cần thanh toán thêm), `PARCEL_REVIEW_TIMEOUT` (EXTRA_LARGE auto-reject 24h)
  - **Stop/Route:** `STOP_NOT_FOUND`, `STOP_REPLACEMENT_INVALID`, `STOP_REPLACEMENT_CYCLE` (replacedByStopId tạo cycle), `STOP_REPLACEMENT_DIFFERENT_OPERATOR`, `STOP_ALREADY_DISABLED`, `STOP_DISABLED_BOOKING_AFFECTED` (legacy/deprecated synchronous warning/count for DELETE; unrelated usages remain unchanged), `STOP_NOT_PICKUP_ALLOWED`, `STOP_NOT_DROPOFF_ALLOWED`, `ROUTE_NOT_FOUND`, `ROUTE_RETURN_NOT_CONFIGURED` (Route.returnRouteId null khi đặt round-trip)
  - **Station:** `STATION_NOT_FOUND`, `STATION_DUPLICATE_NEARBY` (warning khi operator tạo Station mới quá gần Station hiện có — gợi ý link thay vì tạo), `STATION_MERGE_CONFLICT` (merge vi phạm Route/domain invariant; không partial write)
  - **Invoice:** `INVOICE_NOT_FOUND`, `INVOICE_PDF_GENERATION_FAILED`
  - **Operator:** `OPERATOR_DUPLICATE_REGISTRATION` (businessRegistrationNumber đã tồn tại), `OPERATOR_DUPLICATE_TAX_CODE` (taxCode đã tồn tại)
  - **Auth:** `AUTH_INITIAL_PASSWORD_TOKEN_INVALID`, `AUTH_INITIAL_PASSWORD_TOKEN_EXPIRED`, `AUTH_PENDING_INITIAL_PASSWORD` (account chưa set password lần đầu, không login được)
  - **Subscription:** `SUBSCRIPTION_LIMIT_EXCEEDED` (vượt maxVehicles/maxRoutes/etc.), `SUBSCRIPTION_MODULE_DISABLED` (module flag = false, ví dụ thử dùng Parcel khi enableParcel=false), `SUBSCRIPTION_EXPIRED`, **`SUBSCRIPTION_PAYMENT_PENDING`** (operator đang có PENDING_PAYMENT, phải resolve trước khi upgrade tiếp)
  - **Settlement / Wallet (Platform & Operator):** `TRIP_SETTLEMENT_NOT_FOUND`, `TRIP_SETTLEMENT_ALREADY_SETTLED` (status = SETTLED/CANCELLED, admin manual retry không hợp lệ), `PLATFORM_WALLET_INSUFFICIENT_BALANCE` (PlatformWallet không đủ balance để refund/settle; alert System Admin). Note: `WALLET_INSUFFICIENT_BALANCE` đã có ở mục Wallet bên trên — dùng chung cho cả passenger Wallet và Operator Wallet adjust DEBIT (4.6 admin endpoint).
  - **Refund:** `REFUND_FAILURE_PERSISTED` (refund retry exhausted, Admin manual handle), `REFUND_RETRY_EXHAUSTED` (Hangfire job đã retry 5 lần)
  - **Tracking:** `TRACKING_ACCESS_DENIED` (joinTripTracking unauthorized), `TRACKING_TRIP_NOT_ACTIVE` (trip chưa IN_PROGRESS)
  - **RAG:** `RAG_DOCUMENT_NOT_APPROVED`, `RAG_ACCESS_DENIED_FOR_ROLE`
  - **Validation:** `VALIDATION_ERROR` (kèm `errors` array detail field-level)
  - **Generic:** `RESOURCE_NOT_FOUND`, `FORBIDDEN`, `RATE_LIMITED`, `REPORT_VALUE_OVERFLOW` (report aggregate ngoài Int64), `UPSTREAM_UNAVAILABLE`, `INTERNAL_ERROR`

**Request/response DTO shape (canonical):**
- `POST /v1/bookings` request: `{ tripId, pickup: { stationId?: string, stopId?: string }, dropoff?: { stationId?: string, stopId?: string }, seats: [{ seatNumber }], voucherCode?, paymentMethod: "WALLET" | "VNPAY" }`
- `POST /v1/bookings` response: `{ bookingId, bookingCode, status, totalAmount, discountAmount, paymentRedirectUrl? (chỉ VNPay) }`
- API DTO/controller contract chi tiết cho scaffold nằm ở `Docs/API/VietRide_API_Contract_v1.md`. Convention chung vẫn là nested object cho complex relationships, array cho list, ISO 8601 với offset cho datetime, BIGINT VND trả về là number (JS có thể safe cho < 2^53).

**Repository / data access pattern:**
- .NET: EF Core DbContext per service. Repository interface trong Domain layer, implementation trong Infrastructure. CQRS Query handler có thể bypass repository và query DbContext trực tiếp (read-only, performance).
- NestJS: Prisma repository pattern. Service class wrap repository, không gọi repository trực tiếp từ controller.

### Authentication & Authorization — Business rules chi tiết

**Password:** min 8 ký tự, phải có ít nhất 1 chữ cái + 1 chữ số. Hash bcrypt cost 12. Không lưu plaintext. Password change require verify mật khẩu cũ trước.

**Account lockout:** 5 lần sai password trong window 15 phút → `User.status = LOCKED` tự động. LOCKED không nhận reset link, không auto-unlock theo timeout — chỉ System Admin mở khóa thủ công. Login thành công reset counter. Lưu `failedLoginAttempts` + `lastFailedLoginAt` trên User entity; kiểm tra window trước mỗi lần increment.

**Email OTP — 2 lớp bảo vệ:**
- **Rate limit** (chống spam): tối đa 3 request/giờ per email. Redis key `otp_rate:{email}` TTL 1 giờ. OTP cũ tự invalidate khi tạo mới.
- **Brute-force** (chống đoán code): `EmailVerificationToken.failedAttempts` tăng mỗi lần nhập sai, invalidate sau 5 lần.

**User-Operator relationship:**
- `User.operatorId` nullable FK. Bắt buộc cho DRIVER/ASSISTANT/OPERATOR_STAFF/OPERATOR_ADMIN. NULL cho PASSENGER và SYSTEM_ADMIN.
- `ON DELETE RESTRICT` — không xóa Operator khi còn User active.
- OPERATOR_STAFF/OPERATOR_ADMIN không được đổi `operatorId` — phải tạo user mới ở operator mới. DRIVER/ASSISTANT có thể đổi (audit log).

**Refresh token rotation:**
- Mỗi lần dùng → revoke token cũ, issue token mới cùng family. Grace period 30s (tolerate parallel refresh từ mobile).
- Reuse detection: token đã revoked quá grace period được dùng lại → revoke toàn bộ family → force re-login.
- `RefreshToken` fields: `familyId UUID`, `parentTokenId nullable self-FK`, `revokedAt nullable`, `revokedReason enum (NORMAL_ROTATION | REUSE_DETECTED | USER_LOGOUT | ADMIN_REVOKE | PASSWORD_RESET)`.

**Password reset flow:**

`POST /v1/auth/forgot-password { email }` — public, luôn trả 200 (chống email enumeration). Chỉ gửi OTP nếu `User.status = ACTIVE` và `passwordHash NOT NULL` (Google-only account không có password, bỏ qua). INSERT `EmailVerificationToken { purpose=PASSWORD_RESET, code=6-digit OTP, expiresAt=now+5m }`. Rate limit: Redis `identity:pwd_reset_rate:{email}` max 3/giờ.

`POST /v1/auth/reset-password { email, code, newPassword }` — public. Lookup `PASSWORD_RESET` OTP hợp lệ (không expired, chưa dùng, failedAttempts < 5) cho email. Trong transaction: update passwordHash, mark token used, revoke tất cả RefreshToken của user (`revokedReason=PASSWORD_RESET`). Password reset **không unlock** LOCKED account — chỉ Admin unlock.

**Google OAuth + Email/password linking:**

`User.email` UNIQUE. Khi Google callback với `{ email, googleSub, displayName, avatarUrl }`:
- OAuthIdentity đã có → login bình thường.
- OAuthIdentity chưa có, email đã tồn tại → auto-link tạo OAuthIdentity, login với account cũ, push in-app banner thông báo 1 lần.
- OAuthIdentity chưa có, email chưa tồn tại → tạo User mới `status=ACTIVE` (Google đã verify email, không cần OTP).

Password login và Google login cùng trả `User.avatarUrl` đã lưu. Google provider avatar chỉ seed
khi tạo User mới; re-login/link account không ghi đè avatar hiện hữu. `avatarUrl = null` bị omit
khỏi auth response.

Email/password registration: tạo User `status=PENDING_EMAIL_VERIFICATION` → gửi OTP → verify → `status=ACTIVE`. Passenger mobile có thể login trước verify trong restricted session; các non-passenger account chỉ login sau khi thành `ACTIVE`.

### Entity Requirements per Service

> Phần dưới liệt kê **entity cần có per service** kèm vai trò + business field bắt buộc (field có ý nghĩa nghiệp vụ hoặc rule lifecycle). Agent DB schema phase tự quyết FK details, column types, indexes, constraints.

#### Identity & User Service

- **`User`** — Account + profile. Email UNIQUE, phone REQUIRED + UNIQUE cho mọi role trừ SYSTEM_ADMIN (format E.164 VN). Hỗ trợ Google OAuth (passwordHash nullable). Role-based với status lifecycle (xem 5.3.1). Soft delete.
- **`RefreshToken`** — Refresh token rotation (xem 5.2). Family-based với reuse detection.
- **`EmailVerificationToken`** — Token cho 3 purpose: REGISTRATION, PASSWORD_RESET, SET_INITIAL_PASSWORD (TTL 48h). Có rate-limit Redis + brute-force counter (max 5 failed attempts).
- **`OAuthIdentity`** — Map User ↔ Google OAuth identity. Auto-link khi email match.
- **`Operator`** — Nhà xe. Business fields: `businessRegistrationNumber` UNIQUE, `taxCode` UNIQUE, address chi tiết, registrationStatus (xem 5.3.1). Bank account info (nullable, **defer toàn bộ UI/validation sang v2** khi implement Bank Withdrawal — fields tồn tại trên schema để chuẩn bị nhưng v1 KHÔNG dùng).
  - **`cancellationPolicy` JSONB** — array `[{hoursBeforeDeparture, feePercent}]`.
  - **`parcelNoShowPolicy` JSONB** — `{ noShowFeePercent, additionalPaymentTimeoutMinutes }`, default `{ 0, 30 }`.
  - **`luggagePolicy` JSONB** — `{ defaultLuggageKgPerSeat }`, default `{ 10 }`.
- **`SubscriptionPlan`** — Gói SaaS với resource limits + module flags (xem 4.5).
- **`OperatorSubscription`** — 1-1 với Operator. `activePlanId` luôn là plan đang cấp entitlement; target plan và payment session nằm trong `SubscriptionUpgradeAttempt`.
- **`ActivityLog`** — Audit log user action (xem 4.4).
- **`UserDevice`** — FCM token per device (xem FCM Token Lifecycle bên dưới).

#### Booking Service

- **`Booking`** — Vé của 1 buyer. Lưu trip snapshot fields (origin/dest name, departure, route name) để render history mà không cross-service call. `bookingGroupId` + `tripDirection` cho round-trip. `cancellationReason`, `refundOverride` cho refund logic.
- **`BookingPendingAction`** — Pending confirmation cần passenger phản hồi. Reason: ROUTE_CHANGE | SEAT_DOWNGRADE | SCHEDULE_CHANGE | PENDING_SEAT_ASSIGNMENT | STOP_DISABLED. Có severity, deadline, resolvedAction. Chỉ 1 active per booking.
- **`Passenger`** — Sub-entity của Booking (1–5 record per booking). **Operational-only** — KHÔNG lưu nhân thân. Track `seatNumber`, `boardingStatus`, `boardedAt`, `boardedAtStopId`.
- **`BookingTransfer`** — Track chuyển từng Passenger từ Trip_old sang Trip_new khi Vehicle Substitution (xem 6.12). 1 record per Passenger.
- **`BookingStats`** — Counter table cho reporting (consume booking lifecycle events).
- **`Voucher`** — Platform voucher (chỉ System Admin tạo). Có `fundingType` VIETRIDE_FUNDED | OPERATOR_FUNDED, usage limits, applicable scope (route/operator).
- **`VoucherUsage`** — Track 1 lần apply voucher per booking. Lưu `fundedBy` snapshot tại thời điểm apply (để reconcile sau này nếu voucher đổi fundingType).
- **`OperatorVoucherConsent`** — Track opt-in của operator cho voucher OPERATOR_FUNDED (PENDING | ACCEPTED | REJECTED). Unique per `(operatorId, voucherId)`.
- **`OutboxEvent`**.

#### Trip-Route-Vehicle Service

- **`Station`** — Bến canonical platform-wide (không có operatorId). Có `supportsShuttle` flag.
- **`OperatorStation`** — Mapping nhà xe ↔ bến (1 bến có thể được nhiều operator khai thác). Lưu thông tin riêng (counterLocation, hotline, instructions).
- **`Stop`** — Điểm dừng dọc tuyến do operator ký với bên thứ ba. Tạo qua Google Places suggest. Có `replacedByStopId` cho disable flow.
- **`Route`** — Tuyến chính của 1 operator: origin/destination Station, `baseFare`, `totalDistanceKm`, `estimatedDurationMinutes`, `returnRouteId` self-FK link chiều về.
- **`RouteStop`** — Junction Route ↔ Stop dọc tuyến. Có `orderIndex`, `distanceFromOriginKm` nullable, `allowPickup` + `allowDropoff` flags (CHECK ít nhất 1 = true).
- **`RouteStopFareTemplate`** — Exception override `baseFare` per stop với effective time window (effectiveFrom/Until).
- **`AlternativeRoute`** + **`AlternativeRouteStop`** — Tuyến thay thế khi route change. Max 2 alternative per Route chính. Stop sequence riêng.
- **`VehicleType`** — Loại xe: code unique, displayName, `estimatedPassengerLuggageKgPerSeat` override (optional), `isSystemDefined` block delete. Seed STANDARD_BUS / LIMOUSINE / SLEEPER_BUS.
- **`Vehicle`** — Xe của operator: vehicleType, licensePlate, `seatLayoutJson` (xem 6.1 contract), totalSeats, maxCargoWeightKg, status (xem VehicleStatus enum).
- **`Trip`** — Chuyến cụ thể. Source: MANUAL | AUTO_FROM_SCHEDULE | VEHICLE_SUBSTITUTION. Snapshot baseFare + cargo limits từ Vehicle/Route; `notes VARCHAR(2000) NULL` trim/blank-to-null. 2 cargo counter: `reservedParcelWeightKg` + `totalLoadedWeightKg`. `estimatedPassengerLuggageKg` snapshot immutable. `hasSubstitution` flag cho reporting.
- **`TripSeat`** — Trạng thái từng ghế per trip: AVAILABLE | HELD | BOOKED | UNAVAILABLE.
- **`TripStop`** — Snapshot từ RouteStop khi generate Trip. Có `estimatedArrivalTime` (static planned baseline; approved pre-departure Route edit hoặc DriverSchedule `ALL_PENDING` cascade có thể recompute, GPS/Tracking không update), `actualArrivalTime` (set bởi Assistant), status PENDING | ARRIVED | SKIPPED, copy `distanceFromOriginKm` + 2 allow flags.
- **`TripStopFare`** — Exception override fare per trip per stop, với `source=TEMPLATE_SNAPSHOT|MANUAL_OVERRIDE`; existing rows backfill `TEMPLATE_SNAPSHOT`, nhưng Day 22 không tạo snapshot mới; chỉ explicit operator per-Trip override tạo `MANUAL_OVERRIDE`.
- **`DriverSchedule`** — Assignment driver/assistant ↔ vehicle ↔ route theo recurring pattern (dayOfWeek + departureTime). Dùng để Hangfire generate Trip.
- **`DriverScheduleAuditLog`** — Append-only local audit keyed to DriverSchedule; Day-22 action `DRIVER_SCHEDULE_EDITED`, metadata `{changedFields,before,after,requestId}`; actor là logical Identity user, không cross-DB FK.
- **`TripGenerationSkipLog`** — Log khi Hangfire skip generate Trip (vd vượt `maxTripsPerMonth` của subscription).
- **`ShuttleTrip`** + **`ShuttlePassenger`** — Xe trung chuyển + manifest passenger. Xem 6.14.
- **`Incident`** — Báo cáo sự cố từ Driver (category, description, photo URLs, GPS). Operator quyết action.
- **`OutboxEvent`**.

#### Payment & Wallet Service

- **`Payment`** — Mọi giao dịch thanh toán. referenceType: BOOKING | BOOKING_GROUP | PARCEL | PARCEL_ADDITIONAL | TOP_UP | SUBSCRIPTION. Method: WALLET (instant SUCCEEDED) | VNPAY (qua PENDING_REDIRECT). Có `vnpayTxnRef` UNIQUE, idempotencyKey. Nullable persisted `dueAt` là authoritative deadline; request omit dùng default 15 phút và historical persisted-null row dùng `createdAt + 15 phút`.
- **`TopUpRequest`** — Wallet (passenger) top-up qua VNPay (xem 6.5).
- **`Wallet`** + **`WalletTransaction`** — Ví hành khách. **`Wallet.userId` là PK natural** (1-1 với User, logical FK identity.users — cùng pattern `OperatorWallet.operatorId` PK). Balance BIGINT không âm, `rowVersion` optimistic lock. `WalletTransaction.userId` là logical FK (không hard FK tới Wallet — mirror `OperatorWalletTransaction` pattern). Transaction immutable với balanceBefore/balanceAfter snapshot (audit + optimistic lock). Bootstrap qua event `identity.user.created` consume (UPSERT idempotent).
- **`Invoice`** — Subscription invoice (VietRide → Operator): invoiceNumber UNIQUE format `VR-INV-yyyyMM-XXXXXX`, period, amount, status DRAFT | ISSUED | CANCELLED, pdfUrl. v2: e-invoice provider integration.
- **`PlatformWallet`** — Singleton clearing/holding pool của VietRide (xem 4.6). Balance BIGINT không âm, `rowVersion` optimistic lock. Ghi nhận tiền booking/parcel đang hold, refund về PassengerWallet, subscription payment thuộc VietRide, và settlement transfer sang OperatorWallet. Không phải ví người dùng thao tác trực tiếp.
- **`PlatformWalletTransaction`** — Immutable ledger của PlatformWallet. Fields: `type` CREDIT | DEBIT, `amount` positive, `balanceBefore`/`balanceAfter`, `referenceType` BOOKING_PAYMENT_HOLD | PARCEL_PAYMENT_HOLD | BOOKING_REFUND | PARCEL_REFUND | TRIP_SETTLEMENT | SUBSCRIPTION_PAYMENT | MANUAL_ADJUSTMENT, `referenceId`, `note`, `createdAt`. Atomic INSERT với UPDATE PlatformWallet.
- **`OperatorLedgerEntry`** — **Audit log** per-event của doanh thu/refund/voucher per operator per trip (xem 4.6). Fields: `operatorId`, `tripId` nullable (NULL cho ADJUSTMENT/MANUAL không gắn trip), `entryType` enum BOOKING_REVENUE | PARCEL_REVENUE | BOOKING_REFUND | PARCEL_REFUND | VOUCHER_VIETRIDE_FUNDED_CREDIT | VOUCHER_OPERATOR_FUNDED_AUDIT | ADJUSTMENT, `amount` signed, `referenceType` BOOKING | PARCEL | VOUCHER_USAGE | MANUAL, `referenceId`, `note`. **KHÔNG có `balanceBefore`/`balanceAfter`** — balance concept thuộc về OperatorWallet, ledger entries audit-only.
- **`OperatorWallet`** — Ví nội bộ operator trên platform (1-1 với Operator). `balance BIGINT` CHECK >= 0, `rowVersion` cho optimistic lock. Credit chỉ qua TripSettlement settle từ PlatformWallet; debit qua ADJUSTMENT (admin manual cho late refund/correction). v2 sẽ thêm WITHDRAWAL DEBIT (bank transfer ra ngoài).
- **`OperatorWalletTransaction`** — Immutable ledger của OperatorWallet (giống pattern WalletTransaction passenger). Fields: `operatorId`, `type` CREDIT | DEBIT, `amount` positive, `balanceBefore`/`balanceAfter`, `referenceType` TRIP_SETTLEMENT | ADJUSTMENT, `referenceId`, `note`, `createdAt`. Atomic INSERT với UPDATE OperatorWallet.
- **`OperatorTripSettlement`** — Per-Trip per-Operator settlement marker. Fields: `operatorId`, `tripId`, UNIQUE `(operatorId, tripId)`. `netAmount BIGINT` (computed at settle), `tripTerminalAt`, `eligibleAt` (= tripTerminalAt + 7 days), `status` enum PENDING_HOLD | ELIGIBLE | SETTLED | CANCELLED, `settlementMethod` AUTO_WEEKLY | ADMIN_MANUAL nullable, `settledAt`, `settledByUserId` nullable (SYSTEM_ADMIN nếu manual), `walletTransactionId` FK nullable, `rowVersion` cho status transition lock. Hangfire daily eligibility flag + Monday weekly auto-settle. Admin manual `POST /v1/admin/trip-settlements/{id}/settle`.
- **`RefundFailureLog`** — Track refund retry khi event consume fail (vd Wallet credit fail). Hangfire retry job, max 5 lần, alert Admin sau khi exhausted. Xem section 6.2 compensation flow.
- **`OutboxEvent`**.

> **Bank withdrawal**: KHÔNG trong v1. Operator bank account fields (`bankAccountName/bankAccountNumber/bankName`) trên `Operator` entity vẫn nullable cho schema-readiness — v1 không enforce, không validate, không UI banner. v2 sẽ implement Bank Withdrawal flow.

#### Parcel Service

- **`Parcel`** — Hàng ký gửi. `parcelCode` UNIQUE. `senderUserId` NOT NULL; `recipientUserId` nullable; `dropoffStopId` nullable. Lưu estimated/actual cargo + size derived, canonical settlement fields, payment/deadline/check-in snapshots, `pendingActionResumeStatus`, delivery/transfer/return/review fields.
- **`ParcelRouteFare`** — Operator config `pricePerKgVnd` + `minimumPriceVnd` per route/derived size category và effective window.
- **`ParcelStats`** — Counter table per (operatorId, date) cho reporting.
- **`OutboxEvent`**.

#### Tracking Service

- **`GpsTrail`** — GPS history per trip: lat/lng, speed nullable, timestamp. Batch insert từ Redis buffer.
- **`OutboxEvent`**.

#### Notification Service

- **`Notification`** — In-app history per user. type enum (xem dưới), title, body, data JSONB, readAt.
- **`NotificationDelivery`** — Track FCM push attempt per notification: fcmToken, status SENT | FAILED | RETRYING, retryCount, lastError.
- **Không có `OutboxEvent`** — Notification chỉ consume RabbitMQ, không publish.

**`Notification.type` enum:** `BOOKING_CONFIRMED | BOOKING_CANCELLED | BOOKING_DISRUPTED | BOOKING_REFUNDED | PASSENGER_NO_SHOW | TRIP_BOARDING_REMINDER | TRIP_VEHICLE_APPROACHING | TRIP_ROUTE_CHANGED | TRIP_SCHEDULE_CHANGED | TRIP_CANCELLED | TRIP_DELAYED | TRIP_DISRUPTED | STOP_DISABLED | VEHICLE_SUBSTITUTED | VEHICLE_SWAPPED | PARCEL_LOADED | PARCEL_IN_TRANSIT | PARCEL_DELIVERED_PENDING_CONFIRM | PARCEL_REJECTED | PARCEL_RETURNED | WALLET_CREDITED | WALLET_DEBITED | INCIDENT_REPORTED | OFF_ROUTE_ALERT | TRIP_DELAYED_ALERT | CARGO_NEAR_FULL_ALERT | PARCEL_REVIEW_REQUESTED | PARCEL_REVIEW_APPROVED | PARCEL_FINAL_PAYMENT_REQUIRED | PARCEL_SETTLEMENT_RECOVERED | VOUCHER_CONSENT_REQUESTED | VOUCHER_CONSENT_ACCEPTED | VOUCHER_CONSENT_REJECTED | SUBSCRIPTION_LIMIT_EXCEEDED | SUBSCRIPTION_USAGE_WARNING | SUBSCRIPTION_TRIAL_EXPIRING | SUBSCRIPTION_EXPIRED | SUBSCRIPTION_APPROVED | SUBSCRIPTION_PAYMENT_PENDING_WARN | SUBSCRIPTION_PAYMENT_AUTO_REVERTED | INVOICE_ISSUED | DRIVER_SCHEDULE_EDITED | PAYOUT_PROCESSED | PAYOUT_FAILED | OPERATOR_APPROVED | OPERATOR_SUSPENDED | OPERATOR_REGISTRATION_SUBMITTED | TRIP_ASSIGNED | TRIP_ASSIGNMENT_REMOVED | OPERATOR_ANNOUNCEMENT | SHUTTLE_ASSIGNED | SHUTTLE_UNFULFILLED | SHUTTLE_WARNING | DRIVER_STOP_DEPARTED_WITH_PENDING`.

#### FCM Token Lifecycle (Identity Service)

- **`UserDevice`** — fcmToken per device, platform IOS | ANDROID | WEB, isActive, lastActiveAt. Multi-device per user (UNIQUE per `(userId, fcmToken)`).
- Endpoints (require User JWT):
  - `POST /v1/auth/device-token { fcmToken, platform }` — upsert, claim token từ user khác nếu duplicate.
  - `DELETE /v1/auth/device-token { fcmToken }` — set isActive=false (logout).
- Notification Service lấy token qua `GET /internal/v1/users/{userId}/device-tokens`.
- Stale cleanup: Hangfire weekly set isActive=false WHERE lastActiveAt < now - 90 days. FCM `UNREGISTERED`/`INVALID_ARGUMENT` → cleanup ngay.

#### RAG AI Service

- **`KnowledgeDocument`** — File upload metadata: fileUrl Firebase Storage, fileType, accessLevel PUBLIC | OPERATOR | ADMIN, status PENDING_REVIEW | APPROVED | REJECTED | ARCHIVED, audit fields.
- **`KnowledgeChunk`** — Đoạn text + `embedding vector(1536)` pgvector. Có ivfflat index cho similarity search.
- **`RagConversation`** — 1 session chat: userId, role (filter accessLevel).
- **`RagMessage`** — Mỗi turn USER | ASSISTANT trong conversation. Có `citedChunkIds` audit trail.
- **`OutboxEvent`** cho `DocumentApproved` event.
### Feature flagged cho v2
- **Bank Withdrawal cho Operator Wallet** — operator request rút balance từ `OperatorWallet` về tài khoản ngân hàng thật. v1: tiền chỉ luân chuyển trong ví nội bộ platform (xem 4.6). v2 components: (1) UI nhập + validate `Operator.bankAccountName/bankAccountNumber/bankName` (3 fields đã có schema, defer UI sang v2); (2) entity `OperatorWithdrawalRequest` mới với status PENDING|PROCESSING|COMPLETED|FAILED + admin manual approve/process flow; (3) atomic UPDATE OperatorWallet DEBIT + INSERT OperatorWalletTransaction `referenceType=WITHDRAWAL`; (4) banking API integration (VietQR / VNPay PayOuts / Casso) thay manual bank transfer. Error codes mới: `OPERATOR_BANK_ACCOUNT_MISSING`, `OPERATOR_WALLET_INSUFFICIENT_BALANCE_FOR_WITHDRAWAL`.
- **OPERATOR_STAFF counter seat disable** — extend "Manager Seat Disable" (Section 4.3, hiện chỉ OPERATOR_ADMIN có quyền) sang role OPERATOR_STAFF cho use case nhân viên quầy vé khóa ghế thủ công. v1 contingency item: **chỉ implement nếu các task v1 hoàn thiện kịp tiến độ**. Schema không đổi (TripSeat đã có UNAVAILABLE status); chỉ relax authorization ở endpoint `POST /v1/operator/trips/{tripId}/seats/{seatNumber}/disable` cho thêm role OPERATOR_STAFF. Audit log vẫn bắt buộc.
- VNPay Refund API — hoàn tiền trực tiếp về ngân hàng thay vì ví
- Zalo OA làm kênh thông báo bổ sung cho parcel delivery
- Prometheus + Grafana monitoring
- Segment pricing thực (pickup→dropoff) — cần TripSegmentFare matrix
- Partial refund cho seat type downgrade trong vehicle substitution
- Data normalization engine cho System Admin (flag tuyến trùng, stop thiếu coords)
- Auto-suggest stop thay thế bằng geo proximity (PostGIS) khi disable Stop
- Tracking domain tách riêng `wss://tracking.vietride.app` nếu cần scale độc lập
- Parcel-only flow cho guest user (không cần account)
- Shuttle fare configurable theo zone/khoảng cách (v1 shuttle miễn phí khi kèm booking)
- ~~Parcel giao tại Stop dọc tuyến~~ → **In-scope v1** — xem `Parcel.dropoffStopId` ở section 6.6
- **`RouteStop.isPublicVisible boolean`** — flag per RouteStop entry để hỗ trợ "operational stop" (xe ghé chỉ đổ xăng/nghỉ, không hiển thị cho passenger book). v1 workaround: không add RouteStop entry. v2 nếu cần granular control.
- Driver/Vehicle document management (GPLX, đăng kiểm, bảo hiểm với expiration tracking)
- Detailed driver shift management + payroll integration
- **E-invoice provider integration** (VNPT/Misa/Viettel) — xuất e-invoice chính thức theo NĐ 123/2020 cho subscription fee. v1 chỉ generate PDF.
- **E-invoice luồng Operator → Passenger** cho vé xe (operator tự xử lý ngoài hệ thống ở v1).
- **VAT calculation/breakdown** trong totalAmount (v1 operator tự cộng VAT vào giá vé, app không tính riêng).
- **Notification opt-out / NotificationPreference entity** (v1 chỉ transactional, không có promotional).
- **Auto-merge duplicate Station** dựa trên geo proximity + fuzzy name match (v1 System Admin merge thủ công).

### In-scope v1
- **Shuttle service** (xe trung chuyển) — chỉ tại Station có `supportsShuttle = true`. Xem 6.14.
- **Cancellation policy configurable per operator** — `Operator.cancellationPolicy` JSONB. Xem 6.2.
- **Schedule change 3 mức** (MINOR/MEDIUM/MAJOR với refund 0/50/100%). Xem 6.13.
- **Parcel deposit + reweigh + additional payment.** Xem 6.6.
- **SaaS subscription model** — không có commission per ticket. Xem 4.5.
- **Excel export báo cáo** cho Operator Web. Xem 4.5.
- **Future-dated pricing config** (`effectiveFrom`/`effectiveUntil`). Xem Conventions.
- **Subscription Invoice (VietRide → Operator) + PDF generation** — Invoice entity + PDF download. Xem 4.5e. KHÔNG integrate e-invoice provider thật v1.
- **PlatformWallet + Operator Wallet + 7-day hold + Monday auto-settle** — booking/parcel revenue vào `PlatformWallet` holding pool, hold 7 ngày sau Trip terminal (COMPLETED/DISRUPTED), Hangfire Monday weekly debit `PlatformWallet` + credit `OperatorWallet`; admin manual settle per-trip (`POST /v1/admin/trip-settlements/{id}/settle`) bất cứ lúc nào. Entities: `PlatformWallet`, `PlatformWalletTransaction`, `OperatorWallet`, `OperatorWalletTransaction`, `OperatorTripSettlement`. **KHÔNG có bank withdrawal trong v1** — defer sang v2. Xem 4.6.
- **Operator tự tạo Station** với autocomplete dedupe — System Admin chỉ data-quality cleanup. Xem 4.3 + 4.4 + 6.10.
- **Trip edit snapshot rule** — booking CONFIRMED giữ điều kiện cũ khi operator edit Trip. Xem 6.1.
- **Stop disable flow** với BookingPendingAction `STOP_DISABLED`. Xem 6.4.1.
- **System Admin bootstrap** qua Identity Service startup seeder. Xem 5.1.1.
- **Driver/Assistant/OperatorStaff first-login password** qua email link (`SET_INITIAL_PASSWORD` token TTL 48h). Xem 4.3 + 5.1.
- **Parcel delivery tại Stop dọc tuyến** — `Parcel.dropoffStopId` nullable. Sender chọn Stop trong RouteStop của trip; UNLOADED trigger check stop của parcel.
- **`RouteStop` là single source of truth cho pickup/dropoff control** — bỏ hoàn toàn `Trip.allowAlongRoutePickup`/`Dropoff` và `Route.defaultAllowAlongRoute*`. Operator kiểm soát qua việc thêm/bỏ RouteStop entries.
- **`RouteStop.allowPickup` + `RouteStop.allowDropoff`** — phân loại stop pickup-only / dropoff-only / both, phản ánh thực tế operator gom khách đầu tuyến và trả khách dần cuối tuyến. Snapshot vào TripStop.
