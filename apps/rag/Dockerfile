# Multi-stage build for NestJS rag. Build context = repo root.
# docker-compose builds with `context: ../..` from infra/docker/.

# ── Build stage ─────────────────────────────────────────────────────────────────
FROM node:20-alpine AS build
WORKDIR /workspace

# Install deps using workspace package.json (root only — no per-app package.json).
COPY package.json package-lock.json nx.json tsconfig.base.json ./
# Drop the @nx-dotnet/core plugin from this image's nx.json — the Node image has no
# dotnet SDK, and loading it would abort `nx build`. Repo nx.json is untouched.
RUN node -e "const fs=require('fs');const j=JSON.parse(fs.readFileSync('nx.json'));j.plugins=j.plugins.filter(p=>(p.plugin||p)!=='@nx-dotnet/core');fs.writeFileSync('nx.json',JSON.stringify(j,null,2))"
# Copy workspace libs before install so npm creates the @vietride/* workspace symlinks
# in node_modules — webpack resolves the shared libs through those symlinks.
COPY libs/shared libs/shared
RUN npm ci --no-audit --no-fund --ignore-scripts

# Copy sources needed to build rag (Nx will only build referenced projects).
COPY apps/rag apps/rag

# Build via Nx (produces dist/apps/rag).
RUN npx nx run rag:build --configuration=production --skip-nx-cache

# ── Runtime stage ───────────────────────────────────────────────────────────────
FROM node:20-alpine AS runtime
WORKDIR /app
ENV NODE_ENV=production

# Copy built artifacts.
COPY --from=build /workspace/dist/apps/rag ./
COPY --from=build /workspace/node_modules ./node_modules
COPY .env.example ./.env.example

EXPOSE 3003
HEALTHCHECK --interval=15s --timeout=3s --retries=5 \
  CMD wget -qO- http://127.0.0.1:3003/health || exit 1

CMD ["node", "main.js"]
