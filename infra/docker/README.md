# VietRide — Docker Compose stack

A single compose file lives in this directory:

| File | Purpose |
| --- | --- |
| `docker-compose.yml` | Core stack: Postgres + PgBouncer + Redis + RabbitMQ + 5 .NET services + Gateway + 3 NestJS workers |

Observability for v1 is **Sentry (DSN env) + UptimeRobot + Serilog/Winston stdout** per
[BACKEND_SOURCE_OF_TRUTH §9.13](../../BACKEND_SOURCE_OF_TRUTH.md). Prometheus/Grafana/Tempo/Loki/OpenTelemetry are deferred to v2.

## Prerequisites

- Docker Desktop 4.x (or Docker Engine 25+ with Compose v2)
- `.env` file at the repo root — copy `.env.example` and fill in `INTERNAL_JWT_SECRET`, `POSTGRES_PASSWORD`, etc.

## Quick start

### Infra-only (DB + cache + broker, no apps)

```bash
cd infra/docker
docker compose up -d postgres pgbouncer redis rabbitmq
```

### Full app stack

```bash
cd infra/docker
docker compose up -d
```

### Tear everything down

```bash
docker compose down
# Add --volumes to also wipe Postgres/Redis/RabbitMQ data.
```

## Service URLs (host)

| Service | URL |
| --- | --- |
| Gateway | http://localhost:3000 |
| Identity | http://localhost:5001 |
| Trip | http://localhost:5002 |
| Booking | http://localhost:5003 |
| Payment | http://localhost:5004 |
| Parcel | http://localhost:5005 |
| Tracking (Nest) | http://localhost:3001 |
| Notification (Nest) | http://localhost:3002 |
| RAG (Nest) | http://localhost:3003 |
| RabbitMQ mgmt | http://localhost:15672 (`vietride` / `vietride_dev` by default) |
| Postgres | localhost:5432 (direct) / localhost:6432 (PgBouncer) |
| Redis | localhost:6379 |

## Health checks

Every container declares a `healthcheck`. Quick status:

```bash
docker compose ps
# Look for "(healthy)" beside each container.
```

The .NET services probe `GET /health`; NestJS services and the Gateway expose the same path. Postgres uses `pg_isready`, Redis uses `redis-cli ping`, RabbitMQ uses `rabbitmq-diagnostics ping`.

## Validating compose file without starting containers

```bash
docker compose -f docker-compose.yml config --quiet
```

Should exit with code 0.
