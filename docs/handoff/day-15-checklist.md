# Day 15 — Final checklist

> Produced by `/audit-day 15` after independent source audit and full verification.
> Re-run after the schema trigger gap was fixed: no remaining Day-15 gap found.

- **Timeline ref**: BE_TIMELINE_VU.md → Day 15 — Payment & Wallet: Wallet + VNPay top-up (Jira: SCV-88)
- **Plan**: docs/handoff/day-15-plan.md
- **Status**: ✅ READY

## DoD result

- [x] ✅ EF migration `InitPaymentSchema` creates the Day-15 Payment/Wallet schema and is reversible. Evidence: migration creates `outbox_events`, `payments`, `platform_wallet_transactions`, `platform_wallets`, `top_up_requests`, `wallet_transactions`, `wallets`; `Down()` drops them. The audit re-ran fresh-from-empty on `vietride_payment_audit_day15_fresh`, inspected tables/triggers, rolled back to `0`, then dropped the throwaway DB.
- [x] ✅ Canonical `updated_at` triggers now match `db-schema/payment-wallet/schema.sql:481-495`. Evidence: `20260612102736_InitPaymentSchema.cs:288-312` creates `vietride_payment.trg_set_updated_at()` plus triggers for `payments`, `top_up_requests`, `wallets`, `platform_wallets`; `Down()` drops them at `20260612102736_InitPaymentSchema.cs:318-323`; live `pg_trigger` query returned 4 rows.
- [x] ✅ Passenger top-up of 100k via VNPay sandbox works end-to-end through Gateway. Evidence: re-audit setup `register 201 → verify 200 ACTIVE → login 200 → wallet 200 balance 0`; Newman Day-15 folder ran `7 requests / 14 assertions / 0 failures`; wallet increased by `100000`; top-up ledger row existed.
- [x] ✅ Replaying the same VNPay IPN is idempotent. Evidence: Newman replay IPN returned `200` and asserted no double-credit; code uses Redis reservation + `PENDING` row lock/guard.
- [x] ✅ Money is BIGINT VND only and no decimal money type was introduced. Evidence: schema/migration use `bigint`; `CreateTopUpCommandHandler.cs:38-44` enforces `WALLET_TOP_UP_AMOUNT_TOO_LOW` for `< 10000`; Newman used `100000` VND and VNPay raw amount `10000000`.
- [x] ✅ Wallet auto-create on `identity.user.created` is implemented and idempotent. Evidence: `InfrastructureServiceCollectionExtensions.cs:82-88` binds `payment.wallet-bootstrap` to `identity.user.created`; `WalletRepository.cs:46-50` uses `ON CONFLICT (user_id) DO NOTHING`.
- [x] ✅ `POST /v1/wallet/top-up` is authenticated, passenger-only, idempotency-header protected, creates PENDING top-up, and returns signed VNPay redirect URL. Evidence: `WalletController.cs:72-92`; Newman `POST /v1/wallet/top-up` returned `201` with `status=PENDING`.
- [x] ✅ VNPay IPN endpoint is public, validates HMAC, credits Wallet, records immutable balance snapshots, emits canonical `payment.wallet.credited`, and returns VNPay machine JSON. Evidence: `VnPayIpnController.cs:8-32`, `ConfirmTopUpCommandHandler.cs:51-140`, `WalletRepository.cs:109-133`, `WalletCreditedIntegrationEvent.cs:12`; Newman success `200`, invalid signature `401`.
- [x] ✅ VNPay Return URL remains FE-owned; no backend Return URL business handler was added. Evidence: Payment API exposes IPN controller; `VnPayOptions`/runtime config keep return URL as FE path.
- [x] ✅ Top-up timeout after 15 minutes is implemented through Hangfire. Evidence: Hangfire wiring in Payment startup/DI, `TopUpExpiredJob`, `ExpireTopUpCommandHandler`, `TopUpRequestRepository.cs:57-68` updating only stale `PENDING` rows.
- [x] ✅ `GET /v1/wallet` and `GET /v1/wallet/transactions` are authenticated/user-scoped and return ADR 0004 envelopes. Evidence: `WalletController.cs:17-20,32-64`; Newman verified wallet before/after and transaction listing through Gateway.
- [x] ✅ Day-15 Review adversarial case executed: same IPN replay is idempotent, invalid signature is rejected, and money stays BIGINT VND.

## Tasks completed

- Task 15.0 — Payment architecture baseline — ✅ implemented and verified.
- Task 15.1 — Payment entities + EF mapping + `InitPaymentSchema` migration — ✅ implemented; canonical triggers fixed and verified.
- Task 15.2a — Shared inbound RabbitMQ consumer abstraction — ✅ implemented and verified.
- Task 15.2 — Wallet bootstrap consumer — ✅ implemented and verified.
- Task 15.3 — VNPay client + `POST /v1/wallet/top-up` — ✅ implemented and verified.
- Task 15.4 — VNPay top-up IPN + `payment.wallet.credited` event — ✅ implemented and verified.
- Task 15.5 — Top-up timeout job + Hangfire wiring — ✅ implemented and verified.
- Task 15.6 — Wallet read endpoints — ✅ implemented and verified.

