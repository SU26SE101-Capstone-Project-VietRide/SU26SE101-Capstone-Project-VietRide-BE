---
name: ef-migration
description: Create or apply an EF Core migration for a VietRide .NET service using the per-service IDesignTimeDbContextFactory (no host boot needed), ensuring snake_case, soft-delete/audit columns, and a reversible Down(). Use when a schema change is needed for identity/trip/booking/payment/parcel.
---

# EF Core migration (per-service, design-time)

Each service ships an `IDesignTimeDbContextFactory<TDbContext>` under
`apps/<svc>/src/VietRide.<Svc>.Infrastructure/Design/`, so `dotnet ef` runs WITHOUT
booting the host (the host requires `INTERNAL_JWT_SECRET`).

## Add a migration (run from repo root)
```bash
dotnet ef migrations add <Name> \
  -p apps/<svc>/src/VietRide.<Svc>.Infrastructure \
  -s apps/<svc>/src/VietRide.<Svc>.Api \
  -o Migrations
```
Example (identity): `-p apps/identity/src/VietRide.Identity.Infrastructure -s apps/identity/src/VietRide.Identity.Api`.

## Apply (Postgres must be running)
```bash
dotnet ef database update \
  -p apps/<svc>/src/VietRide.<Svc>.Infrastructure \
  -s apps/<svc>/src/VietRide.<Svc>.Api
```
Override design-time connection via env (`IDENTITY_DESIGN_CONNECTION`, `TRIP_DESIGN_CONNECTION`, …); default targets `localhost:5432` with `.env.example` creds.

## Rules
- **Snake_case** schema (shared naming convention) — verify the generated SQL uses snake_case, not PascalCase.
- **Soft-delete + audit**: tables map `deleted_at` from `ISoftDeletable` (getter-only — soft-delete marker only); `created_at`, `updated_at`, `row_version` from `BaseEntity`/`IAuditable`; `is_active` (when present) maps from `IActivatable` — that is a SEPARATE activation toggle, NOT part of soft-delete (ADR 0003).
- **No cross-DB foreign keys** — logical FK only (`db-schema/_global/cross-service-references.md`). The migration must not create a real FK to another service's table.
- Migration must be **reversible**: a real `Down()` (never empty for a destructive change).
- **Never edit a migration that is already merged** — add a new one.
- Cross-check the resulting schema against `db-schema/<service>/schema.sql` (canonical DDL).
- Don't add a NuGet package as a side effect; if a design package is missing, declare its version in `Directory.Packages.props` (CPM) and reference it version-less.

## Verify with the task policy

Migration lifecycle checks are task-specific and remain mandatory when listed in acceptance;
they are not full regression. Use the plan's exact connection/database and commands:

1. Generate the migration and its designer/snapshot under `Infrastructure/Migrations/`.
2. Inspect generated SQL against canonical DDL, snake_case, cross-service FK, audit, soft-delete,
   activation, and reversible `Down()` rules.
3. Apply from an empty database, migrate down to the prior migration, and reapply the new
   migration.
4. Run `dotnet ef migrations has-pending-model-changes` with the same project/startup arguments
   and require no pending model changes.
5. Format only the generated/changed C# paths with
   `dotnet format apps/<svc>/VietRide.<Svc>.sln --verify-no-changes --include <changed paths>`;
   generated `.cs` must be CRLF.

Do not append full solution tests/builds. Full touched-solution regression belongs only to
`/audit-day <N>`.
