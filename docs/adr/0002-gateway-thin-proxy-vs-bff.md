# ADR 0002 — Gateway: thin proxy + selective BFF (not full BFF)

**Status:** Accepted — 2026-05-26
**Owners:** Vũ (BE lead)
**Supersedes:** none
**Related:** [BACKEND_SOURCE_OF_TRUTH.md §3.4](../../BACKEND_SOURCE_OF_TRUTH.md)

## Context

Two competing patterns for the API edge:

1. **Thin proxy Gateway** — verifies User JWT, mints Internal JWT (HS256, 120s), rate-limits, forwards 1:1 to the matched downstream service. No domain logic, no aggregation.
2. **BFF (Backend-For-Frontend)** — same edge concerns plus aggregator endpoints tailored per FE screen (`GET /v1/passenger/dashboard` calls 4 services in parallel, merges into one payload).

Both patterns route **all** FE traffic through the Gateway — neither allows FE to call services directly. The difference is whether the Gateway also owns aggregation/transformation logic.

## Decision

Default to **thin proxy** (per BACKEND_SOURCE_OF_TRUTH §3.4.1 + §3.4.2). Add **selective BFF endpoints case-by-case** only when a specific FE screen demonstrates measurable pain (latency > 500ms from chained calls, or a screen needs 8+ endpoint waterfall on slow networks).

Aggregator endpoint, when added, lives as a **Nest controller in `apps/gateway/src/<feature>/`** alongside route table — it does NOT replace thin proxy for the rest of the routes.

## Rationale (5 factors)

1. **Capstone scope.** 50 work-days, 1 BE dev (Vũ). Full-BFF requires ~45 aggregator endpoints (3 clients × ~15 screens). Even at 3h/endpoint that's ~17 dev-days extra — 34% of the total budget. Thin proxy keeps that time for business logic where it has higher delivery value.

2. **Realtime breaks BFF.** Tracking Service serves Socket.IO directly via Nginx, bypassing the Gateway (truth source §11.6). BFF aggregation only applies to request/response REST — choosing BFF leaves Tracking as an inconsistent exception, forcing FE into two mental models.

3. **BFF adds a second source of truth for schema.** Every `Booking.totalAmount` → `Booking.totalAmountVnd` rename now updates Booking service + Gateway aggregator + FE — 3 places instead of 2. Capstone-scale schema changes happen 3–5× per entity, multiplying maintenance.

4. **Performance gain is marginal in 2026 stacks.** Passenger App (React Native) and Operator Web (Next.js) ship React Query / SWR by default — N parallel REST calls over HTTP/2 collapse into one round-trip latency. Measured difference on 4G is typically <50 ms vs an equivalent BFF endpoint. Not a justification on its own.

5. **3 FE clients with divergent shapes.** Passenger / Driver / Operator each render "my data" differently. BFF means 3 parallel aggregator endpoint families, each maintained separately. Thin proxy stays neutral — each FE composes the shape it needs.

## Consequences

### Positive

- Day 2 Gateway is feature-complete for routing concerns; remaining work is auth/rate-limit hardening, not endpoint authoring.
- FE teams own their data composition (familiar React Query / TanStack Query workflow).
- Adding a 4th client (e.g. internal Admin tool, partner API) requires zero Gateway work.
- Easier to reason about: every `/v1/*` route has exactly one downstream owner.

### Negative

- A few FE screens will need 4–6 parallel fetches. Acceptable per §4 above; revisit if profiling shows otherwise.
- We give up a single point to apply cross-service caching policies (could be reintroduced later via a dedicated CDN/edge layer if needed).
- "BFF endpoint" remains a tool in the toolbox — but each one is a deliberate exception requiring profiling evidence before code lands.

## When to add a selective BFF endpoint

Threshold for adding `GET /v1/<feature>/<screen>` aggregator:

- FE measures **>500 ms** end-to-end on the screen vs the slowest single endpoint
- OR the screen makes **>6 parallel calls** that must all complete before first paint
- OR a critical UX flow (booking checkout, payment redirect) needs atomic visibility across services that can't be expressed via independent endpoints

When all three are weak, don't add the BFF endpoint — the cost (maintenance + coupling) exceeds the gain.

## Implementation note

Day 3+ Gateway refactor will adopt the modular Nest layout (one `*.module.ts` per concern: `auth.module.ts`, `proxy.module.ts`, `rate-limit.module.ts`, `request-context.module.ts`, `health.module.ts`, `logging.module.ts`). That structural change is independent of thin-proxy vs BFF — it's good Nest hygiene either way.
