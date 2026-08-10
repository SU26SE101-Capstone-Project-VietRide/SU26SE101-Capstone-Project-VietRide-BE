# ADR 0005 — UTC instants with Vietnam-facing presentation

**Status:** Accepted — 2026-08-09
**Owners:** Vũ (BE lead)
**Supersedes:** none
**Amends:** [SU26SE101_VIETRIDE_technical_context_v7.md](../../SU26SE101_VIETRIDE_technical_context_v7.md), [BACKEND_SOURCE_OF_TRUTH.md §4.4 / §5.4 / §9.4](../../BACKEND_SOURCE_OF_TRUTH.md), [VietRide_API_Contract_v1.md](../../VietRide_API_Contract_v1.md)

## Context

VietRide serves one business timezone, but timestamps and calendar values were interpreted through a mixture of UTC, fixed `+07:00`, Windows timezone identifiers, and the host or PostgreSQL session timezone. The same instant therefore remained correct while date-only search, schedules, reports, and human-readable notification text could move to the previous day.

## Decision

VietRide uses two explicit temporal models plus duration:

1. **Instant:** persisted as PostgreSQL `TIMESTAMPTZ` and represented internally by `.NET DateTimeOffset` or a JavaScript ISO string normalized to UTC. Timestamp input must contain `Z` or an explicit offset; a timestamp without an offset is invalid.
2. **Vietnam business calendar:** `DateOnly`, `TimeOnly`, `dayOfWeek`, report/search dates, and recurring departure schedules use the IANA timezone `Asia/Ho_Chi_Minh`. The timezone is a system constant; no timezone identifier or fixed offset is stored per record.
3. **Duration:** elapsed time is a duration and has no timezone.

An inclusive Vietnam date range is converted to a UTC half-open interval. For example, `2026-08-10` becomes `[2026-08-09T17:00:00Z, 2026-08-10T17:00:00Z)`. Timestamp queries use `>= fromUtc && < toUtcExclusive`; they do not cast a timestamp to `date` using the database session timezone.

PostgreSQL, containers, internal HTTP, Redis state, RabbitMQ events, Outbox payloads, and Hangfire run in UTC. Their serialized instants end in `Z`. Hangfire cron expressions use `TimeZoneInfo.Utc`; comments may record the corresponding Vietnam time. SQL calendar aggregation must explicitly use `AT TIME ZONE 'Asia/Ho_Chi_Minh'`.

Every JSON instant at a FE-facing `/v1/*` HTTP response boundary and every Tracking WebSocket emission is converted through the IANA timezone `Asia/Ho_Chi_Minh` and serialized as RFC 3339 with its resolved offset, for example `2026-08-10T12:00:00+07:00`. The equivalent internal representation is `2026-08-10T05:00:00Z`; this is a representation change only and does not alter the instant. File downloads and other non-JSON responses are not buffered or transformed.

Public and internal presentation are selected from the request/emission boundary, not from the host timezone. Gateway-generated public errors follow the public `+07:00` policy. Internal errors remain UTC `Z`. Idempotency replay preserves the existing cache namespace and mutation protection while converting cached public JSON to the current presentation before sending it.

DriverSchedule requests remain `DateOnly` + `TimeOnly` + ISO weekday (`1=Monday ... 7=Sunday`). Schedule responses expose the additive constant `timeZone: "Asia/Ho_Chi_Minh"`; no database column is added.

Notification persistence and event data retain UTC instants. Notification HTTP responses use the FE-facing `+07:00` representation, while human-facing Vietnamese notification and email text formats those instants with `Asia/Ho_Chi_Minh`.

Provider adapters may accept a provider-defined local format (for example VNPay `yyyyMMddHHmmss`) but must interpret it as `Asia/Ho_Chi_Minh` and normalize it to UTC immediately at the adapter boundary.

## Consequences

- The same instant and the same date query produce identical results regardless of OS, container, PostgreSQL session timezone, or caller offset.
- Existing timestamp data requires no migration because `TIMESTAMPTZ` stores instants rather than presentation offsets.
- Clients receive one consistent Vietnam representation from public HTTP/WebSocket boundaries, but must still parse RFC 3339 values and compare instants rather than raw timestamp strings.
- Internal clients and event consumers continue to receive UTC `Z`; public clients receive the same instant with the `Asia/Ho_Chi_Minh` offset.
- A future multi-country design must store an IANA timezone on the owning Operator or Schedule and is outside this ADR.
