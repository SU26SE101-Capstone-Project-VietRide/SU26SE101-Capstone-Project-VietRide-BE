# Notification Service - Timeline Production

> **Quy tac cho AI**: Khi nhan task lien quan Notification Service, AI PHAI doc file nay truoc,
> xac dinh Phase hien tai (Phase chua `[x]` dau tien), chi lam dung scope Phase do,
> verify xong moi bao done. TUYET DOI khong tu chuyen sang Phase tiep theo.

## Tom Tat

Notification Service la NestJS worker cho in-app notification history, FCM push delivery,
va email delivery. Service nay chi consume RabbitMQ events, khong publish event va khong co
OutboxEvent table.

Production direction:

- KHONG co Outbox trong Notification Service.
- RabbitMQ la input boundary; BullMQ la internal retry queue cho FCM/email.
- Moi RabbitMQ consumer bat buoc co Redis idempotency.
- In-app history persist vao PostgreSQL schema `vietride_notification` bang Prisma client rieng.
- REST API verify Identity User Access Token bang RS256/JWKS, issuer `vietride-identity`,
  audience `vietride-api`.
- Push provider va email provider phai di qua abstraction; chi them dependency that khi USER approve.

Moi phase phai test doc lap bang unit/e2e theo huong production. Neu dependency that chua san sang,
giu provider interface va fake adapter trong test, nhung khong goi do la production integration verify.

## Phase Progress

- [x] Phase 1 - Production Foundation Va Prisma
- [ ] Phase 2 - Identity-backed REST Auth Va In-app API
- [ ] Phase 3 - Notification Write Core
- [ ] Phase 4 - Core Booking/Payment Consumers
- [ ] Phase 5 - Trip/Tracking Alert Consumers
- [ ] Phase 6 - Parcel/Subscription/Operator Consumers
- [ ] Phase 7 - Push Delivery Pipeline
- [ ] Phase 8 - Email Delivery Pipeline
- [ ] Phase 9 - Reliability, Retention, Observability
- [ ] Phase 10 - Hardening Va Final Acceptance

---

## Phase 1 - Production Foundation Va Prisma

**Thoi luong:** 1 ngay
**Muc tieu:** Notification co nen NestJS production-shaped, Prisma client rieng, Redis/RabbitMQ wiring,
va verify target day du.

### Scope

- Tao Notification timeline production file nay.
- Chuan hoa AppModule:
  - `NestCommonModule`.
  - `NestRedisModule.forRoot({ url: env.REDIS_URL })`.
  - `NestRabbitMqModule.forRoot({ url, exchange: vietride.events, exchangeType: topic })`.
  - `ApiResponseExceptionFilter`, `LoggingInterceptor`, `ApiResponseInterceptor`.
- Them config:
  - `apps/notification/src/config/env.schema.ts`.
  - `apps/notification/src/config/notification-config.module.ts`.
  - `ENV_TOKEN`.
- Them Prisma:
  - `apps/notification/prisma/schema.prisma`.
  - `apps/notification/src/prisma/notification-prisma.service.ts`.
  - `apps/notification/src/prisma/prisma.module.ts`.
  - generator output `../src/generated/notification-prisma-client`.
  - datasource schemas `["vietride_notification"]`.
  - model/enum gan `@@schema("vietride_notification")`.
- Them health/ready nen:
  - `/health` liveness.
  - `/ready` readiness co response co ban; dependency checks chi harden o Phase 10.
- Them Nx targets:
  - `notification:generate`.
  - `notification:test:e2e`.
- Them e2e nen cho health/ready.

### Output hoan thanh

- Notification build duoc voi Prisma client rieng.
- App boot duoc voi config env production-shaped.
- Chua implement REST business endpoint, RabbitMQ consumer, FCM, SendGrid.

### Verify

```bash
npx nx run notification:lint
npx nx run notification:test
npx nx run notification:test:e2e
npx nx run notification:build
```

---

## Phase 2 - Identity-backed REST Auth Va In-app API

**Thoi luong:** 1-2 ngay
**Muc tieu:** FE co the doc notification history va mark read bang Identity access token that.

### Scope

- Implement Identity JWT verifier bang `jose`:
  - issuer `vietride-identity`.
  - audience `vietride-api`.
  - JWKS tu `JWT_PUBLIC_KEY_URL`.
  - `USER_JWT_PUBLIC_KEY` chi la RSA public key override cho local/test.
