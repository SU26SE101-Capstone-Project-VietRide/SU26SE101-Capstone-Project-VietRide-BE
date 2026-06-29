# Multi-stage build for NestJS rag. Build context = repo root.
# docker-compose builds with `context: ../..` from infra/docker/.

# ── Build stage ─────────────────────────────────────────────────────────────────
FROM node:20-alpine AS build
WORKDIR /workspace

# Install deps using workspace package.json (root only — no per-app package.json).
COPY package.json package-lock.json nx.json tsconfig.base.json eslint.config.mjs ./
COPY scripts/prisma-generate-win.mjs scripts/prisma-generate-win.mjs
# Drop the @nx-dotnet/core plugin from this image's nx.json — the Node image has no
# dotnet SDK, and loading it would abort `nx build`. Repo nx.json is untouched.
RUN node -e "const fs=require('fs');const j=JSON.parse(fs.readFileSync('nx.json'));j.plugins=j.plugins.filter(p=>(p.plugin||p)!=='@nx-dotnet/core');fs.writeFileSync('nx.json',JSON.stringify(j,null,2))"
# Copy workspace libs before install so npm creates the @vietride/* workspace symlinks
# in node_modules — webpack resolves the shared libs through those symlinks.
COPY libs/shared libs/shared
RUN npm ci --no-audit --no-fund --ignore-scripts

# Build shared workspace libraries that webpack externalizes at runtime.
RUN npx nx run-many -t build --projects=contracts,nest-common,nest-config,nest-rabbitmq,nest-redis --skip-nx-cache

# Copy sources needed to build rag (Nx will only build referenced projects).
COPY apps/rag apps/rag

# Generate Prisma client with its runtime engine before bundling.
RUN npx prisma generate --schema=apps/rag/prisma/schema.prisma

# Build via Nx (produces dist/apps/rag).
RUN npx nx run rag:build --configuration=production --skip-nx-cache

# ── Runtime stage ───────────────────────────────────────────────────────────────
FROM node:20-alpine AS runtime
WORKDIR /app
ENV NODE_ENV=production

# Copy built artifacts.
COPY --from=build /workspace/dist/apps/rag ./
COPY --from=build /workspace/node_modules ./node_modules
# webpack externalizes the @vietride/* shared libs but they live as workspace symlinks
# (→ /workspace/libs/shared) that don't exist in this stage. Replace those dangling
# symlinks with the compiled lib packages so require('@vietride/*') resolves at runtime.
RUN rm -rf node_modules/@vietride
COPY --from=build /workspace/dist/libs/shared ./node_modules/@vietride
# Prisma generates native query-engine binaries outside the webpack bundle.
# Keep the generated client beside the bundle at the path Prisma searches at runtime.
COPY --from=build /workspace/apps/rag/src/generated ./src/generated
# Prisma schema + migration SQL — needed by `prisma migrate deploy` at startup.
# The rag baseline migration also runs `CREATE EXTENSION IF NOT EXISTS "vector"`,
# so the Postgres image MUST be pgvector/pgvector:pg16 (see docker-compose.prod.yml).
COPY --from=build /workspace/apps/rag/prisma ./prisma
COPY .env.example ./.env.example

EXPOSE 3003
HEALTHCHECK --interval=15s --timeout=3s --retries=5 \
  CMD wget -qO- http://127.0.0.1:3003/health || exit 1

# Apply pending Prisma migrations (creates the schema + pgvector extension on first
# boot) then start.
CMD ["sh", "-c", "npx prisma migrate deploy --schema=./prisma/schema.prisma && node main.js"]
