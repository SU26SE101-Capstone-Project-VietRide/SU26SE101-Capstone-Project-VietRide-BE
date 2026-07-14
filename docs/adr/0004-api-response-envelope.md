# ADR 0004 — Standard API response envelope (`ApiResponse<T>`) for all FE-facing HTTP responses

**Status:** Accepted — 2026-06-01 (approved by BE lead Vũ; rollout sequenced BEFORE Day 3 close — "option A")
**Owners:** Vũ (BE lead)
**Supersedes:** none
**Amends (on acceptance):** [BACKEND_SOURCE_OF_TRUTH.md §5.4 / §5.5 / §5.7](../../BACKEND_SOURCE_OF_TRUTH.md), [VietRide_API_Contract_v1.md](../../VietRide_API_Contract_v1.md)
**Related:** [ADR 0002 — gateway thin proxy](0002-gateway-thin-proxy-vs-bff.md), `libs/dotnet/VietRide.Shared.Web/Filters/ProblemDetailsExceptionFilter.cs`, BSOT §5.9 error-code registry; reference pattern surveyed from a sibling project (`FPT-EXE-201` `DTOs/Common`: `ApiResponse<T>`, `PagedResult<T>`, `QueryOptions`, `QuerySpecMetadataDto`).

## Context

Day 1–3 shipped with the response convention recorded in BSOT §5.4/§5.5/§5.7:

- **Success** = the bare DTO, no wrapper (`Ok(dto)` / `StatusCode(201, dto)`); list = `{ items, total, page, pageSize }`.
- **Error** = RFC 7807 ProblemDetails (`application/problem+json`), enforced globally by `ProblemDetailsExceptionFilter`, carrying the `errorCode` from the BSOT §5.9 registry.
- JSON is camelCase (ASP.NET Core MVC default `JsonSerializerDefaults.Web`; verified — no override).

This is standards-aligned, but the **frontend consumes two different envelope shapes** (a bare object on success, a problem+json on error) and there is **no single place to surface cross-cutting metadata** (a request/trace id for debugging across microservices). The BE lead wants a **single, uniform response envelope** so the FE has one parse path for both success and error, plus a consistent slot for an internal business error code and a correlation id.

VietRide is an **internal product with one known FE team** (not a public API consumed by third parties, not heavily CDN-cached), and we are at **Day 3 of the timeline** — only the Identity service exposes endpoints. Changing the response convention now (≈6 endpoints + the contract) is far cheaper than after the remaining services ship. These two facts materially change the cost/benefit versus a mature public API: the uniformity/DX win is real and the usual downsides of an envelope (HTTP-status tunneling, cache defeat, generic-tooling loss) are either avoidable by discipline or low-impact for this audience.

A sibling project (`FPT-EXE-201`) already runs a mature envelope: `ApiResponse<T>` (auto-produced by a base controller for success and a global exception filter for errors), `PagedResult<T>`, and a `QueryOptions` paging/sort/search input. We **adapt** that pattern — we do not copy it — to fit VietRide's microservices shape and its existing assets (the §5.9 error-code registry, the gateway `X-Request-Id`, soft-delete per ADR 0003).

## Decision

Adopt a **single response envelope `ApiResponse<T>` for every FE-facing HTTP response** across **both stacks** (.NET services + any NestJS HTTP endpoint), produced centrally (not hand-built per action), under five hard rules. Service-to-service `/internal/v1/*` endpoints are intentionally excluded from success wrapping: they return the raw DTO/list on success for simple internal client contracts, while failures still use the standardized ADR 0004 error envelope from the shared exception filter:

### 1. Envelope shape

```jsonc
// Success — single resource
{
  "success": true,
  "statusCode": 200,                 // mirrors the HTTP status line (see Rule 2)
  "message": "Đăng ký thành công",   // optional, for FE toast/UX; omit when not useful
  "data": { /* the DTO, camelCase */ },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}

// Success — list (data IS a PagedResult<T>)
{
  "success": true,
  "statusCode": 200,
  "data": {
    "items": [ /* ... */ ],
    "page": 1, "pageSize": 20, "totalItems": 57,
    "totalPages": 3, "hasNextPage": true, "hasPreviousPage": false
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}

// Error — HTTP status line stays accurate (400/401/404/409/422/429/500...)
{
  "success": false,
  "statusCode": 400,
  "error": {
    "code": "AUTH_OTP_INVALID",            // BSOT §5.9 registry code (UPPER_SNAKE_CASE)
    "message": "Mã xác thực không đúng.",
    "fields": [ { "field": "code", "message": "..." } ]   // only for validation errors
  },
  "meta": { "traceId": "req-abc123", "timestamp": "2026-06-01T10:00:00Z" }
}
```

