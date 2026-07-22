# Day 30 — Final checklist

> Produced by `/audit-day 30` after all Day-30 tasks were committed and the full verification
> matrix was rerun on 2026-07-22. Task tracker approvals and earlier evidence were not treated as
> audit proof.

- **Timeline ref**: `BE_TIMELINE_VU.md` → Day 30 — Sprint 4 demo prep (no Jira key listed)
- **Plan**: `docs/handoff/day-30-plan.md`
- **Status**: ✅ READY

## DoD result

- [x] ✅ The cumulative Postman collection has exactly one importable
  `Day 30 - Sprint 4 demo` folder with six ordered Gateway requests. Audit validation confirmed
  the expected routes, bearer-role placeholders, five `{{$guid}}` idempotency keys, executable
  response assertions, and empty environment placeholders.
- [x] ✅ The exact live wrapper executed `npm run e2e:day30` and exited `0`. The normal child
  proved the lifecycle Trip was generated from the operator-created DriverSchedule with
  `source=AUTO_FROM_SCHEDULE` and a matching `driver_schedule_id`.
- [x] ✅ The same Trip reached `SCHEDULED → BOARDING → IN_PROGRESS → COMPLETED`; its Parcel
  reached `PENDING → LOADED → IN_TRANSIT → UNLOADED`. Each required Outbox routing key appeared
  exactly once, completion replay was byte-identical, and duplicate transition/Outbox counts were
  zero.
- [x] ✅ The runner used generated IDs, short-lived JWTs, runtime UUID-v4 keys, and the disclosed
  fixture-only departure-time advance. The failure-injection and normal paths both verified
  cleanup residue `0`; the wrapper leak scan passed.
- [x] ✅ `docs/handoff/sprint-4-demo-script.md` contains the exact reviewer command, expected
  evidence, ordered narration, fixture/security boundary, and Sprint-5 section. Its
  `No known spillover` branch matches the current `Final result: PASS` evidence.

## Tasks completed

- Task 30.1 — Add the Sprint-4 demo flow to the cumulative Postman collection — ✅
- Task 30.2 — Build and run the self-cleaning Sprint-4 demo harness — ✅
- Task 30.3 — Publish the Sprint-4 demo script and Sprint-5 spillover handoff — ✅

## Changed files

- `docs/api/postman/vietride.postman_collection.json` — Day-30 six-request manual companion
- `docs/api/postman/vietride.local.postman_environment.json` — empty Day-30 placeholders
- `docs/api/postman/README.md` — Day-30 collection and authoritative runner guidance
- `scripts/run-day30-sprint4-demo.mjs` — isolated self-seeding live demo runner
- `scripts/run-day30-sprint4-demo.test.mjs` — runner contract test
- `scripts/run-day30-sprint4-demo-live-wrapper.mjs` — failure/normal live verification wrapper
- `package.json` — `e2e:day30` command
- `docs/handoff/day-30-sprint4-evidence.md` — redacted live evidence for both paths
- `docs/handoff/sprint-4-demo-script.md` — reviewer demo and Sprint-5 handoff
- `docs/handoff/day-30-plan.md` — approved plan, tracker, and Windows wrapper amendment

## Verification run

