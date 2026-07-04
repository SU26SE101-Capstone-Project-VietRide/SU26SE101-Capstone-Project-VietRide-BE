# BE .NET Timeline — Trần Hoàng Vũ

> Daily timeline for backend .NET dev (Vũ) from Day 1 (2026-05-25) through end of Sprint 6 (~2026-07-31). For agent coding manager + review/test task generation.
>
> **Source of truth**: `BACKEND_SOURCE_OF_TRUTH.md`, `SU26SE101_VIETRIDE_technical_context_v7.md`, Jira project SCV. Business rules NOT repeated here — refer to source.
>
> **Conventions**:
> - 1 day = 1 working day (Mon–Fri). Weekends off. 50 total work days.
> - Each day cites Jira key when applicable.
> - "DoD" = Definition of Done = binary criteria to mark day complete.
> - "Review" = what the review/test agent should verify.
> - "Blocker" = upstream dep that must land first.
>
> **Owner scope (Vũ)**: 5 .NET services (Identity & User, Trip-Route-Vehicle, Booking, Payment & Wallet, Parcel) + **NestJS API Gateway** (routing, User JWT validation, Internal JWT minting) + .NET side of Internal JWT validation. Code lives in single monorepo with `docs/` folder for shared documentation.
>
> **Note**: Sprint 1 carryover doc tasks ([SCV-1](https://hoangvutran088.atlassian.net/browse/SCV-1) ERD, [SCV-2](https://hoangvutran088.atlassian.net/browse/SCV-2) Context Diagram) are documentation work tracked separately from this code timeline — they're not blocking BE dev daily flow.

---

## Sprint 2 — Foundation (Day 1–10)

**Sprint goal**: Identity + Operator + Trip foundation + Outbox baseline ready, so Sprint 3 booking/payment can start.

### Day 1 — Mon 2026-05-25 — Monorepo + codebase scaffold ([SCV-8](https://hoangvutran088.atlassian.net/browse/SCV-8), [SCV-9](https://hoangvutran088.atlassian.net/browse/SCV-9))
- Initialize `vietride-backend` Nx monorepo on GitHub (single repo for .NET services + NestJS apps + shared libs + docs). See [docs/adr/0001-monorepo-layout.md](docs/adr/0001-monorepo-layout.md).
- Monorepo layout (per ADR 0001): `apps/{identity,trip,booking,payment,parcel,gateway,tracking,notification,rag}/`, `libs/dotnet/`, `libs/shared/`, `docs/`, `infra/` (docker-compose + nginx + pgbouncer + postgres + rabbitmq config — observability stack deferred to v2 per BACKEND_SOURCE_OF_TRUTH §9.13), `db-schema/`, `tests/`
- One `.sln` per .NET service at `apps/<svc>/VietRide.<Svc>.sln` (4 layer: Api/Application/Domain/Infrastructure + 2 test projects). Shared .NET libs grouped under `libs/dotnet/VietRide.Libs.sln`
- `nx generate @nx/nest:app` for `gateway` (+ tracking/notification/rag scaffolds) with TypeScript strict mode
- `infra/docker/docker-compose.yml` with Postgres 16 (single cluster, separate DB per service), PgBouncer, Redis 7, RabbitMQ 3.13-management
- Root `README.md` with monorepo run instructions
- **DoD**: `docker compose -f infra/docker/docker-compose.yml up` runs all 4 infra; both `dotnet build` per service .sln and `npx nx run-many -t build` succeed on fresh clone
- **Review**: Tuyên + 2 FE devs clone repo, run `docker compose up`, no error; folder layout matches `docs/` convention in source-of-truth (no `Docs/` uppercase folder)
- **Blocker**: none

### Day 2 — Tue 2026-05-26 — Sprint 2 kickoff: .NET skeleton + NestJS Gateway routing ([SCV-69](https://hoangvutran088.atlassian.net/browse/SCV-69), [SCV-70](https://hoangvutran088.atlassian.net/browse/SCV-70))
- Per .NET service: `Program.cs` ASP.NET 8 minimal API, `appsettings.json`, EF Core DbContext stub, Dockerfile (multi-stage SDK 8.0-alpine → aspnet 8.0-alpine)
- Add `Serilog` + structured logging + correlationId middleware in `libs/dotnet/VietRide.Shared.Web` and `nestjs-pino` (Gateway)
- Add Internal JWT HS256 validation handler in `libs/dotnet/VietRide.Shared.Web/Authentication/` (`InternalJwtAuthenticationHandler.cs`)
- Gateway: route table config-driven (`apps/gateway/src/config/routes.ts` maps `/v1/*` paths → downstream service URL); User JWT (RS256) validation via JWKS from Identity; Internal JWT minting (HS256 TTL 120s) before proxying
- Gateway: rate limit per IP (`@nestjs/throttler` 120req/60s default per BACKEND_SOURCE_OF_TRUTH §11.3, env-overridable via `RATE_LIMIT_DEFAULT_PER_MIN`) + health passthrough exemption (`/v1/<svc>/health` rewriteTo `/health`)
- Define `INTERNAL_JWT_SECRET` + `JWT_PUBLIC_KEY_URL` in `.env.example` (Vũ owns both sides of contract)
- Add EF Core migration tooling per service: `IDesignTimeDbContextFactory<TDbContext>` under `apps/<svc>/src/VietRide.<Svc>.Infrastructure/Design/` (so `dotnet ef migrations add` works without booting the full host)
- **DoD**: 5 .NET services boot independently on ports 5001-5005; Gateway on 3000 proxies to each; `/health` returns 200; Internal JWT roundtrip works (Gateway mints → .NET validates)
- **Review**: hit Gateway `/v1/identity/health` from outside docker network → reaches Identity Service; tamper Internal JWT → .NET returns 401
- **Blocker**: none (Vũ owns both Gateway + .NET sides)

### Day 3 — Wed 2026-05-27 — Identity Service: User + Auth foundation ([SCV-65](https://hoangvutran088.atlassian.net/browse/SCV-65))
- **Pre-reqs — architecture baseline (DO FIRST; blocks every CQRS/auth task below):**
  - Add to `Directory.Packages.props` as `<PackageVersion>` (CPM; csproj refs stay version-less): `MediatR` **11.x**, `FluentValidation` + `FluentValidation.DependencyInjectionExtensions` **11.x**, `BCrypt.Net-Next` latest. (CPM/banned-dep hooks will reject `Version=` on the ref and MediatR v12+.)
  - Create MediatR pipeline behaviors (`ValidationBehavior`, `LoggingBehavior`, `TransactionBehavior`) in `libs/dotnet/VietRide.Shared.Application/Behaviors/` (not present yet) and register MediatR + behaviors in the Identity Application/Api DI.
  - Add `NetArchTest.Rules` to `Directory.Packages.props` + a dependency-direction test in `VietRide.Identity.UnitTests` (Domain→nothing, Application→Domain, Infrastructure→Domain+Application, Api→Application+Infrastructure). This makes the "CI-enforced layering" claimed in BSOT/agents real before feature code lands.
- EF migration: User, OAuthIdentity, RefreshToken, EmailVerificationToken, UserDevice tables
- Password hashing (BCrypt.Net-Next cost 12 per technical_context_v7 §security + BSOT §2.1) + RS256 JWT signing + JWKS endpoint
- Endpoints: `POST /auth/register` (passenger email/password), `POST /auth/login`, `POST /auth/refresh`, `POST /auth/logout`
- Email OTP send + verify flow (provider abstraction, SendGrid stub)
- **DoD**: passenger can register → receive OTP → verify → login → get access+refresh token; JWKS endpoint serves public key
- **Review**: end-to-end via curl; check user status transitions PENDING_VERIFICATION → ACTIVE
- **Blocker**: none

### Day 4 — Thu 2026-05-28 — Identity Service: Google OAuth + Complete Phone + Admin bootstrap
- `POST /auth/google` flow with Google OAuth token verification
- "Complete phone" Gateway enforcement: protected routes return 403 if `Phone IS NULL` and role=PASSENGER
- `POST /v1/users/me/complete-profile` endpoint (no OTP (D1))
- Admin bootstrap startup seeder: seeded SYSTEM_ADMIN from `SYSTEM_ADMIN_BOOTSTRAP_*` env (idempotent)
- `POST /admin/users` (System Admin creates other admin)
- **DoD**: Google OAuth user without phone gets 403 on protected route; bootstrap admin exists after first Identity service startup/seeder run
- **Review**: Postman collection covering all 3 auth paths (email/Google/admin-created)

### Day 5 — Fri 2026-05-29 — Identity Service: Staff initial password + FCM tokens
- `SET_INITIAL_PASSWORD` token TTL 48h via `EmailVerificationToken.purpose`
- `POST /auth/set-initial-password` (consumes token)
- Endpoint to resend initial-password link
- `UserDevice` CRUD: `POST /devices` register FCM, `PATCH /devices/{id}` update, `DELETE /devices/{id}` on logout
- Multi-device support: claim transfer if duplicate token belongs to another user (idempotent)
- Internal endpoint `GET /internal/users/{id}/devices/active` for Notification Service
- **DoD**: Driver/Assistant/OperatorStaff can be created (no password) → email link → set password → login
- **Review**: token TTL test (use expired token returns 410); FCM token duplicate-claim test

### Day 6 — Mon 2026-06-01 — Operator Service (within Identity DB per v7) ([SCV-72](https://hoangvutran088.atlassian.net/browse/SCV-72))
- EF migration: Operator entity + JSONB policies (cancellationPolicy, parcelNoShowPolicy, luggagePolicy)
- `POST /operators/register` self-register (creates Operator PENDING + OperatorAdmin user PENDING_EMAIL_VERIFICATION)
- `POST /admin/operators` (System Admin creates operator manually)
- `PATCH /admin/operators/{id}/approve` + `/reject` + `/suspend` endpoints
- Auto-assign Starter Trial subscription on approve (placeholder until Sprint 5 — write the OperatorSubscription row with PLAN='STARTER_TRIAL')
- Operator profile read/update endpoints (OPERATOR_ADMIN only)
- **DoD**: operator self-register → admin approve → operator can login; policy JSONB schema validated
- **Review**: round-trip test; verify only OPERATOR_ADMIN can modify policies, OPERATOR_STAFF gets 403

### Day 7 — Tue 2026-06-02 — Trip-Route-Vehicle: Station + OperatorStation + Stop ([SCV-74](https://hoangvutran088.atlassian.net/browse/SCV-74))
- EF migration: Station, OperatorStation, Stop tables
- `GET /stations/search?q=` autocomplete (canonical Station + dedupe by name+coord)
- `POST /stations` (operator creates new Station if no match — no admin gatekeep per v7 F034)
- `POST /operators/{id}/stations` link existing Station to operator
- Stop CRUD with Google Places integration stub
- **DoD**: operator can search → link existing OR create new Station; Stop created with lat/lng/name
- **Review**: autocomplete dedupe test (search "Mien Tay" returns existing canonical Station); Stop coords validated lat ∈ [-90,90]

### Day 8 — Wed 2026-06-03 — Trip-Route-Vehicle: Route + RouteStop + AlternativeRoute
- EF migration: Route, RouteStop, AlternativeRoute, RouteStopFareTemplate tables
- Route CRUD with `returnRouteId` self-reference
- RouteStop add/remove with `orderIndex`, `allowPickup`, `allowDropoff` flags
- `RouteStopFareTemplate` with `effectiveFrom`/`effectiveUntil` (future-dated pricing)
- AlternativeRoute CRUD (max 2 per main route, validated at API)
- **DoD**: operator can create route with main stops + future-dated fare; flags enforced (no pickup at terminal-only stop)
- **Review**: validation test — adding stop with order index conflict returns 422

### Day 9 — Thu 2026-06-04 — Trip-Route-Vehicle: VehicleType + Vehicle + DriverSchedule skeleton
- EF migration: VehicleType (system-defined seed: 3 types — Limousine 9-seat, Bus 16-seat, Sleeper Bus 40-seat), Vehicle, DriverSchedule tables
- Seed migration for VehicleType with seatLayoutJson templates
- Vehicle CRUD (operator-scoped; validates seatLayout matches totalSeats)
- DriverSchedule create endpoint (operator assigns driver/assistant/vehicle/route by dayOfWeek + departureTime) — no Hangfire trip generation yet, just persist row
- **DoD**: operator can create vehicle with valid seat layout; DriverSchedule row stored with conflict check (one driver one slot)
- **Review**: seatLayout JSON schema validation; conflict test (same driver, same dayOfWeek, same time → 409)

### Day 10 — Fri 2026-06-05 — Outbox + Idempotency baseline + Sprint 2 demo prep ([SCV-78](https://hoangvutran088.atlassian.net/browse/SCV-78))
- `OutboxEvent` table per critical service (Identity, Trip, Booking, Payment, Parcel)
- Outbox poller worker (5s tick) publishing to RabbitMQ exchange `vietride.events`
- `Idempotency-Key` middleware: Redis SETNX with 24h TTL on POST/PATCH endpoints in Booking/Payment/Parcel (placeholder, applies once endpoints exist)
- Wire Outbox into Identity (`user.created`, `operator.approved`, `staff.password_set`)
- Passenger profile + booking history endpoint stub ([SCV-76](https://hoangvutran088.atlassian.net/browse/SCV-76)) — `GET /passenger/me`, `GET /passenger/bookings` (returns empty list, real data Sprint 3)
- **DoD**: outbox emits event when admin approves operator; Tuyên (NestJS) can consume; Sprint 2 demo script ready
- **Review**: kill Outbox publisher mid-tx, verify event eventually published after restart; idempotency-key duplicate returns same response
- **Sprint 2 demo**: passenger register/login → admin approves operator → operator logs in & creates station/route/vehicle

---

## Sprint 3 — Booking + Payment Core (Day 11–20)

**Sprint goal**: Passenger can search trip → book seat → pay → cancel. End of sprint demo: full booking flow on Passenger App with VNPay.

### Day 11 — Mon 2026-06-08 — Trip Search API ([SCV-80](https://hoangvutran088.atlassian.net/browse/SCV-80))
- `GET /trips/search?origin=&destination=&date=` with filter by operator/time/price
- Joins: Trip → Route → RouteStop → Stop → Station, with available seat count
- Trip auto-generation Hangfire job (14 days ahead) — runs once on DriverSchedule activation + nightly Sunday 23:00
- `GET /trips/{id}` detail with stops, seat layout, fare breakdown
- **DoD**: passenger search Saigon→Can Tho 2026-06-15 returns active trips with seat counts; Hangfire generates trips on schedule activate
- **Review**: trip generation idempotent test (re-run same day = no dup); search with no result returns empty 200 not 404

### Day 12 — Tue 2026-06-09 — Booking Service: Seat lock + Booking entity ([SCV-82](https://hoangvutran088.atlassian.net/browse/SCV-82))
- EF migration: Booking, Passenger (sub-entity), BookingPendingAction, TripSeat tables
- Redis Lua script: atomic seat-lock per `(tripId, seatNumber)` with 10-min TTL
- `POST /bookings` accept up to 5 seats, lock all-or-nothing
- TripSeat status machine: AVAILABLE → HELD → BOOKED → (BOARDED/NO_SHOW)
- BookingCode QR generator (plain string `VR-yyyyMMdd-XXXXXXXX`, no JSON/token encoded)
- **DoD**: 2 concurrent bookings on same seat → only one wins; HELD seat auto-releases after 10 min if no payment
- **Review**: stress test 50 concurrent booking attempts on 1 seat; verify only 1 succeeds; verify lock release on timeout

### Day 13 — Wed 2026-06-10 — Booking Service: Pickup/Dropoff + Round-trip ([SCV-84](https://hoangvutran088.atlassian.net/browse/SCV-84))
- Booking pickup/dropoff selection from RouteStop (validates allowPickup/allowDropoff + orderIndex)
- `PATCH /bookings/{id}/pickup-dropoff` edit before T-2h cutoff (pickup downgrade refunds delta to wallet)
- Round-trip booking: `POST /bookings/round-trip` creates 2 Booking rows with shared `bookingGroupId`; atomic seat lock both directions
- Booking snapshot fields (fare frozen at confirmation: baseFare, pickupStopFare, voucherDiscount)
- **DoD**: passenger books round-trip → both legs locked; edit pickup before cutoff works; after cutoff returns 422
- **Review**: edit cutoff exactly at T-2h boundary; round-trip cancel one leg doesn't cancel the other

### Day 14 — Thu 2026-06-11 — Voucher checkout ([SCV-86](https://hoangvutran088.atlassian.net/browse/SCV-86))
- EF migration: Voucher, VoucherUsage, OperatorVoucherConsent tables
- `POST /admin/vouchers` (System Admin creates voucher with fundingType VIETRIDE_FUNDED or OPERATOR_FUNDED)
- Voucher validation at booking checkout: scope (route/operator), usage limit, min order, consent status (ACCEPTED for OPERATOR_FUNDED)
- Operator consent endpoints: `GET /operator/voucher-consents`, `PATCH /operator/voucher-consents/{id}` (accept/reject)
- Round-trip uses 2 VoucherUsage records
- **DoD**: voucher applied at checkout reduces total; revoke voucher consent after booking confirmed doesn't reverse the discount
- **Review**: voucher usage limit boundary; OPERATOR_FUNDED without consent rejected at checkout

### Day 15 — Fri 2026-06-12 — Payment & Wallet: Wallet + VNPay top-up ([SCV-88](https://hoangvutran088.atlassian.net/browse/SCV-88))
- EF migration: Wallet, WalletTransaction, TopUpRequest, PlatformWallet, PlatformWalletTransaction tables
- Wallet auto-create on first user activation
- `POST /payments/topup` Wallet via VNPay (creates TopUpRequest PENDING, returns VNPay redirect URL with HMAC signature)
- VNPay IPN webhook handler: HMAC verify → idempotent SETNX → mark TopUpRequest SUCCEEDED → credit Wallet → emit `topup.succeeded`
- VNPay Return URL handler: status query only (NOT business source of truth)
- TopUp timeout 15 min auto-fail via Hangfire
- **DoD**: passenger tops up 100k via VNPay sandbox → Wallet balance += 100k → WalletTransaction immutable balanceBefore/After recorded
- **Review**: replay same IPN twice → idempotent; Money type BIGINT VND only (no decimals)

### Day 16 — Mon 2026-06-15 — Payment & Wallet: Booking payment + Refund ([SCV-88](https://hoangvutran088.atlassian.net/browse/SCV-88) cont.)
- `POST /payments/booking` accept Wallet (instant) or VNPay (redirect)
- Wallet payment: atomic debit Wallet + credit PlatformWallet (holding) within tx
- VNPay booking payment: redirect → IPN → debit user (no Wallet) → credit PlatformWallet
- Booking → CONFIRMED on PaymentSucceeded event; seat HELD → BOOKED
- Refund flow: Booking → CANCELLED → Wallet credit (refund to Wallet always per v1)
- RefundFailureLog table for retry max 5
- **DoD**: passenger pays via Wallet → booking CONFIRMED in 1 tx; VNPay payment → booking CONFIRMED on IPN; cancel → refund to wallet
- **Review**: payment timeout 15 min → booking auto-released; refund retry on Wallet credit failure

### Day 17 — Tue 2026-06-16 — Booking cancellation + BookingStats ([SCV-90](https://hoangvutran088.atlassian.net/browse/SCV-90))
- `POST /bookings/{id}/cancel` validates Trip status (block if IN_PROGRESS/COMPLETED)
- Refund % calculated from Operator.cancellationPolicy JSON (passenger sees preview before confirm)
- `BookingStats` table per operator with counters (totalBookings, confirmedRevenue, cancelledCount, etc.)
- BookingStats updated via lifecycle events from Outbox (no direct DB write from other services)
- `GET /operators/{id}/booking-stats?from=&to=` aggregation endpoint for operator dashboard
- **DoD**: cancel returns correct refund amount per policy; stats counters update within 5s of event
- **Review**: cancellation preview matches actual refund; multiple cancellations don't double-count

### Day 18 — Wed 2026-06-17 — DriverSchedule + Manifest + Boarding APIs ([SCV-92](https://hoangvutran088.atlassian.net/browse/SCV-92))
- `GET /driver/me/schedule?from=&to=` returns assigned trips (filtered by `driverId` from JWT claim)
- `GET /trips/{id}/manifest` returns passenger list ordered by pickup stop, includes only operational fields (seatNumber, bookingCode, status), no PII per passenger
- `POST /trips/{id}/boarding/passenger/{passengerRecordId}` driver/assistant tick boarding
- `POST /trips/{id}/boarding/qr-scan` accepts bookingCode, returns matching passenger records
- Boarding warning logic: if Trip leaves a stop with PENDING passengers, emit event for Driver App alert
- **DoD**: driver opens schedule → sees today's trip → opens manifest → ticks 5 passengers → leaves stop with warning if missing
- **Review**: QR scan with bookingCode of different trip returns 422; manifest doesn't leak passenger PII

### Day 19 — Thu 2026-06-18 — Booking monitor API for operators ([SCV-96](https://hoangvutran088.atlassian.net/browse/SCV-96))
- `GET /operator/bookings?status=&tripId=&date=&page=` tenant-scoped query (filters by operatorId from JWT)
- `GET /operator/bookings/{id}` detail with status timeline (events from Outbox audit)
- Pagination + sort + filter by passenger phone/bookingCode
- Operator can only see bookings for trips under their operator (enforced at SQL level + middleware)
- **DoD**: operator queries booking list → only sees own operator's bookings; cross-operator query returns 403
- **Review**: SQL injection test on phone filter; pagination boundary (skip past last page returns empty)

### Day 20 — Fri 2026-06-19 — Sprint 3 buffer + demo prep
- Bug sweep on Sprint 3 deliverables
- Wire all 8 Sprint 3 subtasks for E2E test
- Update Postman collection to cover passenger journey: register → login → topup → search → book → pay → cancel
- Sprint 3 demo: full booking flow with VNPay sandbox + operator booking monitor
- **DoD**: E2E Postman flow runs green; demo deck ready for Sprint review
- **Review**: external reviewer runs full Postman collection without errors
- **Sprint 3 demo**: passenger app + VNPay + operator monitor working together

---

## Sprint 4 — Trip Operations + Parcel (Day 21–30)

**Sprint goal**: Trip lifecycle automation + Schedule disruption handling + Parcel basics shipped.

### Day 21 — Mon 2026-06-22 — Trip lifecycle automation ([SCV-98](https://hoangvutran088.atlassian.net/browse/SCV-98))
- Hangfire jobs: Trip auto-transition SCHEDULED → BOARDING at T-30min
- `POST /driver/trips/{id}/start` Driver triggers IN_PROGRESS (primary trigger, NOT GPS)
- `POST /driver/trips/{id}/complete` manual completion + Hangfire fallback at ETA+30min if not completed
- TripStarted event → Parcel transitions LOADED → IN_TRANSIT
- TripCompleted event → Booking status COMPLETED (for non-NO_SHOW passengers)
- **DoD**: SCHEDULED trip auto-transitions to BOARDING 30 min before departure; driver starts/completes; fallback works
- **Review**: time-travel test using fake clock; verify TripStarted triggers Parcel transitions correctly

### Day 22 — Tue 2026-06-23 — Trip edit snapshot + Pricing rules
- `PATCH /operator/trips/{id}` allow edit of baseFare/notes/vehicleId pre-IN_PROGRESS
- Snapshot rule: CONFIRMED bookings keep old fare even if Trip.baseFare changes (no re-query at PaymentSucceeded)
- Future-dated pricing via RouteStopFareTemplate effectiveFrom/Until — query returns active template at booking time
- Route active-status guard: cannot change routeId of trip with confirmed bookings
- DriverSchedule edit cascade: FUTURE_ONLY vs ALL_PENDING choice
- **DoD**: operator edits trip baseFare → new bookings use new fare, old bookings keep old; schedule edit cascade respects choice
- **Review**: snapshot integrity test (book → operator edits fare → cancel → refund uses original fare)

### Day 23 — Wed 2026-06-24 — Schedule change 3 levels + BookingPendingAction ([SCV-100](https://hoangvutran088.atlassian.net/browse/SCV-100))
- `PATCH /operator/trips/{id}/schedule` changes departureDateTime
- Classify delta per v7 6.13: MINOR (auto-inform), MEDIUM (50% refund if cancel), MAJOR (100% refund if cancel)
- BookingPendingAction created for MEDIUM/MAJOR with type SCHEDULE_CHANGE
- Passenger endpoints: `POST /bookings/{id}/pending-actions/{actionId}/accept|reject`
- Hangfire timeout: pending action auto-fallback if user doesn't respond within deadline
- **DoD**: operator changes departure by +3h → all confirmed bookings get MAJOR pending action; passenger accepts or auto-refund
- **Review**: only ONE active pending action per booking allowed; timeout cleanup runs

### Day 24 — Thu 2026-06-25 — Stop disable + No-show
- `DELETE /operator/stops/{id}` (soft delete with replacedByStopId for migration)
- Bookings using disabled stop get BookingPendingAction STOP_DISABLED with deadline `min(now+24h, departure-2h)`
- Passenger chooses replacement stop OR terminal OR cancel 100% refund
- Auto-fallback to terminal at deadline
- NO_SHOW handler: Trip leaves stop+15min without passenger tick → Booking NO_SHOW (no refund); PARTIAL_NO_SHOW for partial booking
- **DoD**: stop disable creates pending actions for affected bookings; NO_SHOW marked correctly after grace period
- **Review**: NO_SHOW vs PARTIAL_NO_SHOW (3/5 boarded) accuracy test

### Day 25 — Fri 2026-06-26 — Parcel Service: ParcelRouteFare + Create parcel ([SCV-104](https://hoangvutran088.atlassian.net/browse/SCV-104))
- EF migration: ParcelRouteFare, Parcel, ParcelStats tables
- `POST /operator/parcel-route-fares` per (routeId, sizeCategory) configuration
- `POST /parcels` passenger creates parcel (booking-attached OR parcel-only path)
- Validation: ParcelRouteFare must exist for route+size; trip must be SCHEDULED/BOARDING
- ParcelCode QR generator (plain `VR-PCL-yyyyMMdd-XXXXXXXX`)
- **DoD**: passenger creates booking-attached parcel; parcel-only flow via `/parcels/available-trips`; no fare config returns 422
- **Review**: parcel-only on IN_PROGRESS trip returns TRIP_NOT_ACCEPTING_PARCEL

### Day 26 — Mon 2026-06-29 — Parcel: Deposit + Reweigh + EXTRA_LARGE review
- Deposit payment endpoint: same flow as booking (Wallet/VNPay)
- `POST /assistant/parcels/{id}/reweigh` actual weight + additional charge calculation
- PENDING_ADDITIONAL_PAYMENT status + Hangfire timeout (per parcelNoShowPolicy deadline)
- EXTRA_LARGE size → PENDING_OPERATOR_REVIEW (no charge, no capacity lock until approve)
- `PATCH /operator/parcels/{id}/review` approve/reject with reason; auto-reject after 24h
- **DoD**: small/medium parcel: deposit → confirmed; EXTRA_LARGE: review → approve → deposit → confirmed
- **Review**: reweigh exceeding estimate triggers additional payment flow; auto-reject timeout fires

### Day 27 — Tue 2026-06-30 — Parcel: Load + In-transit + Unload ([SCV-106](https://hoangvutran088.atlassian.net/browse/SCV-106))
- `POST /assistant/parcels/{id}/load` scan QR + confirm LOADED (validates trip is current + parcel CONFIRMED)
- TripStarted event → all LOADED parcels → IN_TRANSIT
- `POST /assistant/parcels/{id}/unload` at TripStop ARRIVED (validates dropoffStopId match)
- Operator override `PATCH /operator/parcels/{id}/status` with reason/audit
- **DoD**: assistant scans QR → LOADED; TripStarted → IN_TRANSIT; arrival at stop → UNLOAD enabled
- **Review**: unload before stop ARRIVED returns 422; operator override audit log persisted

### Day 28 — Wed 2026-07-01 — Parcel: Dropoff at Stop + Capacity counter ([SCV-108](https://hoangvutran088.atlassian.net/browse/SCV-108))
- `dropoffStopId` field on Parcel: null = destination terminal, else must be RouteStop with allowDropoff=true. Carry-over: arrival anchor cho destination terminal xử lý ở Day 28/39; P0 chỉ gate stop thật khi có `dropoffStopId`.
- Trip cargo counters: `totalReservedWeightKg`, `totalLoadedWeightKg`, atomic update with parcel status transitions
- Near-full alert: when `totalLoadedWeightKg >= 0.8 * vehicleMaxCargoKg`, emit `trip.cargo_near_full` event
- `GET /operator/trips/{id}/cargo-capacity` returns reserved/loaded/max/percentFull
- **DoD**: parcel with dropoffStopId honors stop selection; capacity counter updates atomically; 80% threshold triggers event
- **Review**: concurrent parcel create at capacity limit — only one wins; counter consistency

### Day 29 — Thu 2026-07-02 — Sprint 4 integration buffer
- Bug sweep Sprint 4 deliverables
- E2E test: full trip lifecycle with parcels
  - Operator creates trip → driver starts → assistant loads 3 parcels → trip in-progress → arrival at stop → unload 1 parcel → trip complete
- Wire NotificationService consumer for new events from Trip/Parcel
- **DoD**: full lifecycle E2E passes; events flow Trip → Notification
- **Review**: Tuyên confirms NestJS consumers receive all new events from Sprint 4

### Day 30 — Fri 2026-07-03 — Sprint 4 demo prep
- Update Postman collection: trip lifecycle + parcel flow
- Sprint 4 demo: operator creates schedule → trip auto-generates → driver runs trip → parcel load/unload
- Document any spillover for Sprint 5
- **DoD**: demo green; Sprint 5 prep doc written
- **Sprint 4 demo**: trip automation + parcel basic flow E2E

---

## Sprint 5 — Disruption Handling + Subscription (Day 31–40)

**Sprint goal**: Vehicle Substitution + Trip Disruption + Shuttle + Subscription/Invoice/Settlement complete.

### Day 31 — Mon 2026-07-06 — Parcel delivery confirmation + cancel ([SCV-110](https://hoangvutran088.atlassian.net/browse/SCV-110))
- `deliveryToken` 48h generated at parcel UNLOAD
- Email link `/public/parcels/confirm?token=` (no JWT needed, token is proof)
- `POST /public/parcels/confirm` recipient ACCEPT/REJECT
- Reject undo within 15 min
- Manual confirm endpoint for assistant/operator if no email (with audit note)
- Hangfire: 7-day re-alert if no confirmation; expired token does NOT auto-confirm
- **DoD**: recipient receives email → opens link → accepts → parcel DELIVERED; resend revokes old token
- **Review**: expired token returns 400 `PARCEL_DELIVERY_TOKEN_EXPIRED`; reject + `POST /v1/parcels/delivery/undo-reject` within 15min reverts to `DELIVERED_PENDING_CONFIRM`

### Day 32 — Tue 2026-07-07 — Parcel cancel/return/transfer flows
- Parcel auto-cancel after Trip cancel (with refund per parcelNoShowPolicy)
- Operator manual cancel with refund choice
- PENDING_OPERATOR_ACTION status with 2h re-alert
- RETURNED status for failed delivery returning to sender
- Capacity counter release on each cancel state transition
- **DoD**: all parcel cancellation paths refund correctly; capacity released
- **Review**: state machine completeness test (each transition documented + tested)

### Day 33 — Wed 2026-07-08 — Trip disruption: operator cancel + alternative route ([SCV-112](https://hoangvutran088.atlassian.net/browse/SCV-112))
- `POST /operator/trips/{id}/cancel` preview (affected bookings + refund total) + confirm endpoint pair
- Trip CANCELLED (only allowed pre-IN_PROGRESS) → bulk Booking CANCELLED → bulk refund 100%
- `POST /operator/trips/{id}/change-route` swap to AlternativeRoute
- BookingPendingAction ROUTE_CHANGE for affected bookings (passenger picks new stop or cancel 100%)
- Window: 30 min if IN_PROGRESS, 60 min before progress
- **DoD**: trip cancel triggers bulk refunds via Outbox (eventual, retry max 5); route change creates pending actions
- **Review**: refund failure retry; partial refund-failure doesn't block trip CANCELLED status

### Day 34 — Thu 2026-07-09 — Vehicle Substitution + BookingTransfer ([SCV-114](https://hoangvutran088.atlassian.net/browse/SCV-114))
- `POST /operator/trips/{id}/substitute-vehicle` for IN_PROGRESS trips
- Creates Trip_new (status BOARDING, source VEHICLE_SUBSTITUTION) with new vehicle
- Trip_old → DISRUPTED hasSubstitution=true
- BookingTransfer entity: 1 row per Passenger record in Booking, status PENDING_CONFIRM
- Driver/Assistant on Trip_new confirms physical transfer per passenger
- **DoD**: substitution creates new trip + transfer records; passengers can be confirmed individually
- **Review**: partial substitution (3/5 passengers on new vehicle) tracked correctly

### Day 35 — Fri 2026-07-10 — Parcel transfer in substitution + Disrupted no-substitution
- Parcel transfer: LOADED/IN_TRANSIT → PENDING_TRANSFER_CONFIRM on Trip_new
- 30-min timeout → TRANSFER_ESCALATED, operator manual handle
- DISRUPTED no-substitution path: `POST /operator/trips/{id}/disrupt-no-substitution`
- Refund proportional to traveledRatio (use TripStop.distanceFromOriginKm or fallback stop order)
- Floor 1000 VND on refund amount
- **DoD**: parcels transferred or escalated correctly; no-substitution refunds proportionally
- **Review**: edge case — Trip with NO stop arrivals refunds 100%; partial completed refunds proportional

### Day 36 — Mon 2026-07-13 — Shuttle backend ([SCV-116](https://hoangvutran088.atlassian.net/browse/SCV-116))
- EF migration: ShuttleTrip, ShuttlePassenger tables
- Passenger requests shuttle at booking time IF pickup Station.supportsShuttle=true
- Operator `GET /operator/shuttle-requests` lists pending requests
- `POST /operator/shuttle-trips` creates manual ShuttleTrip with assigned driver/vehicle, links ShuttlePassenger records
- Shuttle tracking re-uses Tracking service (same Socket.IO room pattern)
- Shuttle is FREE in v1 (no payment flow)
- **DoD**: passenger selects shuttle pickup → operator sees request → creates trip → passengers linked
- **Review**: shuttle at non-supportsShuttle station rejected at booking

### Day 37 — Tue 2026-07-14 — Subscription lifecycle ([SCV-118](https://hoangvutran088.atlassian.net/browse/SCV-118))
- EF migration: SubscriptionPlan, OperatorSubscription tables (already partial from Sprint 2 — extend)
- Resource limits (maxTrips, maxVehicles, maxUsers) + module flags (enableParcel, enableShuttle, enableRag)
- OperatorSubscription state machine: TRIAL → ACTIVE → PENDING_PAYMENT → EXPIRED → CANCELLED
- Subscription payment endpoint: `POST /operator/subscription/pay` Wallet/VNPay
- Block operations exceeding limits with 429 + warn at 80%
- **DoD**: operator subscription lifecycle works; over-limit blocked; warning sent at 80%
- **Review**: subscription expires → operator can read but not create; module flag off → endpoint returns 403

### Day 38 — Wed 2026-07-15 — Invoice PDF + PlatformWallet + Settlement
- Invoice entity + PDF generator (use QuestPDF or similar)
- Upload to Firebase Storage with signed URL
- Trigger on SubscriptionPayment SUCCEEDED → Invoice generated → notify operator
- PlatformWallet ledger: hold on booking/parcel revenue → ELIGIBLE at Trip terminal+7days → Monday auto-settle to OperatorWallet
- OperatorTripSettlement records per Trip
- Manual admin settle endpoint
- **DoD**: subscription payment generates invoice PDF; weekly Monday job settles eligible trips
- **Review**: settlement amount = revenue − platform fee % per OperatorSubscription plan; ledger balanced

### Day 39 — Thu 2026-07-16 — Driver Ops incident + TripStop arrival ([SCV-120](https://hoangvutran088.atlassian.net/browse/SCV-120))
- EF migration: Incident table
- `POST /driver/trips/{id}/incident` category enum + description + up to 3 image URLs + GPS + tripId
- Operator receives event → dashboard alert + push
- `POST /assistant/trip-stops/{id}/arrive` sets TripStop.actualArrivalTime + status ARRIVED
- Endpoint role: DRIVER or ASSISTANT
- **DoD**: incident reported → operator notified; TripStop arrival button enables UNLOAD parcel actions
- **Review**: incident does NOT auto-change Trip.status; arrival before Trip IN_PROGRESS returns 422

### Day 40 — Fri 2026-07-17 — Admin users + Station cleanup + Reports backend ([SCV-122](https://hoangvutran088.atlassian.net/browse/SCV-122))
- `GET /admin/users` with filters + lock/unlock endpoints
- `GET /admin/activity-logs` query with action/user/date filters
- Station merge endpoint: `POST /admin/stations/merge` source → target, re-link OperatorStation rows, audit
- Station normalize endpoint: update name/coords with audit
- Platform reports aggregate: `GET /admin/reports/platform?from=&to=` (cross-operator revenue, trips, parcels)
- **DoD**: admin merges 2 duplicate stations; activity log queryable; platform report returns aggregated numbers
- **Review**: merge re-links all OperatorStation correctly; activity log cannot be modified after write
- **Sprint 5 demo**: full disruption + substitution scenarios + admin operations

---

## Sprint 6 — Reporting + Polish + Demo (Day 41–50)

**Sprint goal**: Excel export, reliability hardening, full E2E rehearsal, capstone demo.

### Day 41 — Mon 2026-07-20 — Operator Excel export backend ([SCV-125](https://hoangvutran088.atlassian.net/browse/SCV-125))
- Use ClosedXML library for .xlsx generation
- Endpoints: `GET /operator/reports/bookings/export?from=&to=`, `/parcels/export`, `/revenue/export`, `/occupancy/export`, `/cancellation/export`
- Stream response (no memory bloat for large datasets)
- Tenant filter operatorId in every query
- **DoD**: operator downloads 6 reports (booking, parcel, revenue, occupancy, cancellation, refund) as Excel files
- **Review**: large dataset (10k rows) export doesn't OOM; tenant isolation verified

### Day 42 — Tue 2026-07-21 — Platform reports backend stabilization
- Cross-operator aggregate endpoints for System Admin
- BookingStats + ParcelStats aggregation across operators
- Cache hot queries (5 min Redis TTL)
- Performance check: report endpoints respond <2s for typical period (1 month)
- **DoD**: admin platform report endpoint stable; perf acceptable
- **Review**: query 3-month period without timeout

### Day 43 — Wed 2026-07-22 — Reliability hardening: Outbox + Idempotency review ([SCV-131](https://hoangvutran088.atlassian.net/browse/SCV-131))
- Outbox dead-letter handling: events failed > 5 retries land in OutboxDLQ table for admin review
- `GET /admin/outbox/dlq` endpoint
- Idempotency-Key coverage audit: ensure ALL mutation endpoints in Booking, Payment, Parcel use it
- Hangfire job health endpoint: `/internal/jobs/status` returns last-run + next-run + lag
- **DoD**: DLQ captures failed events; idempotency audit shows 100% mutation endpoint coverage
- **Review**: chaos test — kill RabbitMQ, verify Outbox retains events; restart, events drained

### Day 44 — Thu 2026-07-23 — E2E seed data + demo scenarios ([SCV-133](https://hoangvutran088.atlassian.net/browse/SCV-133))
- Seed script for demo environment:
  - 1 System Admin, 3 Operators (each PLAN=STARTER_TRIAL or BUSINESS)
  - 5 Stations covering Saigon + Mien Tay region
  - 3 routes per operator + alternative routes
  - 9 vehicles (3 per operator, mixed VehicleType)
  - DriverSchedules generating 30 days of trips
  - 10 passenger accounts pre-topup-ed
  - 5 vouchers (mix VIETRIDE_FUNDED + OPERATOR_FUNDED)
  - 1 RAG KB document per access level
- **DoD**: seed script runs in <2 min; demo env is reproducible
- **Review**: re-run seed = idempotent; demo accounts can immediately book/parcel

### Day 45 — Fri 2026-07-24 — E2E rehearsal: passenger journey
- Demo scenario 1: passenger register → search → book → pay → track → board → cancel partial
- Demo scenario 2: passenger sends parcel → driver loads → unloads at stop → recipient confirms via email
- Identify any rough edges; fix critical bugs only (no new features)
- **DoD**: 2 scenarios end-to-end without manual intervention
- **Review**: external observer (mentor/another team) can run scenarios from app UI

### Day 46 — Mon 2026-07-27 — E2E rehearsal: operator + driver journey
- Demo scenario 3: operator approves voucher, manages booking monitor, exports report
- Demo scenario 4: operator handles disrupted trip with vehicle substitution + parcel transfer
- Demo scenario 5: driver runs full trip with manifest, GPS, boarding, parcel handling
- **DoD**: 3 more scenarios green
- **Review**: timing — full demo runs in <30 min total

### Day 47 — Tue 2026-07-28 — Bug fix sweep + perf tuning
- Triage open bugs from rehearsals
- Add DB indexes for slow queries identified in perf review
- Tune Hangfire job intervals
- Review log volume (silence noisy infos to warn)
- **DoD**: open bug count = 0 for demo blockers
- **Review**: query perf P95 <500ms for hot paths (search, manifest, monitor)

### Day 48 — Wed 2026-07-29 — Capstone demo dry-run #1
- Full demo dry-run with all team + mentor
- Time each segment, identify long pauses
- Polish demo script (English vs Vietnamese for delivery)
- Prepare backup screenshots for any flaky parts
- **DoD**: dry-run #1 completed; feedback collected
- **Review**: mentor signs off on dry-run

### Day 49 — Thu 2026-07-30 — Capstone demo dry-run #2 + final polish
- Address feedback from dry-run #1
- Final UI/data polish in demo environment (no code changes if avoidable)
- Verify all 8 services healthy, RabbitMQ flowing, Redis OK
- Backup demo environment snapshot
- **DoD**: dry-run #2 better than #1; backup snapshot taken
- **Review**: full team agrees demo ready

### Day 50 — Fri 2026-07-31 — Capstone demo day + retrospective
- Capstone final demo to mentor/committee
- Post-demo: archive demo recording
- Team retrospective: lessons learned, technical debt log for v2
- Update [BACKEND_SOURCE_OF_TRUTH.md](BACKEND_SOURCE_OF_TRUTH.md) with v1 ACTUAL vs DESIGNED deviations
- Write handover doc for any v2 work
- **DoD**: demo done; retro doc committed; capstone v1 closed

---

## Cross-cutting standing items (every day)

These are daily habits, NOT separate tasks:

- **Morning** (15 min): check Jira board, pick next subtask, update status to In Progress
- **Per PR**: include unit tests for new endpoints (≥1 happy path, ≥1 error case); update Postman collection
- **Per endpoint**: must include OpenAPI annotation (Swashbuckle) so FE devs can generate clients
- **Per new .NET endpoint**: add corresponding route entry in `services/gateway/routes.config.ts` so Gateway proxies it (FE always calls through Gateway, never directly to service)
- **Per migration**: must be reversible (Down() method); never edit a merged migration
- **End of day** (15 min): commit + push branch; update Jira ticket with progress note
- **Friday EoD**: open PR for review; tag Tuyên (NestJS contract changes) + relevant FE dev for breaking changes

## Daily review/test checklist template

For the review/test agent on each PR/day:

1. **Build**: `dotnet build` clean on a fresh clone
2. **Unit tests**: `dotnet test` all green; new code coverage ≥60%
3. **Migration**: `dotnet ef database update` runs clean from empty DB
4. **API contract**: Swagger renders; new endpoints documented
5. **Integration**: at least 1 happy path through new endpoints via Postman
6. **Events**: if endpoint emits Outbox event, verify event payload matches consumer schema (cross-check with Tuyên)
7. **Security**: JWT required on protected endpoint; tenant isolation if operator-scoped
8. **Idempotency**: mutation endpoints accept `Idempotency-Key` header
9. **Performance**: hot endpoints respond <500ms in P95 on dev data
10. **Jira**: subtask status updated; PR linked

## Spillover/contingency plan

If a day slips:
- Sprint 2 spillover → push to Day 11 morning (lose half day of Trip Search)
- Sprint 3 spillover → bookend Day 20 (the buffer day)
- Sprint 4 spillover → squeeze Parcel delivery into Day 29 evening
- Sprint 5 spillover → trim Settlement weekly job (use manual settle only) — flag for v2
- Sprint 6 spillover → drop Excel report aesthetics; keep functional only

**Hard stop rules** (cannot skip):
- Identity, Outbox baseline, Booking core, Payment with VNPay IPN, Trip lifecycle automation
- Everything else is negotiable for demo

## References

- [BACKEND_SOURCE_OF_TRUTH.md](BACKEND_SOURCE_OF_TRUTH.md) — authoritative DB/event/API contracts
- [SU26SE101_VIETRIDE_technical_context_v7.md](SU26SE101_VIETRIDE_technical_context_v7.md) — section 4.x for service detail, 5.x for auth, 6.x for business flows, 7xxx for entity specs
- Jira: https://hoangvutran088.atlassian.net/jira/software/projects/SCV
- Feature Backlog: Google Drive "Feature List & Scope" → Feature Backlog tab F001–F071 (v1 only)