### 2. HTTP status codes are kept and authoritative

The real HTTP status line (200/201/204/400/401/403/404/409/422/429/5xx) is **always set correctly** — the envelope does **not** replace it. `statusCode` in the body **mirrors** it for FE/debug convenience; the status line is the source of truth. **Never** return `200 OK` with `success:false`. (204 No Content keeps an empty body — no envelope.)

### 3. Internal error code (`error.code`) is preserved from the §5.9 registry

The envelope's `error.code` carries the existing BSOT §5.9 `errorCode` (e.g. `AUTH_OTP_INVALID`, `AUTH_OTP_RATE_LIMIT_EXCEEDED`). The registry and its discipline (UPPER_SNAKE_CASE, one code per documented failure) are **unchanged** — only their transport moves from the RFC 7807 `errorCode` member into `error.code`. `error.fields[]` replaces ProblemDetails `errors[]` for field-level validation. We **drop the `application/problem+json` media type** (IETF) in exchange for one uniform envelope — an acceptable trade for an internal, single-FE product.

### 4. Correlation: `meta.traceId`

`meta.traceId` is populated from the gateway-stamped `X-Request-Id` (ADR 0002 / proxy middleware), giving the FE and logs one id to correlate a request across services. `meta.timestamp` is the response time (UTC ISO-8601). No other redundant fields at the root.

### 5. Produced centrally in shared libs (both stacks), controllers stay thin

- **.NET** (`libs/dotnet`):
  - `PagedResult<T>` + `QueryOptions` (page/pageSize-clamped-1..100/search/searchIn/sortBy/sortDir/includeDeleted) live in `VietRide.Shared.Kernel` (or `.Application`) so query handlers return `PagedResult<T>` and queries bind `QueryOptions`.
  - `ApiResponse` / `ApiResponse<T>` + a result filter (`IAlwaysRunResultFilter`) that **auto-wraps** FE-facing `ObjectResult` values into the success envelope, plus an `ApiResponseExceptionFilter` that **replaces** `ProblemDetailsExceptionFilter` and maps exceptions → the error envelope (preserving HTTP status + `error.code`), live in `VietRide.Shared.Web`. Successful `/internal/v1/*` (or `/internal/*`) responses are skipped by the result filter and return the raw DTO/list; exception/error responses are not skipped and keep the standardized error envelope. Controllers keep returning `Ok(dto)` / `StatusCode(201, dto)` — the filter wraps FE-facing successes; **controller bodies barely change**. The exception filter logs structured context (userId from `sub`, path, method, `traceId`) via Serilog — pattern borrowed from the reference `GlobalExceptionFilter`, adapted to keep VietRide's `error.code` + the `ValidationException→422` (not 400) distinction + the `TooManyRequestsException→429` / `DomainException→422` arms.
  - **Model-binding validation** (malformed JSON, missing non-nullable body field, type mismatch — the surface that `[ApiController]` otherwise auto-answers with a built-in 400 `ValidationProblemDetails`) is routed through the SAME envelope via an `ApiBehaviorOptions.InvalidModelStateResponseFactory` override (preferred over a ModelState action filter). FluentValidation failures and model-binding failures both return `422 VALIDATION_ERROR` in the standard `error` envelope.
- **TS** (`libs/shared`): mirror `ApiResponse`/`PagedResult`/`QueryOptions` types in `libs/shared/contracts`; implement a Nest response interceptor + exception filter in `libs/shared/nest-common` so any FE-facing Nest HTTP endpoint emits the identical shape. A **contract test** asserts the .NET and TS envelopes match.
- **Gateway** stays a pass-through proxy (ADR 0002, unchanged) — it forwards each service's envelope verbatim.

### 6. Request side is standardized too (list/query inputs)

List/collection endpoints bind a shared **`QueryOptions`** from the query string (camelCase): `?page=1&pageSize=20&search=...&searchIn=email,phone&sortBy=createdAt&sortDir=desc&includeDeleted=false`.