| Command | Result | Notes |
|---|---|---|
| `node --check scripts/run-day30-sprint4-demo.mjs` | PASS | Syntax valid. |
| `node --test --test-reporter=tap scripts/run-day30-sprint4-demo.test.mjs` | PASS | 1/1 passed; 0 failed/skipped. |
| Day-30 Postman structural validator | PASS | 6/6 ordered routes, roles, idempotency headers, response assertions, and empty placeholders. |
| Evidence/handoff semantic validator | PASS | Both summaries, both markers, `Final result: PASS`, reviewer command, and no-spillover branch agree. |
| `npx prettier --check ...Day-30 files...` | PASS | All matched files use Prettier style. |
| `git diff --check` | PASS | No whitespace errors. |
| `dotnet build libs/dotnet/VietRide.Libs.sln -c Release` | PASS | 0 warnings, 0 errors. |
| `dotnet build apps/{identity,trip,booking,payment,parcel}/VietRide.<Svc>.sln -c Release` | PASS | All five service solutions: 0 warnings, 0 errors. |
| `dotnet format <all six solutions> --verify-no-changes` | PASS | All six commands exited `0`. |
| `dotnet test libs/dotnet/VietRide.Libs.sln -c Release` | PASS | Messaging 23, Persistence 32, Reporting 11, Web 98; 164/164 total. |
| `dotnet test apps/identity/VietRide.Identity.sln -c Release` | PASS | Unit 304/304; integration 155/155. |
| `dotnet test apps/trip/VietRide.Trip.sln -c Release` | PASS | Unit 535/535; integration 206/206. |
| `dotnet test apps/booking/VietRide.Booking.sln -c Release` | PASS | Unit 473/473; integration 161/161. |
| `dotnet test apps/payment/VietRide.Payment.sln -c Release` | PASS | Unit 111/111; integration 36/36. |
| `dotnet test apps/parcel/VietRide.Parcel.sln -c Release` | PASS | Unit 193/193; integration 44/44. |
| `.NET DB-pool execution boundary` | PASS | Service app containers were stopped while DB-backed solution tests ran sequentially; this released Postgres pools and avoided cross-solution fixture contention. They were restarted before runtime checks. |
| `npx nx run-many -t build --all --exclude="VietRide.*"` | PASS | 10 TS projects completed; known third-party/source-map warnings remained non-fatal. |
| `npx nx run-many -t lint --all --exclude="VietRide.*"` | PASS | 10 TS projects completed. |
| `npx nx run-many -t test --all --exclude="VietRide.*" --ci --passWithNoTests --skip-nx-cache` | PASS | 30/30 suites; 163/163 tests. |
| EF migration apply/down/re-apply | SKIP | Day 30 changed no migration, schema, or migration history. |
| `node scripts/run-day30-sprint4-demo-live-wrapper.mjs` | PASS | Exit `0` after 1,607.3 s; failure-injection and normal-e2e redacted summaries both PASS. Timeout allowed both real `*/15` auto-boarding boundaries. |
| Failure-injection E2E through Gateway `:3000` | PASS | Generated schedule/Trip, full lifecycle and Outbox evidence, expected injected failure, cleanup residue `0`, no credential/key leak. |
| Normal Day-30 E2E through Gateway `:3000` | PASS | Schedule `201`; lifecycle/replay actions `200`; generated Trip proof, all states/events, zero duplicates, cleanup residue `0`. |
| `docker compose --env-file .env -f infra/docker/docker-compose.yml --profile app up -d --build --wait --wait-timeout 180` | PASS | Rebuilt the post-merge stack; 9 apps plus Postgres, Redis, RabbitMQ, and PgBouncer healthy. |
| `/health` matrix | PASS | Gateway `3000`, Identity `5001`, Trip `5002`, Booking `5003`, Payment `5004`, Parcel `5005`, Tracking `3001`, Notification `3002`, RAG `3003`: 9/9 HTTP `200`. |
| Day-30 Review/demo bullet overall | PASS | Importable artifact and executed real-stack schedule → generated Trip → parcel lifecycle demo both passed. |
| CPM/banned dependencies/MediatR/no-coauthor/EOL scan | PASS | No `.csproj` package version, banned dependency, MediatR v12, `Co-Authored-By` trailer in Day-30 commits, or tracked EOL violation. |

## Contract / event / schema changes shipped

None. Day 30 only exercises existing endpoints, routing keys, errors, and schemas. No migration,
new event/error/convention, BSOT registry entry, or BSOT changelog update was required.

After merging `origin/main`, the demo fixture was aligned with the already-migrated Identity
schema by seeding `operator_subscriptions.active_plan_id` instead of the removed `plan_id` column.
This is a runner compatibility fix, not a product schema change.

## Known gaps & carry-over for Day 31

- None. Both Day-30 live paths report cleanup residue `0`, and the Sprint-4 demo handoff correctly
  records `No known spillover`.

## Notes for Day 31 planning

- Full DB-backed `.NET` regression should continue to run service solutions sequentially while
  the nine app containers are stopped; parallel solution runs can exhaust or contend for local
  Postgres fixtures and produce false connection/database-exists failures.
- The Day-30 wrapper legitimately spans two production `*/15` scheduler boundaries. Reviewers
  should allow at least 45 minutes for the exact wrapper command rather than treating buffered
  output as a hang.
