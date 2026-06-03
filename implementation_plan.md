# Implementation Plan - Tracking Service Phase 2

## Task
Complete Tracking Service Phase 2: GPS Persistence Batch Job.

## Template
Use the NestJS scaffold/module workflow from `docs/developer-guides/nest/prompt-templates.md`, plus Tracking phase-by-phase rules from `docs/developer-guides/nest/tracking-service-timeline.md`.

## Scope
- Complete `gps-batch` repeat queue/job for scheduled Redis GPS buffer flush.
- Add `TRACKING_GPS_FLUSH_ENABLED` and `TRACKING_GPS_FLUSH_INTERVAL_MS` runtime config.
- Flush `tracking:active_trips` buffers into PostgreSQL `gps_trails`.
- Validate each buffered GPS sample and skip malformed rows safely.
- Clear Redis buffer only after successful database insert.
- Add unit/e2e coverage for Phase 2 cases.

## Constraints
- Do not add new TypeScript dependencies.
- Use Prisma ORM through the local `TrackingPrismaService`.
- Do not use mock auth token flows.
- Do not delete, rename, or move files.
- Keep LF line endings.
- Business logic must not use `console.log`.
- Verify with:
  - `npx nx run tracking:lint`
  - `npx nx run tracking:test`
  - `npx nx run tracking:test:e2e`
  - `npx nx run tracking:build`

## Implementation Steps
1. Update Tracking env schema with `TRACKING_GPS_FLUSH_INTERVAL_MS`.
2. Implement queue scheduler/worker lifecycle in `GpsBatchModule` or a provider using existing `bullmq`.
3. Harden `GpsBatchFlushService.flushOnce()` against malformed JSON and DB insert failures.
4. Add unit tests for flush behavior and scheduler gating.
5. Add Phase 2 e2e coverage where appropriate for app wiring.
6. Run the required verification commands.

## Rollback Plan
If verification fails after two fix attempts, stop and report the exact failing command and error details to the user.
