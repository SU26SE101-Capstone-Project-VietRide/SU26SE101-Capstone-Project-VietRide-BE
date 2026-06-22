---
name: smoke-test
description: Bring up the VietRide local stack (Postgres/Redis/RabbitMQ + services) and verify health across the Gateway and each .NET/NestJS service via the /health roundtrip matrix. Use to confirm the stack boots and routing works before/after a change.
---

# Smoke-test the VietRide stack

## Infra first
```bash
cd infra/docker
docker compose up -d postgres redis rabbitmq pgbouncer
```

## Run apps (pick one)
- **Docker (production-like):** `docker compose up -d` (all 9 app containers).
- **Local dev (hot reload):** run each .NET service + the Gateway:
  ```bash
  dotnet run --project apps/identity/src/VietRide.Identity.Api      # :5001
  # trip :5002, booking :5003, payment :5004, parcel :5005
  npx nx run gateway:serve                                          # :3000
  ```
  Local .NET hosts need `INTERNAL_JWT_SECRET` set (see `.env.example`).

## Health matrix (expect HTTP 200)
```bash
curl http://localhost:3000/health                 # Gateway -> {"status":"ok"}
curl http://localhost:3000/v1/identity/health     # Gateway -> Identity (proxy roundtrip)
curl http://localhost:3000/v1/trip/health
curl http://localhost:3000/v1/booking/health
curl http://localhost:3000/v1/payment/health
curl http://localhost:3000/v1/parcel/health
curl http://localhost:5001/health                 # Identity direct (bypass gateway)
```
Ports: gateway 3000, identity 5001, trip 5002, booking 5003, payment 5004, parcel 5005, tracking 3001, notification 3002, rag 3003.

## Checks
- Every `/v1/<svc>/health` returns 200 (proves Gateway route + Internal JWT mint + downstream up).
- Tamper the Internal JWT → downstream returns 401 (auth handler works).
- RabbitMQ mgmt UI reachable at http://localhost:15672; exchange `vietride.events` exists.
- On failure, check container logs: `docker compose logs <service> --tail=50`.

> The built-in `/run` and `/verify` skills can also drive the app; use this skill when you specifically want the full multi-service health matrix.
