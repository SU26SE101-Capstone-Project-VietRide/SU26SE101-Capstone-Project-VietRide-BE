# Sprint 2 — Demo script (Day 10)

> Demo flow per BE_TIMELINE_VU.md Day 10:
> **passenger register/login → admin approves operator → operator logs in & creates station/route/vehicle.**
> Every step below was executed live against the Docker stack via the Gateway (`:3000`) on 2026-06-10
> during `/audit-day 10`. Status codes + observed side-effects are real.
>
> ⚠️ **Known limitation:** the final leg (operator creates **route / vehicle**) cannot be demoed yet —
> Route & Vehicle belong to Days 8–9, which are NOT implemented. Operator can create **stations** (Day 7).
> See "Blocked leg" at the bottom.

## 0. Prerequisites — bring up the stack

```bash
# From repo root. Postgres/RabbitMQ/Redis + the .NET/NestJS app containers.
docker compose --env-file .env -f infra/docker/docker-compose.yml --profile app up -d --build
docker ps --format "table {{.Names}}\t{{.Status}}"     # all healthy
```

Health matrix (all 200):

```bash
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:3000/health            # gateway
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5001/health            # identity
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:3000/v1/identity/health # via gateway
```

> Windows note: these `curl` examples use bash syntax. In PowerShell use `curl.exe` (not the
> `Invoke-WebRequest` alias) and `'{...}'` single-quoted JSON bodies.

---

## 1. Passenger register → verify → login (via Gateway)

```bash
# 1a. Register (public). → 201, status PENDING_EMAIL_VERIFICATION
curl -s -X POST http://localhost:3000/v1/auth/register -H "content-type:application/json" \
  -d '{"email":"passenger1@example.com","phone":"0901234567","password":"Passw0rd!23","displayName":"Passenger One"}'

# 1b. OTP is logged/stored (dev uses a logging email service). Read it from the DB:
docker exec vietride_postgres psql -U vietride -d vietride_identity -t -A -c \
  "SELECT t.code FROM vietride_identity.email_verification_tokens t \
   JOIN vietride_identity.users u ON u.id=t.user_id \
   WHERE u.email='passenger1@example.com' AND t.purpose='REGISTRATION' \
   ORDER BY t.created_at DESC LIMIT 1;"

# 1c. Verify email — purpose is REQUIRED. → 200, status ACTIVE
curl -s -X POST http://localhost:3000/v1/auth/verify-email -H "content-type:application/json" \
  -d '{"email":"passenger1@example.com","code":"<OTP>","purpose":"REGISTRATION"}'

# 1d. Login → 200 with accessToken (RS256 user token)
curl -s -X POST http://localhost:3000/v1/auth/login -H "content-type:application/json" \
  -d '{"email":"passenger1@example.com","password":"Passw0rd!23"}'
```

**Side-effect to show:** register emits an `identity.user.created` Outbox event in the SAME transaction,
which the OutboxBackgroundService publishes to RabbitMQ exchange `vietride.events`:

```bash
docker exec vietride_postgres psql -U vietride -d vietride_identity -t -c \
  "SELECT event_type, status, published_at IS NOT NULL FROM vietride_identity.outbox_events \
   WHERE payload::jsonb->>'email'='passenger1@example.com';"
# → identity.user.created | PUBLISHED | t
```

Payload: `{ "userId", "role":"PASSENGER", "email", "createdAt" }` (BSOT §7.3).

### Passenger profile + booking-history stubs (Day 10 / SCV-76)

```bash
TOKEN=<accessToken from 1d>
curl -s http://localhost:3000/v1/passenger/me       -H "Authorization: Bearer $TOKEN"   # 200, GetMeResponseDto
curl -s http://localhost:3000/v1/passenger/bookings -H "Authorization: Bearer $TOKEN"   # 200, {items:[],page:1,pageSize:20,total:0}
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:3000/v1/passenger/me          # 401 (no token)
```

---

## 2. Admin approves an operator (via Gateway) — the Day-10 headline DoD

