# Implementation Plan - Tracking Service Phase 6

## Task
Continue Tracking Service Phase 6: Off-route Detection.

## Template
Use the NestJS scaffold/module workflow from `docs/developer-guides/nest/prompt-templates.md`, plus event/outbox guidance from `docs/developer-guides/nest/nest-event-handling.md` and Tracking phase-by-phase rules from `docs/developer-guides/nest/tracking-service-timeline.md`.

## Current Phase
Phase 6 is the first unchecked phase in `docs/developer-guides/nest/tracking-service-timeline.md`.

## Scope
- Add an `off-route` module for route deviation detection.
- Add a production-shaped route geometry provider interface.
- Add a noop/default provider for current production isolation until Trip-Route-Vehicle endpoint exists.
- Trigger off-route evaluation from the existing `gps:update` flow after `LocationService.recordLocation()`.
- Algorithm:
  - Calculate distance from current GPS point to nearest route segment.
  - If nearest distance is `> 500m`, start/keep Redis timer `tracking:off_route_since:{tripId}`.
  - Publish alert only when deviation is continuous for `> 2 minutes`.
  - If vehicle returns to route before threshold, clear Redis timer and do not publish.
- Create Outbox event:
  - `eventType`: `OffRouteAlert`
  - payload: `tripId`, `latitude`, `longitude`, `distanceMeters`, `detectedAt`
- Do not publish RabbitMQ directly; Phase 8 handles outbox publishing.

## Constraints
- Do not add new TypeScript dependencies.
- Use Prisma ORM through local `TrackingPrismaService`; do not use shared PrismaService.
- Keep Identity JWT verification as-is; do not add mock auth token flows.
- E2E may use fake provider in Nest testing module, but must not mock auth.
- Do not delete, rename, or move files.
- Keep LF line endings.
- No `console.log`.
- Business-layer logging, if needed, uses `pino`.
- No magic numbers: thresholds/TTLs/durations must be named constants.
- Do not mark Phase 6 done or update `CHANGELOG_AI.md` until USER confirms manual/backend test ok.

## Planned Files
- Add `apps/tracking/src/off-route/off-route.constants.ts`
- Add `apps/tracking/src/off-route/route-geometry.provider.ts`
- Add `apps/tracking/src/off-route/noop-route-geometry.provider.ts`
- Add `apps/tracking/src/off-route/off-route.service.ts`
- Add `apps/tracking/src/off-route/off-route.module.ts`
- Add `apps/tracking/src/off-route/off-route.service.spec.ts`
- Update `apps/tracking/src/location/location.gateway.ts`
- Update `apps/tracking/src/location/location.module.ts`
- Update `apps/tracking/src/location/location.gateway.e2e-spec.ts`
- Add `scripts/test-tracking-phase6.js` if the Socket.IO behavior can be smoke-tested through environment config without real downstream dependencies.
- Update `TASK.md` after USER approval by adding Phase 6 to `## In Progress`.

## Implementation Steps
1. Add `OffRouteModule` with constants, route geometry provider interface, noop provider, and `OffRouteService`.
2. Implement nearest-point-to-polyline distance using existing in-repo math style; reuse ETA distance helper where appropriate.
3. Implement Redis timer behavior:
   - no route geometry or too few points -> no alert.
   - on-route -> delete `tracking:off_route_since:{tripId}`.
   - first off-route sample -> set timer with recorded/detected timestamp and return no alert.
   - continued off-route beyond threshold -> create one outbox event and keep dedupe behavior so repeated samples do not spam.
4. Wire `OffRouteService.handleGpsUpdate(event)` into `LocationGateway.updateLocation()` after recording GPS.
5. Add unit tests for short drift, continuous off-route beyond 2 minutes, and return-to-route clearing Redis.
6. Extend socket e2e mocks so driver `gps:update` invokes off-route detection while preserving existing GPS, ETA, and approaching-alert behavior.
7. Add script verify for Phase 6 if feasible with fake route geometry/test config; otherwise report dependency limitation clearly.
8. Run required verification:
   - `npx nx run tracking:lint`
   - `npx nx run tracking:test`
   - `npx nx run tracking:test:e2e`
   - `npx nx run tracking:build`

## Rollback Plan
If verification fails after two fix attempts, stop and report the exact failing command and error details to the user. Do not continue self-fixing beyond that limit.
