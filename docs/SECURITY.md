# VietRide Backend — Security Notes

> Capstone-scope guidance for handling secrets, dev defaults, and production hardening.
> For incidents or production deployment, see [runbooks/](runbooks/).

## 1. Dev defaults that ship in the repo

These values are **committed intentionally** so a fresh `git clone` boots without setup, but they are **not safe for production**. Every value below must be replaced before any deploy that's reachable from the public internet.

| Where | Value | What it is |
|---|---|---|
| `apps/*/src/*.Api/Properties/launchSettings.json` | `INTERNAL_JWT_SECRET = "dev-secret-please-change-min-32-chars-aaaaaaaaaaaaaaaa"` | Shared HMAC key for Internal JWT (Gateway ↔ .NET services). Dev only. |
| `apps/*/src/*.Api/appsettings.json` | `Password=vietride_dev` in connection string | PostgreSQL password matching `docker compose` default. |
| `.env.example` | All `*_API_KEY`, `*_SECRET`, `*_HASH_SECRET` placeholders | Template — copy to `.env` (gitignored) and fill real values. |
| `infra/docker/docker-compose.yml` | `POSTGRES_PASSWORD: vietride_dev`, `RABBITMQ_DEFAULT_PASS: vietride_dev` | Local stack only. |
| `.env.example` / deployment env | `SYSTEM_ADMIN_BOOTSTRAP_EMAIL`, `SYSTEM_ADMIN_BOOTSTRAP_PASSWORD`, optional `SYSTEM_ADMIN_BOOTSTRAP_DISPLAY_NAME` | Identity startup seeder uses these once to create the first `SYSTEM_ADMIN`. They are placeholders/templates only; no admin password/hash is stored in `seed.sql` or EF seed migrations. |

## 2. Pre-deploy checklist

Before any environment that's not your laptop:

- [ ] Generate a fresh `INTERNAL_JWT_SECRET` ≥ 32 random bytes:
  ```bash
  openssl rand -hex 32
  ```
  Set it identically on Gateway + all 5 .NET services. Mismatch → all `X-Internal-Auth` headers fail with 401.
- [ ] Rotate Postgres password; update `ConnectionStrings__Default` env var (don't edit `appsettings.json` in the image).
- [ ] Rotate RabbitMQ password; update `RABBITMQ_PASSWORD`.
- [ ] Generate Identity's RS256 keypair (Day 3+ when JWKS endpoint exists). Public key auto-published at `/v1/.well-known/jwks.json`; private key as env var on Identity only.
- [ ] Set a one-time bootstrap admin secret before first Identity startup: `SYSTEM_ADMIN_BOOTSTRAP_EMAIL`, a strong `SYSTEM_ADMIN_BOOTSTRAP_PASSWORD`, and optional `SYSTEM_ADMIN_BOOTSTRAP_DISPLAY_NAME`.
- [ ] After first login, change the bootstrap admin password immediately; the startup seeder is idempotent and does not update an existing `SYSTEM_ADMIN`.
- [ ] Set real `VNPAY_HASH_SECRET`, `SENDGRID_API_KEY`, `FIREBASE_PRIVATE_KEY`, `GOOGLE_MAPS_API_KEY`, `ANTHROPIC_API_KEY` per `.env.example`.
- [ ] Confirm no `.env` accidentally baked into Docker image (`.dockerignore` should block it; verify with `docker run --rm <image> sh -c 'ls -la /.env* /app/.env*'`).
- [ ] Confirm `ASPNETCORE_ENVIRONMENT=Production` (disables Swagger UI by default).

## 3. Secret-handling rules

1. **Never commit a real production secret.** Use `.env` (gitignored), platform secret manager (AWS Secrets Manager / GCP Secret Manager / Vault), or `dotnet user-secrets` locally.
2. **Don't echo secrets in logs.** Serilog config in `appsettings.json` already excludes the env var bag; if you add new structured properties, exclude anything matching `*Secret`, `*Password`, `*Token`.
3. **JWT secret rotation:** Internal JWT uses HS256 (symmetric) — rotation requires synchronous redeploy of Gateway + all 5 .NET services. There's no key-id; capstone v1 ships with single-key rotation. See [Appendix A.5 of BACKEND_SOURCE_OF_TRUTH.md](../BACKEND_SOURCE_OF_TRUTH.md) for procedure.
4. **User Access Token (RS256):** Identity holds the private key, all other services verify via JWKS public endpoint. Key rotation = add new key to JWKS, wait for cache TTL (1h), retire old key.

## 4. Reporting

Capstone scope — no formal CVD program. If you find an issue during the SU26SE101 cycle, file it on the team's internal channel + tag the BE lead. For post-capstone (production deploy), set up a `SECURITY.md` policy and a `security@vietride.app` mailbox.

## 5. Related docs

- [BACKEND_SOURCE_OF_TRUTH.md §6](../BACKEND_SOURCE_OF_TRUTH.md) — Auth/JWT canonical
- [Appendix A.5](../BACKEND_SOURCE_OF_TRUTH.md) — Internal JWT rotation procedure
- [runbooks/](runbooks/) — Operational runbooks (incident response, key rotation)