## Changed files

Branch-level Day-15 changes vs `main` include:

- `Directory.Packages.props` — Hangfire CPM versions.
- `apps/payment/src/VietRide.Payment.Api/**` — Payment startup, wallet/VNPay controllers, request DTO/config.
- `apps/payment/src/VietRide.Payment.Application/**` — VNPay abstraction, wallet/top-up/read CQRS, events, repository contracts.
- `apps/payment/src/VietRide.Payment.Domain/**` — Payment/Wallet/TopUp/PlatformWallet entities and enums.
- `apps/payment/src/VietRide.Payment.Infrastructure/**` — EF DbContext/configurations/migration/repositories, VNPay client, Hangfire job, DI, internal JWT factory.
- `apps/payment/tests/**` — Day-15 Payment unit/integration coverage.
- `libs/dotnet/VietRide.Shared.Messaging/**` and `tests/dotnet/VietRide.Shared.Messaging.UnitTests/**` — inbound RabbitMQ consumer abstraction/options/background service and tests.
- `apps/gateway/src/config/routes.ts` — public VNPay IPN and user wallet routes.
- `docs/api/postman/vietride.postman_collection.json` and `vietride.local.postman_environment.json` — Day-15 Newman flow/env.
- `infra/docker/docker-compose.yml`, `.env.example` — local/runtime VNPay return URL alignment.
- `apps/notification/Dockerfile` — runtime build support change present in current diff.
- `docs/handoff/day-15-checklist.md` — this audit checklist.

## Verification run

