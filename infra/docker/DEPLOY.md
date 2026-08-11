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
VNPAY_BASE_URL=https://sandbox.vnpayment.vn/paymentv2/vpcpay.html
VNPAY_WEB_RETURN_URL=https://app.vietride.online/payments/return
VNPAY_MOBILE_SDK_RETURN_URL=https://api.vietride.online/v1/payments/vnpay-mobile-sdk-return
VNPAY_SDK_SCHEME=vietride
VNPAY_IS_SANDBOX=false
VNPAY_WEB_ENABLED=false
VNPAY_MOBILE_SDK_ENABLED=false
VNPAY_IPN_URL=https://api.vietride.online/v1/payments/vnpay-ipn
VNPAY_PAYMENT_TIMEOUT_MINUTES=15

# Hạ tầng deep-link Android dùng chung, không còn tham gia VNPay return.
ANDROID_PACKAGE=com.vietride.passenger
DEEPLINK_APP_SCHEME=vietride
DEEPLINK_ANDROID_SHA256_FINGERPRINTS=<release-fingerprints>

# Tracking Phase 13 — capability link chia sẻ Main Trip
TRACKING_SHARE_TOKEN_SECRET=<openssl rand -hex 32>
TRACKING_SHARE_PAGE_URL=https://app.vietride.online/trip-sharing
TRACKING_SHARE_TOKEN_TTL_SECONDS=86400
TRACKING_SHARE_CONTEXT_RATE_LIMIT_PER_MIN=60
TRACKING_SHARE_SOCKET_RATE_LIMIT_PER_MIN=20
TRACKING_SHARE_SOCKET_REVALIDATE_SECONDS=60

# Tracking and Notification Socket.IO — explicit browser origins only.
# Both production frontend origins are allowed; wildcard '*' is forbidden.
TRACKING_CORS_ORIGIN=https://app.vietride.online,https://vietride.online
NOTIFICATION_CORS_ORIGIN=https://app.vietride.online,https://vietride.online
```

`VNPAY_PAYMENT_TIMEOUT_MINUTES` is the legacy null-`DueAt` fallback. Persisted payment deadlines
remain authoritative, so this setting does not extend Booking's 10-minute Trip seat lock.

Hai cờ `VNPAY_WEB_ENABLED` và `VNPAY_MOBILE_SDK_ENABLED` mặc định `false` để triển khai an toàn.
Chỉ bật từng kênh sau khi URL tương ứng, merchant VNPay và client của kênh đó đã được kiểm thử.
`/payments/return` thuộc frontend Web; callback kỹ thuật Mobile SDK đi thẳng vào Payment tại
`/v1/payments/vnpay-mobile-sdk-return`. Cả hai đường return chỉ xác minh/hiển thị kết quả; IPN vẫn
là nguồn duy nhất được phép thay đổi trạng thái thanh toán.

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
docker compose -f docker-compose.prod.yml --env-file .env config --quiet
docker compose -f docker-compose.prod.yml --env-file .env up -d
docker compose -f docker-compose.prod.yml --env-file .env exec -T nginx nginx -t
docker compose -f docker-compose.prod.yml ps      # all (healthy)?
docker compose -f docker-compose.prod.yml logs -f cloudflared   # "Registered tunnel connection"
```

### Notification realtime rollout

Set `NOTIFICATION_CORS_ORIGIN` in `/opt/vietride/infra/docker/.env` before starting the new
Notification image. Deploy Notification and Nginx together, validate the loaded configuration,
then reload Nginx and run the Phase 12 smoke script through the public origin:

```bash
cd /opt/vietride/infra/docker
docker compose -f docker-compose.prod.yml --env-file .env config --quiet
docker compose -f docker-compose.prod.yml --env-file .env up -d notification nginx
docker compose -f docker-compose.prod.yml --env-file .env exec -T nginx nginx -t
docker compose -f docker-compose.prod.yml --env-file .env exec -T nginx nginx -s reload
```

Run `node scripts/test-notification-phase12-realtime.js` from a trusted checkout with its required
URL, token and test-ID environment variables. The URL must resolve through public Nginx, not the
container port. Confirm an authorized connection, `UNAUTHORIZED` for invalid credentials, delivery
only to the intended user and no `userId` in `notification:created`.

### Rollback
Re-run **Deploy (production)** with a previous good tag (e.g. `v0.9.0`). Compose pulls the old
images and restarts — no rebuild.

---

## Part E — Log access for the BE team (dozzle, no SSH)

Goal: BE can read production logs themselves — without an SSH account, and without seeing any
other container on the server. Two pieces do this, and **both are required**:

1. `dozzle` (read-only log UI) reaches Docker through `dockerproxy`, which allows only
   list-containers / read-logs / watch-events and blocks every write (`POST=0`). Dozzle has no
   shell and no restart button. `DOZZLE_FILTER=name=vietride_` limits it to this stack, so
   unrelated containers on the same daemon stay invisible.
2. **Cloudflare Access** in front of the hostname. Dozzle itself is unauthenticated.

