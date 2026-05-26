#!/usr/bin/env bash
# Tear down + rebuild local dev stack. Placeholder until app containers wired Day 2.
set -euo pipefail

ROOT=$(git rev-parse --show-toplevel)
cd "$ROOT/infra/docker"

echo "→ Bringing down stack (preserving volumes)"
docker compose down

echo "→ Bringing up postgres + redis + rabbitmq + pgbouncer"
docker compose up -d

echo "→ Stack up. Postgres on \${POSTGRES_PORT:-5432}, Redis \${REDIS_PORT:-6379}, RabbitMQ mgmt \${RABBITMQ_MGMT_PORT:-15672}"
