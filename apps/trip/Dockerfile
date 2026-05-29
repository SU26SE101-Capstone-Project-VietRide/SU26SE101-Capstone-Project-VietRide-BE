# Multi-stage build for VietRide.Trip.Api (representative .NET service Dockerfile;
# trip/booking/payment/parcel are identical except for SERVICE name).
#
# Build context = repo root (so libs/ + apps/ are visible). docker-compose builds with
# `context: ../..` from infra/docker/.

ARG SERVICE=Trip

# ── Build stage ─────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
ARG SERVICE
WORKDIR /src

# Copy global.json + Directory.Build.props so SDK version + MSBuild rules apply.
COPY global.json Directory.Build.props Directory.Packages.props ./

# Copy libs (referenced by every service).
COPY libs/dotnet/ libs/dotnet/

# Copy this specific service.
COPY apps/trip/ apps/trip/

# Restore + publish.
RUN dotnet restore "apps/trip/src/VietRide.Trip.Api/VietRide.Trip.Api.csproj"
RUN dotnet publish "apps/trip/src/VietRide.Trip.Api/VietRide.Trip.Api.csproj" \
      -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ── Runtime stage ───────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
# The alpine runtime ships without ICU; .NET aborts on startup without it (globalization
# is not invariant). Install icu-libs so culture-aware formatting (Money, dates) works.
RUN apk add --no-cache icu-libs
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:5002 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 5002
HEALTHCHECK --interval=15s --timeout=3s --retries=5 \
  CMD wget -qO- http://127.0.0.1:5002/health || exit 1

ENTRYPOINT ["dotnet", "VietRide.Trip.Api.dll"]