| Command | Result | Notes |
|---|---|---|
| `dotnet build apps/payment/VietRide.Payment.sln -c Release` | PASS | Re-run after fix: `0 Warning(s)`, `0 Error(s)`. |
| `dotnet format apps/payment/VietRide.Payment.sln --verify-no-changes` | PASS | Re-run after fix: no output. |
| `dotnet test apps/payment/VietRide.Payment.sln -c Release --no-build` | PASS | Payment unit `32/32`; Payment integration `5/5`. |
| `dotnet build libs/dotnet/VietRide.Libs.sln -c Release` | PASS | Re-run after fix: `0 Warning(s)`, `0 Error(s)`. |
| `dotnet format libs/dotnet/VietRide.Libs.sln --verify-no-changes` | PASS | Re-run after fix: no output. |
| `dotnet test libs/dotnet/VietRide.Libs.sln -c Release --no-build` | PASS | Shared Messaging `4/4`; Shared Persistence `4/4`; Shared Web `71/71`. |
| `npx nx run-many -t build --all --exclude="VietRide.*"` | PASS | 10 TS projects + 2 dependent generate tasks; source-map warnings only. |
| `npx nx run-many -t lint --all --exclude="VietRide.*"` | PASS | 14 TS projects linted successfully. |
| `npx nx run-many -t test --all --exclude="VietRide.*" --ci --passWithNoTests` | PASS | Contracts `27/27`, Gateway `72/72`, Tracking `29/29`, Notification `69/69`, RAG `2/2`; no-test libs exited 0. |
| `docker exec vietride_postgres psql -U vietride -d postgres -c "DROP DATABASE IF EXISTS vietride_payment_audit_day15_fresh;"` | PASS | Fresh audit DB reset. |
| `docker exec vietride_postgres psql -U vietride -d postgres -c "CREATE DATABASE vietride_payment_audit_day15_fresh OWNER vietride;"` | PASS | Fresh throwaway DB created. |
| `PAYMENT_DESIGN_CONNECTION=... dotnet ef database update -p apps/payment/src/VietRide.Payment.Infrastructure -s apps/payment/src/VietRide.Payment.Api` | PASS | Applied `20260612102736_InitPaymentSchema` fresh-from-empty. EF host warning about `INTERNAL_JWT_SECRET` length did not block design-time migration. |
| `docker exec vietride_postgres psql -U vietride -d vietride_payment_audit_day15_fresh -c "select table_name from information_schema.tables where table_schema='vietride_payment' ..."` | PASS | Tables: `__ef_migrations_history`, `outbox_events`, `payments`, `platform_wallet_transactions`, `platform_wallets`, `top_up_requests`, `wallet_transactions`, `wallets`. |
| `docker exec vietride_postgres psql -U vietride -d vietride_payment_audit_day15_fresh -c "select tgname, tgrelid::regclass::text ... from pg_trigger where not tgisinternal ..."` | PASS | 4 triggers: `trg_payments_updated_at`, `trg_platform_wallets_updated_at`, `trg_top_up_requests_updated_at`, `trg_wallets_updated_at`. |
| `PAYMENT_DESIGN_CONNECTION=... dotnet ef database update 0 -p apps/payment/src/VietRide.Payment.Infrastructure -s apps/payment/src/VietRide.Payment.Api` | PASS | Rolled back `20260612102736_InitPaymentSchema` cleanly. |
| `docker exec vietride_postgres psql -U vietride -d postgres -c "DROP DATABASE vietride_payment_audit_day15_fresh;"` | PASS | Fresh audit DB dropped. |
| `docker compose --env-file .env -f infra/docker/docker-compose.yml --profile app up -d --build` | PASS | Full app stack rebuilt and started; Docker reported only missing Google OAuth env warnings. |
| `docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"` | PASS | Gateway, Identity, Trip, Booking, Payment, Parcel, Tracking, Notification, RAG, Postgres, RabbitMQ, Redis, PgBouncer all `healthy`. |
| `/health` matrix via `Invoke-WebRequest http://localhost:<port>/health` | PASS | `gateway 200`, `identity 200`, `trip 200`, `booking 200`, `payment 200`, `parcel 200`, `tracking 200`, `notification 200`, `rag 200`. |
| `PowerShell inline: register -> OTP from DB -> verify-email -> login -> GET /v1/wallet` | PASS | `register 201`; OTP length `6`; `verify 200 ACTIVE`; `login 200`; wallet ready `200 balance 0`; token redacted. |
| `npx newman run docs/api/postman/vietride.postman_collection.json -e docs/api/postman/vietride.local.postman_environment.json --folder "Payment - Wallet top-up (Day 15)" --env-var baseUrl=http://localhost:3000 --env-var email=<redacted> --env-var password=<redacted> --env-var accessToken=<redacted>` | PASS | Newman `7 requests`, `14 assertions`, `0 failures`; statuses: wallet before `200`, top-up `201`, IPN success `200`, wallet after `200`, transactions `200`, replay IPN `200`, invalid signature `401`. |
| Review artifact validation | PASS | Postman collection/env parsed and Day-15 folder executed. |
| Review execution against Docker/local stack | PASS | Functional flow ran against rebuilt Docker stack via Gateway; replay and invalid-signature cases executed. |
| `grep <PackageReference ... Version=>` in `*.csproj` | PASS | No matches. |
| Banned dependency declaration scan for AutoMapper/OpenTelemetry/Prometheus/Grafana/Tempo/Loki/Hangfire commercial/MediatR v12+ | PASS | No manifest declarations found. |
| `git log --format=%B main..HEAD | Select-String -Pattern 'Co-Authored-By'` | PASS | No co-author trailer found. |
| `git ls-files --eol -- <changed files>` | PASS | `.cs/.csproj` files are CRLF; `.json/.yml/.md` files are LF as required. |

## Contract / event / schema changes shipped

- REST endpoints implemented/verified:
  - `POST /v1/wallet/top-up`
  - `GET /v1/wallet`
  - `GET /v1/wallet/transactions`
  - `POST /v1/payments/vnpay-topup-ipn`
- Gateway routes verified:
  - public `POST /v1/payments/vnpay-topup-ipn`
  - user-auth `/v1/wallet`
- Schema shipped:
  - `20260612102736_InitPaymentSchema` creates the Day-15 Payment/Wallet schema plus `outbox_events` and now includes canonical update triggers for mutable Day-15 tables.
- Event shipped:
  - canonical `payment.wallet.credited`; already registered in BSOT.
- Error codes used:
  - Existing BSOT codes only, including `WALLET_TOP_UP_AMOUNT_TOO_LOW` and `PAYMENT_SIGNATURE_INVALID`.
- BSOT registry/changelog:
  - No new event/error registry entry was needed; no BSOT changelog update required.
- Timeline erratum recorded:
  - Timeline says informal `topup.succeeded` and Return URL handler; implementation correctly follows canonical event `payment.wallet.credited` and keeps backend business source of truth on IPN.

## Known gaps & carry-over for Day 16

- None blocking Day 15.
- Keep the canonical update-trigger pattern in future Payment migrations when mutable tables carry `updated_at`.
- Keep the Redis SETNX + DB `PENDING` status guard pattern as the baseline for future payment-like IPNs.

## Notes for Day 16 planning

- Day-15 re-audit is green after the migration trigger fix.
- The real Docker stack passed the top-up `100000` VND flow through Gateway, including wallet balance update, ledger visibility, replay idempotency, and invalid-signature rejection.
- Keep `payment.wallet.credited` as the canonical wallet-credit event; do not introduce `topup.succeeded`.