- `page` defaults 1; `pageSize` defaults 20, **clamped to max 100** (matches BSOT §5.7).
- `sortBy` + `searchIn` are **whitelisted per aggregate** (a query handler/repository rejects any field not in its allow-list) — this is a **security requirement** (prevents arbitrary-column sort/search → injection / info-leak), borrowed from the reference's "SortBy must be whitelisted in repository".
- `sortDir` ∈ {`asc`,`desc`} (default `desc`); this `sortBy`+`sortDir` pair **supersedes** BSOT §5.7's `?sort=-field` convention (confirm on acceptance).
- `includeDeleted` (default `false`) honors soft-delete (ADR 0003) — only admin/privileged endpoints expose it.
- **Defined now, applied later:** Day-3 Identity has no list endpoint (all single-resource), so `QueryOptions`/`PagedResult` types ship with the shared libs but are first *wired* into an endpoint at the first list endpoint (Day 4+, e.g. `GET /v1/users`). Defining the standard now is cheap and prevents per-endpoint drift.
- (Optional, deferred) a `QuerySpecMetadataDto` + `GET /v1/.../query-specs` endpoint can later expose each list's searchable/sortable fields for a dynamic FE filter UI — out of scope until a screen needs it.

## Rationale

1. **One parse path for the FE** — success and error share `{ success, statusCode, data | error, meta }`; the FE writes one response handler, not per-endpoint shape handling.
2. **Keeps every existing asset** — HTTP status codes, the §5.9 error-code registry, soft-delete (ADR 0003 `includeDeleted`), gateway `X-Request-Id` (now surfaced as `traceId`).
3. **Central production, thin controllers** — the base-controller/filter approach (borrowed from the reference project) means the envelope is automatic; workers don't hand-build it, mirroring how `ProblemDetailsExceptionFilter` works today.
4. **Cheapest now** — only Identity has endpoints; the contract + tests to migrate are small. The same change after 10 services ship would be far more expensive.
5. **Fits the audience** — internal, single-FE, non-public, not CDN-heavy: the envelope's usual downsides are avoidable (Rule 2) or negligible here, while the DX win is concrete.

## Consequences

### Positive

- Uniform, predictable responses; FE integration simpler and less error-prone.
- `meta.traceId` gives cross-service correlation for free (a real microservices win).
- Rich pagination (`totalPages`/`hasNextPage`/`hasPreviousPage`) and a safe, clamped, whitelist-sortable `QueryOptions` for all list endpoints.
- Controllers remain thin; the envelope is centrally enforced in two shared libs.

### Negative

- **Drops `application/problem+json`** (IETF standard + generic tooling/Swagger problem integration). Accepted for an internal single-FE product; `error.code` retains the machine-readable signal.
- **Two implementations to keep in sync** (.NET `VietRide.Shared.Web` + TS `nest-common`). Mitigated by a cross-stack contract test and a shared type mirror in `libs/shared/contracts`.
- `statusCode` in the body duplicates the HTTP status line (kept deliberately for FE/debug; status line remains authoritative).
- Migration touches every shipped endpoint's **tests** (assert `.data` / `.error.code`) and all API Contract examples.

### Follow-ups (only after this ADR is Accepted)

1. Rewrite **BSOT §5.4 (success), §5.5 (error), §5.7 (pagination)** to the envelope + `PagedResult<T>` + `QueryOptions` spec above; bump BSOT version + §13 changelog.
2. Update **VietRide_API_Contract_v1.md** — wrap every response example in the envelope.
3. **Re-plan a dedicated feature/day** (`/plan-day`) to implement: the shared types (.NET `ApiResponse<T>`/`PagedResult<T>`/`QueryOptions` + TS mirror in `libs/shared/contracts`), the success result-filter + the `ApiResponseExceptionFilter` replacing `ProblemDetailsExceptionFilter` + the `InvalidModelStateResponseFactory` override (model-binding failures → `422 VALIDATION_ERROR` envelope), the Nest interceptor + exception filter, the cross-stack contract test, and the migration of Day 1–3 Identity endpoints + their tests. (Request-side `QueryOptions`/whitelist-sort is built here but first wired at the first list endpoint, Day 4+.)
4. Add an explicit success-envelope invariant line to `AGENTS.md` + `dotnet-worker.md`/`nest-worker.md` so future days don't drift (the gap noted during the Day-3 response review).
5. (Optional, later) `QuerySpecMetadataDto`-style endpoint (`GET /v1/.../query-specs`) for dynamic FE filter/sort UIs — defer until a list endpoint needs it.

## Decisions resolved at acceptance (2026-06-01)

- **`statusCode` in body:** KEPT (mirrors HTTP status line; status line stays authoritative). Approved.
- **Sort convention:** `sortBy` + `sortDir` (explicit, whitelist-validatable) **supersedes** BSOT §5.7's `?sort=-field`. Approved — BSOT §5.7 to be rewritten accordingly.
- **`pageSize` cap:** max 100 (BSOT §5.7 unchanged). Approved.
- **Rollout timing:** option A — implement the envelope + migrate Identity Day 1–3 BEFORE running `/verify` + `/audit-day 3`, so Day 3 closes on the new standard (no double migration).
