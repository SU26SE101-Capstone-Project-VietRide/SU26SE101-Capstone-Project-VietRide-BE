# VietRide — Production Deploy Guide (single Ubuntu 24.04 server, CLI-only)

CD model: **build images in CI → push to GHCR → manually deploy** (`Deploy (production)`
workflow → Actions tab → Run workflow → pick a tag). The server runs `docker-compose.prod.yml`,
which **pulls** images from GHCR — it never builds.

```
 push tag v1.0.0 ─> docker-build.yml ─> GHCR images ─> (manual) deploy.yml ─> SSH ─> server: compose pull + up
```

### Networking model — IMPORTANT

This server sits behind the provider's **NAT** and only the port range **24450–24460** can be
mapped inbound. Cloudflare's normal DNS proxy can't reach those ports, so we use a
**Cloudflare Tunnel** (`cloudflared`) instead:

```
User → https://api.vietride.app   (Cloudflare edge, clean :443, free TLS)
              ↕  encrypted tunnel — the SERVER dials OUT (no inbound port needed at all)
        cloudflared (container on the server)
              ↓  docker network
        nginx:80 → gateway → .NET/Nest services
```

Consequences:
- **No inbound port forwarding is required** — not even 24450. The tunnel is outbound-only.
- **No Let's Encrypt / certbot** — Cloudflare provides the public TLS cert.
- nginx listens on plain HTTP inside docker and is **not published** to the host.

---

## Part A — One-time server setup (run on the Ubuntu box)

### 1. Install Docker Engine + Compose
```bash
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker $USER   # log out / back in afterwards
docker compose version
```

### 2. Firewall — only outbound matters
The tunnel dials out on 443, so you do **not** need to open any inbound app port. Keep SSH open:
```bash
sudo ufw allow OpenSSH
sudo ufw enable
```
(Outbound HTTPS is allowed by default — that's all cloudflared needs.)

### 3. Create the deploy directory + a dedicated SSH key for CI
```bash
sudo mkdir -p /opt/vietride
sudo chown $USER:$USER /opt/vietride

ssh-keygen -t ed25519 -f vietride_deploy -C "github-actions" -N ""
cat vietride_deploy.pub >> ~/.ssh/authorized_keys
# The PRIVATE key (vietride_deploy) → GitHub secret DEPLOY_SSH_KEY (Part C).
```

### 4. Create the Cloudflare Tunnel (gives you TUNNEL_TOKEN)
In the **Cloudflare Zero Trust dashboard** (free): Networks → Tunnels → Create a tunnel →
*Cloudflared* → name it `vietride`. Then:
- Copy the **tunnel token** (a long string). It goes into the server `.env` as `TUNNEL_TOKEN`.
- Under **Public Hostnames**, add:
  | Subdomain | Domain | Service |
  | --- | --- | --- |
  | `api` | `vietride.app` | `http://nginx:80` |

  > `nginx` is the docker service name — cloudflared resolves it on the shared `vietride_net`
  > network because both run in this compose project.

This auto-creates the DNS record for `api.vietride.app` and binds 443 → your tunnel → nginx.

### 5. Create the production `.env` (real secrets — never commit)
```bash
mkdir -p /opt/vietride/infra/docker
nano /opt/vietride/infra/docker/.env
```
Start from [.env.example](../../.env.example) and set real values. **Production must add:**
```ini
# Image source — owner is the lowercased GitHub org
IMAGE_PREFIX=ghcr.io/su26se101-capstone-project-vietride/vietride
IMAGE_TAG=v1.0.0

# Cloudflare Tunnel token from step 4
TUNNEL_TOKEN=<long-token-from-cloudflare>

# Strong, unique secrets (do NOT reuse the _dev defaults)
POSTGRES_PASSWORD=<strong-random>
RABBITMQ_PASSWORD=<strong-random>
INTERNAL_JWT_SECRET=<openssl rand -hex 32>
SYSTEM_ADMIN_BOOTSTRAP_EMAIL=...
SYSTEM_ADMIN_BOOTSTRAP_PASSWORD=...
GOOGLE_OAUTH_CLIENT_ID=...
GOOGLE_OAUTH_CLIENT_SECRET=...
VNPAY_TMN_CODE=...
VNPAY_HASH_SECRET=...
VNPAY_PAYMENT_URL=https://sandbox.vnpayment.vn/paymentv2/vpcpay.html
VNPAY_RETURN_URL=https://api.vietride.app/payments/vnpay-return
```

> The single-port FE/BE split is done **inside nginx by path** (`/` → FE, `/v1/` → gateway),
> not by exposing ports — so the actual server port number never matters here. The tunnel
> connects straight to `nginx:80`.

---

## Part B — Cloudflare TLS mode
In the Cloudflare dashboard → SSL/TLS → set mode to **Full**. (The tunnel is already encrypted
end-to-end, so origin certs are unnecessary; Full just keeps the edge strict.)

---

## Part C — GitHub repository secrets (Settings → Secrets and variables → Actions)
Create an **Environment** named `production` and add:

| Secret | Value |
| --- | --- |
| `DEPLOY_HOST` | Server public IP or SSH hostname |
| `DEPLOY_USER` | SSH user (whose authorized_keys you appended to) |
| `DEPLOY_SSH_KEY` | Contents of the **private** key `vietride_deploy` |
| `DEPLOY_PORT` | SSH port (optional, default 22) |
| `APP_DOMAIN` | `api.vietride.app` (used by the post-deploy health check via Cloudflare) |
| `GHCR_USERNAME` | Your GitHub username |
| `GHCR_TOKEN` | PAT (classic) with `read:packages` — or make the GHCR packages public |

---

## Part D — Release flow (every deploy)
1. **Build images**: push a version tag → triggers `docker-build.yml`.
   ```bash
   git tag v1.0.0 && git push origin v1.0.0
   ```
2. Wait for "Docker Build & Push" to go green (images now in GHCR).
3. **Deploy**: Actions → **Deploy (production)** → Run workflow → enter `v1.0.0`.
4. The workflow copies compose files, pulls images, restarts the stack, then health-checks
   `https://api.vietride.app/health` (through Cloudflare).

### First-ever boot (manual sanity check on the server)
```bash
cd /opt/vietride/infra/docker
docker compose -f docker-compose.prod.yml --env-file .env pull
docker compose -f docker-compose.prod.yml --env-file .env up -d
docker compose -f docker-compose.prod.yml ps      # all (healthy)?
docker compose -f docker-compose.prod.yml logs -f cloudflared   # "Registered tunnel connection"
```

### Rollback
Re-run **Deploy (production)** with a previous good tag (e.g. `v0.9.0`). Compose pulls the old
images and restarts — no rebuild.

---

## Notes
- `tracking`, `notification`, `rag` (NestJS) are **not deployed yet** — their images aren't built
  by `docker-build.yml`. When ready: add them to that workflow's matrix, uncomment their blocks in
  `docker-compose.prod.yml`, uncomment the gateway `*_BASE_URL` lines, and the `/tracking/` route
  in `nginx.prod.conf`.
- Postgres/Redis/RabbitMQ have **no published host ports** — reach them via
  `docker compose exec postgres psql ...` when needed.
- Frontend: when the FE is ready, either add an `frontend` container to the compose and switch the
  nginx `location /` to `proxy_pass http://frontend;`, or mount its static build into nginx.
