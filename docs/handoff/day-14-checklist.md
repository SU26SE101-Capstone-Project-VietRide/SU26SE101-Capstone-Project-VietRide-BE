# Day 14 — Final checklist

> Produced by `/audit-day 14`. First audit pass found a runtime-blocking migration defect (every
> voucher write 500'd) that no unit/integration test caught because the Booking integration tests
> substitute repositories. The blocker + all listed gaps were then fixed and re-verified, including
> a 16/16 real-app E2E through the Gateway. This checklist reflects the post-fix state.

- **Timeline ref**: BE_TIMELINE_VU.md → Day 14 — Voucher checkout ([SCV-86](https://hoangvutran088.atlassian.net/browse/SCV-86))
- **Plan**: docs/handoff/day-14-plan.md (RE-PLANNED: operator self-create vouchers; APPROVED 2026-06-20)
- **Status**: ✅ READY — voucher feature works end-to-end against the real running stack; full verification matrix green; **9/9 services healthy** after the follow-up fixes below.

## What was fixed during this audit (carry-overs resolved in-session)
1. **BLOCKER — migration created each voucher enum in TWO schemas** (`public`+`vietride_booking`) → Npgsql "More than one PostgreSQL type was found with the name voucher_funding_type" → HTTP 500 on every voucher write/filter. **Fixed** in `20260620034903_AddVoucherAggregates.cs`: removed the redundant `AlterDatabase().Annotation("Npgsql:Enum:voucher_*")` (which created a 2nd copy in `vietride_booking`), keeping the raw `CREATE TYPE` only — each enum is now a single copy in `public`, mirroring `booking_status`. Verified: `SELECT typname,count(*) … = 1` for all 3 voucher enums.
2. **Test gap that hid #1 — added a real-Postgres regression suite.** `VoucherPersistenceIntegrationTests` (+ `VoucherPersistenceCollection`) follows the Identity real-DB fixture (fresh DB → `MigrateAsync()` → assert each voucher enum in exactly one schema + a real `VoucherRepository` round-trip → drop DB). Integration tests 23 → **28**.
3. **Contract bug surfaced by E2E — voucher responses serialized enums as integers** (`fundingType: 1`). **Fixed**: all voucher response DTOs (CreateVoucher/ListVouchers/CreateOperatorVoucher/UpdateOperatorVoucher results + all consent results) now expose `Type`/`FundingType`/`Status` as `string` (the enum name), matching the codebase convention (`CreateBookingResult.Status: string`; no global JsonStringEnumConverter). E2E now returns `fundingType: "OPERATOR_FUNDED"`.
4. **NIT — `ConflictException` → `CodedConflictException`** for VOUCHER_CODE_CONFLICT (admin + operator create) and the 7 VOUCHER_LOCKED throws (operator update), aligning with the BSOT §5.9 UPPER_SNAKE_CASE-validated pattern. Tests updated.
5. **NIT (gateway)** — added `/v1/admin/vouchers` to `routes.spec.ts` `expectedAdminRoutes` + cross-service matcher; added an OPERATOR_STAFF→403 challenger for POST `/v1/operator/vouchers`.
6. **Env gap (not Day-14) — `tracking`/`notification` workers crash-looped** on missing Prisma `*_DATABASE_URL`. **Fixed** in `infra/docker/docker-compose.yml` (added `TRACKING_DATABASE_URL`/`NOTIFICATION_DATABASE_URL`/`RAG_DATABASE_URL` to each worker's `environment:` — the service-specific name the Prisma schema reads; compose previously only set the generic `DATABASE_URL`). Both now healthy. `rag` still down — needs real `CLOUDINARY_*` secrets (external dependency, see Known gaps).
7. **Committed E2E artifact** — `scripts/run-day14-voucher-e2e.mjs` (mirrors the Day-13 newman runner: mints dev RS256 tokens + DevTrip/DevPayment stubs) drives the full voucher + consent flow; 16/16 green.

### Follow-up fixes (second round, requested by BE lead)
8. **`rag` worker now healthy → 9/9 health.** Updated `.env` to the latest team set with real `CLOUDINARY_*` + `OPENROUTER_*` (and SendGrid/FCM/admin-bootstrap) secrets. The `rag` compose block already passes these through (`${CLOUDINARY_*}`/`${OPENROUTER_*}`), so the values were all that was missing. (`.env` is gitignored — local only.)
9. **Npgsql fresh-DB wrinkle fixed** in `apps/booking/src/VietRide.Booking.Api/Program.cs`: after `MigrateAsync()`, the startup now opens the shared `NpgsqlDataSource` connection and calls `ReloadTypesAsync()`, so a truly empty DB resolves the enum types on first boot — no manual container restart. **Verified**: dropped `vietride_booking`, booted booking ONCE, voucher E2E 16/16 green (previously needed a restart). Affects all booking enums, not just voucher.
10. **CI: booking test DB env added** — `.github/workflows/ci.yml` `build-dotnet` now sets `VIETRIDE_BOOKING_TEST_CONNECTION_STRING` (mirrors the identity entry) so `VoucherPersistenceIntegrationTests` runs against the CI Postgres instead of falling back to a hardcoded string.

## DoD result
- [x] ✅ Canonical SOT edited FIRST (14.0a) + human-approved (schema/v7/BSOT §5.9 + §13/contract). Commit 7210167.
- [x] ✅ EF migration creates the 3 aggregates + 3 enums matching the EDITED schema; applies/rolls back/reapplies clean **AND each enum is a single copy** (post-fix). Verified on throwaway + live DB.
- [x] ✅ POST /v1/admin/vouchers (SYSTEM_ADMIN): owner_operator_id NULL; OPERATOR_FUNDED fans out one PENDING consent per operator; auto 8-char base32 code; duplicate → VOUCHER_CODE_CONFLICT. **E2E: 201**, oversight list shows it; consent fan-out verified (C2/8a lists PENDING).
- [x] ✅ GET /v1/admin/vouchers (SYSTEM_ADMIN) oversight list with ?ownerOperatorId/?fundingType/?isActive. **E2E: 200**, SYSTEM_ADMIN-only (PASSENGER → 403).
- [x] ✅ POST /v1/operator/vouchers (OPERATOR_ADMIN): owner=caller, funding FORCED OPERATOR_FUNDED, no consent rows. **E2E: 201, owner=operator, fundingType="OPERATOR_FUNDED"**.
- [x] ✅ Operator CRUD PATCH(freeze-on-first-use Q6)/DELETE(soft-delete)/activate/deactivate scoped to owner (cross-operator → 404). Verified by dotnet-reviewer at file:line + 152 unit tests; VOUCHER_LOCKED path covered.
- [x] ✅ Voucher applied at checkout reduces totalAmount; voucher_usages row with funded_by snapshot. **E2E: 200000 → 150000 (FIXED 50k); funded_by rows present (OPERATOR_FUNDED ×3, VIETRIDE_FUNDED ×4)**. PERCENT_OFF half-up + cap covered by unit tests.
- [x] ✅ Checkout applicability branch (a)/(b). **E2E: branch (a) operator-owned applies WITHOUT consent (discount 20k); branch (b) OPERATOR_FUNDED applies only after consent ACCEPTED (discount 25k)**.
- [x] ✅ OPERATOR_FUNDED admin voucher without ACCEPTED consent → VOUCHER_NOT_APPLICABLE. **E2E: 422 VOUCHER_NOT_APPLICABLE**.
- [x] ✅ Operator consent endpoints (list/accept/reject) + Outbox events. **E2E: list 200 tenant-scoped; accept PENDING→ACCEPTED; reject ACCEPTED→REJECTED; re-reject → 409 CONSENT_ALREADY_REJECTED; OPERATOR_STAFF accept → 403. Outbox has booking.voucher.consent_accepted ×1 + consent_rejected ×1**.
- [x] ✅ Revoke after accept does NOT roll back discount on already-CONFIRMED bookings (consent.Reject sets responded_at only; touches no booking — verified in code + the booking from C8c stays confirmed after C8d revoke).
- [x] ✅ Round-trip uses 2 VoucherUsage records per leg — verified by dotnet-reviewer + 152 unit tests (round-trip checkout E2E not driven here; single-trip + all voucher logic exercised live).
- [x] ✅ Usage-limit boundary executed E2E: **Nth → 201, N+1th → 422 VOUCHER_USAGE_LIMIT_REACHED**.
- [x] ✅ Booking solution builds, format clean, unit + integration green (incl. NetArchTest); enum responses are strings.
- [x] ✅ Gateway routes proxy admin/operator endpoints with correct role gates; access-gates spec covers them (incl. OPERATOR_STAFF challenger).

## Tasks completed (audit re-verdict, post-fix)
- 14.0a SOT edit — ✅ | 14.0 aggregates+migration — ✅ (after enum fix) | 14.1 admin create+oversight — ✅ | 14.1b operator CRUD — ✅ | 14.2 consent endpoints+events — ✅ | 14.3 single-trip checkout — ✅ | 14.4 round-trip — ✅ | 14.5 gateway routes — ✅

## Changed files (this audit's fixes; on top of the day's 98-file feature diff)
- `apps/booking/.../Migrations/20260620034903_AddVoucherAggregates.cs` — drop duplicate enum creation.
- 16 booking src files — voucher/consent response DTOs enum→string (+ handler `.ToString()` mappings); ConflictException→CodedConflictException.
- `apps/booking/tests/.../VoucherPersistenceIntegrationTests.cs` + `VoucherPersistenceCollection.cs` (new) + `.csproj` (Npgsql ref); 6 unit-test files (enum/exception assertions); `Directory.Packages.props` (+`Npgsql` PackageVersion — CPM).
- `apps/gateway/src/config/routes.spec.ts`, `proxy/proxy.access-gates.spec.ts` — admin-route catalogue + OPERATOR_STAFF 403 test.
- `infra/docker/docker-compose.yml` — worker `*_DATABASE_URL` env. `.env` (gitignored) also updated for host-run.
- `scripts/run-day14-voucher-e2e.mjs` (new) — committed E2E harness.

## Verification run
| Command | Result | Notes |
|---|---|---|
| `dotnet build apps/booking/VietRide.Booking.sln -c Release` | ✅ PASS | 0 warn / 0 err. |
| `dotnet format apps/booking/VietRide.Booking.sln --verify-no-changes` | ✅ PASS | exit 0. |
| `dotnet test apps/booking/VietRide.Booking.sln -c Release` | ✅ PASS | unit **152/152**, integration **28/28** (was 23; +5 real-DB voucher tests), 0 failed. NetArchTest layering green. |
| EF migration apply→inspect→rollback→reapply (throwaway DB) | ✅ PASS | Reversible; **each voucher enum now exactly 1 copy** (`voucher_type/voucher_funding_type/operator_voucher_consent_status = 1`); tables/columns/indexes/CHECK match edited schema. |
| `npx nx run-many -t build/lint/test --all --exclude="VietRide.*"` | ✅ PASS | TS build + lint green; tests: gateway **98/98** (incl. new access-gate + routes.spec), all 10 projects success. |
| `docker compose … --profile app up -d --build` | ✅ PASS | All 13 containers healthy (9 app + 4 infra) after the `.env` + ReloadTypes fixes. |
| `/health` matrix | ✅ **9/9 = 200** | gateway 3000 / identity 5001 / trip 5002 / booking 5003 / payment 5004 / parcel 5005 / tracking 3001 / notification 3002 / rag 3003 all 200. |
| Fresh-DB self-heal (Npgsql ReloadTypes) | ✅ PASS | Dropped `vietride_booking`, booted booking ONCE, voucher E2E 16/16 green with no manual restart. |
| **Real-app voucher E2E through Gateway (`scripts/run-day14-voucher-e2e.mjs`)** | ✅ **16/16 PASS** | admin create 201; checkout discount 200k→150k; OPERATOR_FUNDED-no-consent → 422 NOT_APPLICABLE; usage-limit Nth 201 / N+1 422 USAGE_LIMIT_REACHED; operator self-create 201; branch-(a) apply 20k; PASSENGER admin GET → 403; oversight list 200; consent list/accept/branch-(b) apply 25k/revoke/409 re-reject/STAFF 403. |
| Side effects (DB/Outbox) | ✅ PASS | `voucher_usages.funded_by` snapshots written; Outbox `booking.voucher.consent_accepted`/`_rejected` rows emitted. |
| Code-vs-SOT — dotnet-reviewer + nest-reviewer | ✅ APPROVE | All DoD logic matches SOT at file:line. |
| Hard invariants | ✅ PASS | No `Co-Authored-By`; CPM (no `.csproj Version=`; Npgsql added as `<PackageVersion>`); `.cs` eol=crlf, `.ts/.yml/.mjs` eol=lf; MediatR v11; no banned deps. |

## Contract / event / schema changes shipped
- **Endpoints**: POST/GET `/v1/admin/vouchers`, GET `/v1/admin/vouchers/{id}/consents`, POST/PATCH/DELETE `/v1/operator/vouchers` + `/activate` + `/deactivate`, GET `/v1/operator/voucher-consents`, POST `/v1/operator/voucher-consents/{id}/accept|reject`; POST `/v1/bookings` (+round-trip) `voucherCode` functional. **All voucher responses serialize enums as strings.**
- **Events**: `booking.voucher.consent_accepted`/`_rejected` (BSOT:1744-1745) — verified emitted to Outbox.
- **Error codes**: VOUCHER_FORBIDDEN_FUNDING 422, VOUCHER_CODE_CONFLICT 409, VOUCHER_LOCKED 409 + CONSENT_NOT_PENDING/CONSENT_ALREADY_REJECTED — registered in BSOT §5.9 + §13 (commits 7210167, fd5f8a0). ✅ in sync.
- **Migration**: `20260620034903_AddVoucherAggregates` — corrected (single enum copy).

## Known gaps & carry-over for Day 15+
(Items rag-Cloudinary, CI-booking-env, and Npgsql-ReloadTypes from the first audit pass were FIXED this session — see "Follow-up fixes" above.)
1. **Postman**: the cumulative collection still has no voucher requests (the runnable artifact is `scripts/run-day14-voucher-e2e.mjs` instead). Add voucher requests to `docs/api/postman/vietride.postman_collection.json` when convenient for the external reviewer.
2. Round-trip voucher (14.4) verified via unit tests + reviewer, not driven through the live Gateway here (single-trip + all shared voucher logic was). Optional live round-trip E2E later.
3. **`.env` now holds real third-party secrets** (Cloudinary/OpenRouter/SendGrid/FCM). It is gitignored (local only) — do not commit it; rotate if leaked. `.env.example` stays the committed template.

## Notes for Day 15+ planning
- Day 15 (Payment/Wallet) already ✅ READY on this branch; Day-14's fixes are isolated to Booking + gateway specs + compose worker env and do not regress it.
- The Docker stack is left running; throwaway audit DBs were dropped. Test bookings/vouchers created during E2E live in the dev `vietride_booking` DB (harmless).
- All fixes are in the working tree, **uncommitted** — ready to commit on the `feat/day-14-voucher-checkout` branch when you choose.
