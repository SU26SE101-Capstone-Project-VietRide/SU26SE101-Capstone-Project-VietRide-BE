# Multi-stage build for VietRide.Booking.Api (representative .NET service Dockerfile;
# trip/booking/payment/parcel are identical except for SERVICE name).
#
# Build context = repo root (so libs/ + apps/ are visible). docker-compose builds with
# `context: ../..` from infra/docker/.

ARG SERVICE=Booking

# ── Build stage ─────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
ARG SERVICE
WORKDIR /src

# Copy global.json + Directory.Build.props so SDK version + MSBuild rules apply.
COPY global.json Directory.Build.props Directory.Packages.props ./

# Copy libs (referenced by every service).
COPY libs/dotnet/ libs/dotnet/

# Copy this specific service.
COPY apps/booking/ apps/booking/

# Restore + publish.
RUN dotnet restore "apps/booking/src/VietRide.Booking.Api/VietRide.Booking.Api.csproj"
RUN dotnet publish "apps/booking/src/VietRide.Booking.Api/VietRide.Booking.Api.csproj" \
      -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ── Runtime stage ───────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
# The alpine runtime ships without ICU; .NET aborts on startup without it (globalization
# is not invariant). tzdata is required for the Asia/Ho_Chi_Minh business calendar.
RUN apk add --no-cache icu-libs tzdata
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=5003 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 5003
HEALTHCHECK --interval=15s --timeout=3s --retries=5 \
  CMD wget -qO- http://127.0.0.1:5003/health || exit 1

ENTRYPOINT ["dotnet", "VietRide.Booking.Api.dll"]
