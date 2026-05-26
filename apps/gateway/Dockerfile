# Multi-stage build for NestJS gateway. Build context = repo root.
# docker-compose builds with `context: ../..` from infra/docker/.

# ── Build stage ─────────────────────────────────────────────────────────────────
FROM node:20-alpine AS build
WORKDIR /workspace

# Install deps using workspace package.json (root only — no per-app package.json).
COPY package.json package-lock.json nx.json tsconfig.base.json ./
RUN npm ci --no-audit --no-fund

# Copy sources needed to build gateway (Nx will only build referenced projects).
COPY apps/gateway apps/gateway
COPY libs/shared libs/shared

# Build via Nx (produces dist/apps/gateway).
RUN npx nx run gateway:build --configuration=production --skip-nx-cache

# ── Runtime stage ───────────────────────────────────────────────────────────────
FROM node:20-alpine AS runtime
WORKDIR /app
ENV NODE_ENV=production

# Copy built artifacts.
COPY --from=build /workspace/dist/apps/gateway ./
COPY --from=build /workspace/node_modules ./node_modules
COPY .env.example ./.env.example

EXPOSE 3000
HEALTHCHECK --interval=15s --timeout=3s --retries=5 \
  CMD wget -qO- http://127.0.0.1:3000/health || exit 1

CMD ["node", "main.js"]