> ⚠️ Without the Access policy in step 2, `logs.vietride.online` is **public** and production
> logs — request payloads, emails, tokens — are readable by anyone who guesses the subdomain.
> Create the Access policy **before** adding the public hostname, not after.

### 1. Create the Access application (do this FIRST)
Zero Trust dashboard → Access → Applications → **Add an application** → *Self-hosted*:

| Field | Value |
| --- | --- |
| Application domain | `logs` . `vietride.online` |
| Policy name | `BE team` |
| Action | **Allow** |
| Include | **Emails** → the BE members' emails (or *Emails ending in* `@yourdomain`) |

Login method: **One-time PIN** needs no identity provider — members get a code by email.

### 2. Add the tunnel public hostname
Networks → Tunnels → `vietride` → Public Hostnames → Add:

| Subdomain | Domain | Service |
| --- | --- | --- |
| `logs` | `vietride.online` | `http://dozzle:8080` |

Same pattern as `api` → `http://nginx:80` and `db` → `http://adminer:8080`: cloudflared resolves
the service name on `vietride_net`. Dozzle listens on 8080 and is **not** published to the host.

### 3. Deploy
No new `.env` keys and no workflow change — the next **Deploy (production)** run copies the
compose file and `up -d` creates `dozzle` + `dockerproxy`. To bring them up by hand:
```bash
cd /opt/vietride/infra/docker
docker compose -f docker-compose.prod.yml --env-file .env up -d dockerproxy dozzle
```

### 4. Verify the lockdown
```bash
# From your laptop — must return a Cloudflare Access login page, never the dozzle UI:
curl -sI https://logs.vietride.online | head -1     # expect 302 → cloudflareaccess.com

# On the server — the proxy must refuse writes even though it can list containers:
docker compose -f docker-compose.prod.yml exec dozzle wget -qO- http://dockerproxy:2375/containers/json | head -c 80
docker compose -f docker-compose.prod.yml exec dozzle wget -qO- --post-data='' http://dockerproxy:2375/containers/prune   # expect 403 Forbidden
```
Then open the URL in a browser: you should hit the Access email prompt, and after logging in see
only `vietride_*` containers.

### Revoking access
Remove the email from the Access policy — effective immediately, no server change, no redeploy.

---

## Notes
- Gateway chỉ mở anonymous exact `GET /v1/tracking/shared-trip/context`; mọi request Tracking khác
  tiếp tục cần Identity JWT. Header `X-Trip-Share-Token` phải được forward nguyên vẹn và phải được
  redact khỏi access/application logs.
- Socket guest dùng namespace `/shared`, path `/tracking/socket.io` và đi trực tiếp qua Nginx đến
  Tracking vì Gateway chỉ proxy HTTP. Nginx phải giữ WebSocket upgrade cho `/tracking/socket.io`;
  không route namespace này qua Gateway.
- Notification realtime dùng namespace mặc định `/`, path `/notification/socket.io` và cũng đi
  trực tiếp qua Nginx. Gateway không có route WebSocket. Handshake xác thực Identity RS256 access
  token từ `auth.token`, fallback `Authorization: Bearer`; server tự join room theo `sub` và không
  nhận `userId` từ client.
- Phase 12 hiện giả định một Notification replica. Không tăng replica trước khi có Socket.IO Redis
  adapter; nếu tăng sớm, một notification có thể được persist nhưng socket trên replica khác không
  nhận event. REST inbox vẫn là nguồn dữ liệu bền vững.
- Phase 13 hiện giả định một Tracking replica cho room delivery. Không tăng replica trước khi có
  Socket.IO Redis adapter; nếu tăng sớm, viewer có thể không nhận event/revoke từ replica khác.
- Rotation `TRACKING_SHARE_TOKEN_SECRET` vô hiệu hóa ngay toàn bộ link v1 hiện tại. Triển khai secret
  mới đồng nhất trên mọi Tracking replica, restart service, kiểm tra link cũ bị từ chối và yêu cầu
  Passenger tạo link mới. Không log hoặc in secret/token trong lệnh kiểm tra.
- Sau deploy, kiểm tra ba queue terminal riêng `tracking-trip-share-completed`,
  `tracking-trip-share-cancelled`, `tracking-trip-share-disrupted` đã bind lần lượt với
  `trip.trip.completed`, `trip.trip.cancelled`, `trip.trip.disrupted`; kiểm tra `/ready`, retry/DLQ và
  không xóa queue đang còn message.
- `tracking`, `notification` và `rag` đã nằm trong image build matrix và production compose. Khi
  đổi Socket.IO route, luôn deploy Nginx cùng service sở hữu route và chạy `nginx -t` trước reload.
- Postgres/Redis/RabbitMQ have **no published host ports** — reach them via
  `docker compose exec postgres psql ...` when needed.
- Frontend: when the FE is ready, either add an `frontend` container to the compose and switch the
  nginx `location /` to `proxy_pass http://frontend;`, or mount its static build into nginx.
