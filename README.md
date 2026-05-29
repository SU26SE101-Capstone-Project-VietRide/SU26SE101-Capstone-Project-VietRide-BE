# VietRide Backend

Capstone SU26SE101 — backend monorepo. 5 .NET 8 services + 4 NestJS services + 1 NestJS API Gateway, managed by Nx with `@nx-dotnet/core`.

## Prerequisites

- Node.js 20 LTS or newer (verified with 24.x)
- npm 10+
- .NET 8 SDK (8.0.421+). Pinned via [global.json](global.json).
- Docker + Docker Compose v2
- Git

## This repo is an Nx monorepo — read before you start

- **One install, at the root.** Run `npm install` ONCE in the repo root. There is **no
  `package.json` per app** — all JS deps live at the root and the shared TS libs are wired as
  workspace symlinks (`@vietride/*`). **Do NOT** `cd apps/<svc> && npm install` — it will fail.
- **Run everything from the repo root.** Nx commands take a *project name*, not a path:
  `npx nx run gateway:serve` (✅), not `cd apps/gateway && ...` (❌). `npx nx show projects` lists them.
- **`.env` is mandatory and gitignored.** It is NOT in the repo — you must create it
  (`cp .env.example .env`). Without it every service aborts on boot (zod env validation calls
  `process.exit(1)` with `❌ Invalid env vars`; .NET hosts fail on missing `INTERNAL_JWT_SECRET`).
- **.NET** services run via Visual Studio (open the `.sln`, F5) or `dotnet run`.
  **NestJS** services run via `npx nx run <svc>:serve` (gateway/tracking/notification/rag) — not VS.

## Quick start (Day 2 ready)

```powershell
# 1. Install JS deps (ONCE, at repo root — never per app)
npm install

# 2. Copy env template — MANDATORY, nothing boots without it (.env is gitignored)
cp .env.example .env
# INTERNAL_JWT_SECRET has a working dev default; edit only if needed.

# 3. Bring up infra (Postgres + PgBouncer + Redis + RabbitMQ) via the `infra` profile.
#    Run from infra/docker; .env lives at repo root so pass --env-file.
cd infra/docker
docker compose --env-file ../../.env --profile infra up -d

# 4a. Full stack in Docker (production-like) — enable BOTH profiles (app depends on infra):
docker compose --env-file ../../.env --profile infra --profile app up -d

# 4b. OR keep only infra in Docker and run services locally for dev (hot reload):
cd ../..
# Terminal 1 (Identity, port 5001):
dotnet run --project apps/identity/src/VietRide.Identity.Api
# ... repeat for trip:5002, booking:5003, payment:5004, parcel:5005
# Terminal 6 (Gateway, port 3000):
npx nx run gateway:serve

# 5. Smoke test
curl http://localhost:3000/health                 # Gateway → {"status":"ok","service":"Gateway"}
curl http://localhost:3000/v1/identity/health     # Gateway → Identity proxy roundtrip
curl http://localhost:5001/health                 # Identity direct
curl http://localhost:5001/swagger                # Swagger UI lives on each .NET service (NOT the gateway)
curl http://localhost:3000/v1/trip/health         # Gateway → Trip
# ... booking, payment, parcel
```

> **Compose profiles:** services are split into `infra` (postgres/pgbouncer/redis/rabbitmq) and
> `app` (the 9 service containers). A bare `docker compose up -d` with no `--profile` starts
> **nothing** — always pass a profile. `docker compose --profile app down` stops just the app tier.

## Day-to-day commands

```bash
npx nx run-many -t build           # build all 49 projects (.NET + NestJS)
npx nx run-many -t test            # run xunit + jest
npx nx affected -t build           # build only what changed vs main (CI fast path)
npx nx run VietRide.Identity.Api:serve
npx nx run gateway:serve --watch
npx nx graph                        # visualize project DAG
```

## EF Core migrations

Each .NET service ships an `IDesignTimeDbContextFactory<TDbContext>` under
`apps/<svc>/src/VietRide.<Svc>.Infrastructure/Design/` so `dotnet ef` works
WITHOUT booting the full host (which would require `INTERNAL_JWT_SECRET`).

```bash
# Add a migration for one service (run from repo root):
dotnet ef migrations add InitialCreate \
  -p apps/identity/src/VietRide.Identity.Infrastructure \
  -s apps/identity/src/VietRide.Identity.Api \
  -o Migrations

# Apply migrations (requires Postgres running):
dotnet ef database update \
  -p apps/identity/src/VietRide.Identity.Infrastructure \
  -s apps/identity/src/VietRide.Identity.Api
```

Override the design-time connection string per service via env var
(`IDENTITY_DESIGN_CONNECTION`, `TRIP_DESIGN_CONNECTION`, …). Default targets
`localhost:5432` with creds from `.env.example`.

## Layout

```
apps/                       Deployable services
├── gateway/                NestJS — JWT validate + Internal JWT sign + proxy
├── identity/               .NET 8 — Auth, RBAC, Operator, Subscription
├── trip/                   .NET 8 — Station/Route/Trip/Vehicle/Schedule
├── booking/                .NET 8 — Booking, Passenger, Voucher
├── payment/                .NET 8 — Wallet, VNPay, Settlement
├── parcel/                 .NET 8 — Parcel lifecycle
├── tracking/               NestJS — Socket.IO GPS + ETA
├── notification/           NestJS — FCM push + email + RabbitMQ consume
└── rag/                    NestJS — LLM SSE + pgvector

tests/
├── e2e/                    Cross-service e2e (Playwright/Supertest spanning multiple services)
├── load/                   k6 / Artillery load test scripts
├── gateway-e2e/            Per-app HTTP e2e (Jest + axios, Nx supertest pattern)
├── tracking-e2e/
├── notification-e2e/
└── rag-e2e/

libs/
├── dotnet/                 6 .NET shared libs (Kernel, Application, Persistence, Messaging, Http, Web)
└── shared/                 6 TS libs (contracts, nest-common, nest-config, nest-persistence, nest-rabbitmq, nest-redis)

infra/                      docker-compose + nginx + pgbouncer + rabbitmq config
db-schema/                  Canonical DDL per service (existing, do not move)
docs/                       ALL developer + generated docs — ADR, runbook, dev guide
├── adr/                    Architecture Decision Records
├── api/openapi/            Auto-generated OpenAPI 3 spec per service (tool output)
├── deliverables/           Capstone submission artifacts
└── runbooks/               On-call / deployment runbooks
scripts/                    One-off ops scripts
tests/                      Cross-service e2e + load tests
```

## Source of truth

Read in priority order — when conflict, top wins:

1. [BACKEND_SOURCE_OF_TRUTH.md](BACKEND_SOURCE_OF_TRUTH.md) — DB / event / API conventions
2. [VietRide_API_Contract_v1.md](VietRide_API_Contract_v1.md) — REST endpoint contracts
3. [SU26SE101_VIETRIDE_technical_context_v7.md](SU26SE101_VIETRIDE_technical_context_v7.md) — business rules
4. [BE_TIMELINE_VU.md](BE_TIMELINE_VU.md) — daily plan (Vũ scope)
5. [db-schema/](db-schema/) — canonical DDL

## Nx cheatsheet

```bash
npx nx graph                            # browse project DAG
npx nx affected -t build                # build only what changed since main
npx nx run-many -t test                 # run all tests
npx nx show project identity            # inspect a project's targets
npx nx g @nx/nest:lib --directory=libs/shared/foo --name=foo  # add lib
```

For .NET-specific work, `dotnet build`/`dotnet test` per .sln remain the canonical commands. Nx wraps them for cache + affected.
