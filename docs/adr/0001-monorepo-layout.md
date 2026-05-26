# ADR 0001 — Monorepo layout: Nx + apps/ + libs/

**Status:** Accepted — 2026-05-26
**Owners:** Vũ (BE lead)

## Context

BACKEND_SOURCE_OF_TRUTH Section 3.1 mandates Nx-managed monorepo with `apps/` (1 folder = 1 deployable service) + `libs/` (shared building blocks). BE_TIMELINE_VU Day 1 originally referenced `services/dotnet/` layout — superseded.

## Decision

- Workspace manager: **Nx 22.x** with `@nx-dotnet/core` plugin for .NET tracking.
- 9 apps under `apps/`: 5 .NET services (identity, trip, booking, payment, parcel) + 4 NestJS (gateway, tracking, notification, rag).
- 12 libs:
  - 6 .NET shared libs under `libs/dotnet/` (Kernel, Application, Persistence, Messaging, Http, Web).
  - 6 NestJS shared libs under `libs/shared/` (contracts, nest-common, nest-config, nest-persistence, nest-rabbitmq, nest-redis).
- Each .NET service has its own `.sln` (Api / Application / Domain / Infrastructure + UnitTests + IntegrationTests).
- Root `Directory.Build.props` enforces `Nullable=enable`, `TreatWarningsAsErrors=true`, `TargetFramework=net8.0`.
- Root `global.json` pins .NET SDK to 8.0.x (LTS until 2026-11).

## Consequences

- `nx affected -t build/test` provides incremental CI for both stacks.
- Adding a 10th service = one Nx generator command, no new repo, no submodule.
- Tradeoff: contributors must learn Nx executor wrapper on top of `dotnet build`. `.sln` remains the IDE entry, so IDE workflow unchanged.