- Protected REST:
  - `GET /api/v1/notifications?unreadOnly&page&pageSize&sortBy&sortDir`.
  - `POST /api/v1/notifications/:notificationId/read`.
- Owner check:
  - user chi doc va mark read notification cua minh.
- QueryOptions:
  - `pageSize` max 100.
  - sort whitelist.
- E2E:
  - missing/invalid auth -> 401 envelope.
  - validation query sai -> 400 envelope.
  - owner happy path -> 200/204 dung contract.
- Script verify:
  - `scripts/test-notification-phase2.js`.

### Verify

```bash
npx nx run notification:lint
npx nx run notification:test
npx nx run notification:test:e2e
npx nx run notification:build
```

---

## Phase 3 - Notification Write Core

**Thoi luong:** 1 ngay
**Muc tieu:** Co core service/repository tao in-app notification an toan cho cac consumer va API.

### Scope

- Them `notifications` module:
  - controller/service/repository/dto.
  - mapper chuan hoa `Notification.type`, title, body, data JSON.
- Repository chi dung `NotificationPrismaService`.
- Service chi goi repository.
- Helper:
  - create notification.
  - list notification.
  - mark read idempotent.
  - optional unread count helper neu API can.
- Unit tests:
  - create maps data dung.
  - mark read cua owner thanh cong.
  - mark read cua user khac -> 404/403 theo contract phase.

### Verify

```bash
npx nx run notification:lint
npx nx run notification:test
npx nx run notification:test:e2e
npx nx run notification:build
```

---

## Phase 4 - Core Booking/Payment Consumers

**Thoi luong:** 1-2 ngay
**Muc tieu:** Notification consume nhom event cot loi cua booking va wallet/payment.

### Scope

- Consume:
  - `booking.booking.confirmed`.
  - `booking.booking.cancelled`.
  - `booking.booking.refunded`.
  - `payment.wallet.credited`.
  - `payment.wallet.debited`.
- Redis idempotency:
  - TTL 24h.
  - key theo routing key + messageId/correlationId.
- Validate payload bang Zod.
- Malformed payload bi drop co log, khong requeue loop.
- Persist in-app notification bang core service.
- Chua bat buoc push FCM that; enqueue push se vao Phase 7.

### Verify

```bash
npx nx run notification:lint
npx nx run notification:test
npx nx run notification:test:e2e
npx nx run notification:build
```

---

## Phase 5 - Trip/Tracking Alert Consumers

**Thoi luong:** 2 ngay
**Muc tieu:** Notification nhan cac alert tu Trip va Tracking.

### Scope

- Consume:
  - `trip.trip.boarding_started`.
  - `trip.trip.route_changed`.
  - `trip.trip.schedule_changed`.
  - `trip.trip.cancelled`.
  - `trip.trip.delayed`.
  - `trip.incident.reported`.
  - `tracking.gps.off_route`.
  - `tracking.gps.approaching_stop`.
- Map notification types:
  - `TRIP_BOARDING_REMINDER`.
  - `TRIP_ROUTE_CHANGED`.
  - `TRIP_SCHEDULE_CHANGED`.
  - `TRIP_CANCELLED`.
  - `TRIP_DELAYED`.
  - `INCIDENT_REPORTED`.
  - `OFF_ROUTE_ALERT`.
  - `TRIP_VEHICLE_APPROACHING`.
- E2E/unit:
  - approaching wave 1/2 title/body dung.
  - delayed/off-route dedupe theo message id.

### Verify

```bash
npx nx run notification:lint
npx nx run notification:test
npx nx run notification:test:e2e
npx nx run notification:build
```

---

## Phase 6 - Parcel/Subscription/Operator Consumers

**Thoi luong:** 2 ngay
**Muc tieu:** Hoan thien nhom event con lai cho parcel, subscription, payout/operator alert.

### Scope

- Consume parcel lifecycle:
  - created/loaded/unloaded/delivered_pending_confirm/delivery_confirmed/delivery_rejected.
  - cancelled/rejected/returned/auto_rejected/review_requested/transfer_initiated.
- Consume subscription/payment operator events:
  - subscription limit/trial/expired/approved.
  - invoice issued.
  - trip settlement completed.
  - payout processed/failed.
- Persist in-app notification theo recipient trong payload.
- Neu payload chi co `operatorId`, dung provider interface de resolve recipients o phase sau neu endpoint chua san sang.

### Verify

```bash
npx nx run notification:lint
npx nx run notification:test
npx nx run notification:test:e2e
npx nx run notification:build
```

---

## Phase 7 - Push Delivery Pipeline