```bash
# 2a. Admin login (bootstrap SYSTEM_ADMIN from .env). → 200 + accessToken
curl -s -X POST http://localhost:3000/v1/auth/login -H "content-type:application/json" \
  -d '{"email":"admin@vietride.app","password":"ChangeMeOnFirstLogin!"}'
ATK=<admin accessToken>

# 2b. Operator self-registers (public). VN phone format required (0xxxxxxxxx / +84xxxxxxxxx). → 201 + operatorId
curl -s -X POST http://localhost:3000/v1/operators/register -H "content-type:application/json" \
  -d '{"name":"Demo Operator","contactEmail":"ops@example.com","contactPhone":"0981112233",
       "businessRegistrationNumber":"BRN-DEMO-001","taxCode":"TAX-DEMO-001",
       "addressStreet":"1 Le Loi","addressWard":"Ben Nghe","addressDistrict":"D1","addressProvince":"HCMC",
       "representativeName":"Rep","representativePhone":"0881112233","password":"Passw0rd!23"}'
OPID=<operatorId>

# 2c. Admin approves. → 200, registrationStatus APPROVED
curl -s -X POST http://localhost:3000/v1/admin/operators/$OPID/approve \
  -H "Authorization: Bearer $ATK" -H "content-type:application/json" -d '{}'
```

**Side-effect to show (the DoD):** approval emits `identity.operator.approved` in the same transaction,
published to RabbitMQ with payload `{ "operatorId", "approvedAt" }`. To watch it land, bind a temp
queue to the exchange BEFORE approving and drain it after:

```bash
# Bind a capture queue (do this before 2c)
curl -s -u vietride:vietride_dev -H "content-type:application/json" \
  -X PUT  http://localhost:15672/api/queues/%2F/demo.capture -d '{"durable":false}'
curl -s -u vietride:vietride_dev -H "content-type:application/json" \
  -X POST http://localhost:15672/api/bindings/%2F/e/vietride.events/q/demo.capture -d '{"routing_key":"identity.#"}'

# After 2c, read messages:
curl -s -u vietride:vietride_dev -H "content-type:application/json" \
  -X POST http://localhost:15672/api/queues/%2F/demo.capture/get \
  -d '{"count":10,"ackmode":"ack_requeue_false","encoding":"auto"}'
# → routing_key identity.operator.approved | {"approvedAt":"…","operatorId":"…"}
```

> Suspend (`POST /v1/admin/operators/{id}/suspend`) emits `identity.operator.suspended {operatorId,suspendedAt}` the same way.

**At-least-once / restart resilience (Day-10 "Review" item):** if RabbitMQ is unreachable when a row is
drained, the row stays `FAILED` with an incremented `retry_count` (bounded by `MaxRetryCount`, default 10 —
never republished past the cap) and is re-published on the next poll tick once the broker is back. Observed
live during the audit.

---

## 3. Operator logs in & creates station / route / vehicle

```bash
# 3a. The OPERATOR_ADMIN user created in 2b must set its initial password (link/OTP emailed),
#     then login → operator accessToken.
# 3b. Create a station (Day 7 — IMPLEMENTED):
curl -s -X POST http://localhost:3000/v1/operator/stations \
  -H "Authorization: Bearer <operatorToken>" -H "content-type:application/json" -d '{ … station … }'
```

### ⛔ Blocked leg — route & vehicle
`POST /v1/routes` and `POST /v1/vehicles` are **not implemented** (Days 8–9 not delivered; `apps/trip`
Domain currently has only Station / OperatorStation / Stop). The "operator creates **route/vehicle**"
portion of the Sprint-2 demo is therefore **deferred** until Days 8–9 land. Stations are demoable today.

---

## Idempotency note (Day-10 placeholder)
`IdempotencyMiddleware` (Shared.Web) is shipped as a placeholder and is **NOT wired** to any endpoint
this sprint (Booking/Payment/Parcel don't exist yet). Its behaviour (same key+body → cached; same
key+different body → `422 IDEMPOTENCY_KEY_MISMATCH`) is covered by unit tests; it activates once the
Sprint-3 mutation endpoints exist.

## Cross-service consumption
`identity.user.created` / `identity.operator.approved` / `identity.operator.suspended` are published to
the `vietride.events` topic exchange and consumed by the **notification** worker (NestJS) — see
`apps/notification` `IdentityEventsConsumer` (Day-10 gap-fix). Add further consumers (Payment wallet init,
Trip/Booking on suspend) in their respective services.
