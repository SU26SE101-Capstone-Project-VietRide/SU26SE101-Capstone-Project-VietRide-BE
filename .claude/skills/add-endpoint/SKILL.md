---
name: add-endpoint
description: Add an HTTP endpoint to a VietRide .NET service (thin controller -> MediatR.Send), with the ADR 0004 ApiResponse envelope, Idempotency-Key on mutations, Swashbuckle annotation, and the matching Gateway route entry. Use when exposing a new REST endpoint defined in VietRide_API_Contract_v1.md.
---

# Add a .NET endpoint + wire the Gateway route

## Inputs to confirm
- **Service** + **HTTP method + path** — match `VietRide_API_Contract_v1.md` exactly (request/response shape, status codes).
- The **Command/Query** it dispatches (scaffold it first with `scaffold-aggregate` if missing).
- **Auth mode**: public / user JWT (RS256) / role-gated. Internal-only endpoints use `X-Internal-Auth`.

## Controller rules (BSOT §3.2)
- Controller is **thin**: bind request -> `MediatR.Send(command/query)` -> map `Result<T>` to `ActionResult`. No business logic, no DbContext, no service call.
- One controller per aggregate: `Controllers/<Aggregate>Controller.cs`.
- Annotate with Swashbuckle (`[ProducesResponseType]` per documented status) so FE can generate clients.
- Errors flow through the global ApiResponse envelope filter (ADR 0004) — error shape `{success:false,statusCode,error:{code,message,fields?},meta}`, `error.code` UPPER_SNAKE_CASE from BSOT §5.9. Don't hand-roll error JSON; RFC 7807/`application/problem+json` is dropped.
- **Mutations (POST/PATCH/PUT/DELETE)** must honor the `Idempotency-Key` header (Redis SETNX 24h, via the shared `IdempotencyMiddleware`). Reads do not.
- Money in responses is the raw BIGINT VND.

## Gateway route (apps/gateway/src/config/routes.ts)
- Most prefixes already exist (`/v1/auth`, `/v1/users`, `/v1/bookings`, …). If the new path falls under an existing `prefix`, **no change needed**.
- Only add a `ProxyRoute` when introducing a NEW path family. Set `authRequired` (`none|user|mixed`) and `requiredRoles` to match the contract. `mixed` = some sub-paths public (e.g. VNPay IPN, public parcel confirm).
- Longest-prefix wins (see `matchRoute`); keep health passthrough entries intact.
- FE always calls through the Gateway — never document a direct service URL.

## Steps
1. Confirm the contract entry in `VietRide_API_Contract_v1.md`.
2. Ensure the Command/Query + handler exist (else `scaffold-aggregate`).
3. Add/extend the controller action; add `[ProducesResponseType]`s; apply auth attributes.
4. If a new path family: add one `ProxyRoute` in `routes.ts` and a unit case in `routes.spec.ts` if patterns there cover it.
5. If the endpoint emits an event, use the `add-integration-event` skill.

## Verify
- `.NET`: `dotnet build` + `dotnet format --verify-no-changes` clean for the service sln.
- `Gateway`: `npx nx run gateway:test` and `npx nx run gateway:lint` green (TS = LF endings).
- Manually: Gateway proxies the path to the service; protected path returns 401 without a token; mutation returns the same response on duplicate `Idempotency-Key`.