**Thoi luong:** 2-3 ngay
**Muc tieu:** NotificationDelivery audit va FCM push retry qua BullMQ.

### Scope

- BullMQ queue `notification:fcm-push`.
- Device-token provider:
  - production: `GET /internal/v1/users/{userId}/device-tokens`.
  - internal auth theo convention HS256 `X-Internal-Auth`.
- Tao `NotificationDelivery` cho tung token snapshot.
- FCM provider abstraction:
  - interface trong app.
  - fake/no-op provider cho test.
  - provider that chi khi USER approve `firebase-admin`.
- Retry/backoff:
  - 5s -> 30s -> 5m -> exhausted/DLQ behavior.
- Invalid token:
  - blacklist Redis `notification:fcm_token_blacklist:{token}` TTL 1 ngay.

### Verify

```bash
npx nx run notification:lint
npx nx run notification:test
npx nx run notification:test:e2e
npx nx run notification:build
```

---

## Phase 8 - Email Delivery Pipeline

**Thoi luong:** 2 ngay
**Muc tieu:** Email delivery qua BullMQ voi provider abstraction va retry.

### Scope

- BullMQ queue `notification:email-send`.
- Email provider abstraction:
  - fake/no-op provider cho test.
  - SendGrid provider that chi khi USER approve `@sendgrid/mail`.
- Template categories:
  - AUTH_OTP.
  - SET_INITIAL_PASSWORD.
  - parcel delivery link.
  - operator/subscription/invoice notices.
- Khong log OTP/token/link day du.
- Retry/backoff tuong tu push.

### Verify

```bash
npx nx run notification:lint
npx nx run notification:test
npx nx run notification:test:e2e
npx nx run notification:build
```

---

## Phase 9 - Reliability, Retention, Observability

**Thoi luong:** 1-2 ngay
**Muc tieu:** Hardening van hanh truoc final acceptance.

### Scope

- Retention job:
  - xoa notifications cu hon 90 ngay, delivery cascade.
  - env configurable.
- Readiness dependency checks:
  - Prisma.
  - Redis.
  - RabbitMQ.
- Pino structured logs cho business layer.
- Sentry-safe error logging:
  - khong log token, OTP, deliveryToken, email body nhay cam.
- Review retry constants, Redis TTL constants, queue names.

### Verify

```bash
npx nx run notification:lint
npx nx run notification:test
npx nx run notification:test:e2e
npx nx run notification:build
```

---

## Phase 10 - Hardening Va Final Acceptance

**Thoi luong:** 1 ngay
**Muc tieu:** Full Notification Service production-ready va verify end-to-end voi dependency that khi co config.

### Scope

- Cleanup skeleton neu can:
  - `/api` hello endpoint khong anh huong route production.
- Full env review:
  - `JWT_PUBLIC_KEY_URL`.
  - `USER_JWT_PUBLIC_KEY` chi local/test.
  - `DATABASE_URL`.
  - `REDIS_URL`.
  - `RABBITMQ_URL`.
  - FCM/SendGrid env neu da approve provider that.
- Production integration verify:
  - Postgres.
  - Redis.
  - RabbitMQ.
  - Identity internal device-token endpoint.
  - FCM/SendGrid config neu enabled.
- Script verify for user-facing API/delivery behavior.
- Full repo TS verification.

### Verify

```bash
npx nx run notification:lint
npx nx run notification:test
npx nx run notification:test:e2e
npx nx run notification:build
npm run lint:ts
npm run test:ts
npm run build:ts
```

---

## Public Interfaces / Events Can Hoan Thanh

- REST:
  - `GET /api/v1/notifications`
  - `POST /api/v1/notifications/:notificationId/read`
- RabbitMQ:
  - Exchange `vietride.events`
  - Notification consumes only; no outbox.
- BullMQ:
  - `notification:fcm-push`
  - `notification:email-send`
- Redis:
  - `notification:fcm_token_blacklist:{token}`
  - idempotency keys for RabbitMQ consumers.
- Internal HTTP:
  - `GET /internal/v1/users/{userId}/device-tokens`

## Assumptions

- Identity Service la source of truth cho User Access Token.
- Identity internal device-token endpoint da ton tai theo BSOT registry, nhung production verify chi pass khi endpoint chay that.
- Notification Service khong publish event.
- Khong them `firebase-admin` hoac `@sendgrid/mail` neu USER chua approve.
- Khong xoa/rename/move file neu khong co lenh ro rang tu USER.
