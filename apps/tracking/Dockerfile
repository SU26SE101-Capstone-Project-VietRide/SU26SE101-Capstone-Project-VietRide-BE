# Multi-stage build for NestJS tracking. Build context = repo root.
# docker-compose builds with `context: ../..` from infra/docker/.

# ── Build stage ─────────────────────────────────────────────────────────────────
FROM node:20-alpine AS build
WORKDIR /workspace

# Install deps using workspace package.json (root only — no per-app package.json).
COPY package.json package-lock.json nx.json tsconfig.base.json eslint.config.mjs ./
# Drop the @nx-dotnet/core plugin from this image's nx.json — the Node image has no
# dotnet SDK, and loading it would abort `nx build`. Repo nx.json is untouched.
RUN node -e "const fs=require('fs');const j=JSON.parse(fs.readFileSync('nx.json'));j.plugins=j.plugins.filter(p=>(p.plugin||p)!=='@nx-dotnet/core');fs.writeFileSync('nx.json',JSON.stringify(j,null,2))"
# Copy workspace libs before install so npm creates the @vietride/* workspace symlinks
# in node_modules — webpack resolves the shared libs through those symlinks.
COPY libs/shared libs/shared
RUN npm ci --no-audit --no-fund --ignore-scripts

# Build shared workspace libraries that webpack externalizes at runtime.
RUN npx nx run-many -t build --projects=contracts,nest-common,nest-config,nest-rabbitmq,nest-redis --skip-nx-cache

# Copy sources needed to build tracking (Nx will only build referenced projects).
COPY apps/tracking apps/tracking

# Build via Nx (produces dist/apps/tracking).
RUN npx nx run tracking:build --configuration=production --skip-nx-cache

# ── Runtime stage ───────────────────────────────────────────────────────────────
FROM node:20-alpine AS runtime
WORKDIR /app
ENV NODE_ENV=production

# Copy built artifacts.
COPY --from=build /workspace/dist/apps/tracking ./
COPY --from=build /workspace/node_modules ./node_modules
# webpack externalizes the @vietride/* shared libs but they live as workspace symlinks
# (→ /workspace/libs/shared) that don't exist in this stage. Replace those dangling
# symlinks with the compiled lib packages so require('@vietride/*') resolves at runtime.
RUN rm -rf node_modules/@vietride
COPY --from=build /workspace/dist/libs/shared ./node_modules/@vietride
# Prisma generates native query-engine binaries outside the webpack bundle.
# Keep the generated client beside the bundle at the path Prisma searches at runtime.
COPY --from=build /workspace/apps/tracking/src/generated ./src/generated
COPY .env.example ./.env.example

EXPOSE 3001
HEALTHCHECK --interval=15s --timeout=3s --retries=5 \
  CMD wget -qO- http://127.0.0.1:3001/health || exit 1

CMD ["node", "main.js"]
