# ADR 0003 — Soft-delete marker (`deleted_at`) is separate from the activation flag (`is_active`)

**Status:** Accepted — 2026-05-31
**Owners:** Vũ (BE lead)
**Supersedes:** none
**Related:** [BACKEND_SOURCE_OF_TRUTH.md §9.6](../../BACKEND_SOURCE_OF_TRUTH.md), [AGENTS.md Domain conventions](../../AGENTS.md), `libs/dotnet/VietRide.Shared.Kernel/Primitives/BaseEntity.cs`, `db-schema/_global/README.md`

## Context

The shared `ISoftDeletable` marker bundled two properties:

```csharp
public interface ISoftDeletable
{
    DateTimeOffset? DeletedAt { get; set; }
    bool IsActive { get; set; }
}
```

and BSOT/AGENTS described the soft-delete convention as the single phrase
*"`is_active` + `deleted_at`"*. This caused a concrete defect during Day 3 Task 3.1:
the `users` table has **no `is_active` column**, so an entity forced to implement
`ISoftDeletable` would carry an `IsActive` property with no backing column — EF would
either generate a spurious column (schema violation) or `.Ignore()` it (dead property),
and `SoftDelete()` setting `IsActive = false` would silently not persist.

Investigation across all of `db-schema/` shows the two columns are **two different
concerns**, not a single pattern:

| Column | Meaning | Reversible? | Excluded from normal queries? |
|---|---|---|---|
| `deleted_at` | **Soft delete** — record is gone | only by admin/restore | yes (`WHERE deleted_at IS NULL`) |
| `is_active` | **Activation toggle** — temporarily enabled/disabled (operator suspended, station closed, route paused) | yes, freely | no — inactive rows still queryable |

`db-schema/_global/SCHEMA_REVIEW_REPORT.md` §9 already records this intent explicitly:
*"Operator: is_active = temporary pause, deleted_at = permanent"* and
*"User: deleted_at + status='DELETED'; no separate is_active (semantic redundant with status)"*.

Column inventory of the six "soft-deletable" aggregates:

| Aggregate | `deleted_at` | `is_active` | Activation axis |
|---|---|---|---|
| Operator | ✅ | ✅ | `is_active` |
| **User** | ✅ | ❌ | **`status` enum (`ACTIVE`/`LOCKED`/…)** |
| Station | ✅ | ✅ | `is_active` |
| Stop | ✅ | ✅ | `is_active` |
| Route | ✅ | ✅ | `is_active` |
| Vehicle | ✅ | ✅ | `is_active` |

`User` is not an anomaly — it uses its `status` enum for the activation axis, so a
boolean `is_active` would be redundant state that can desync from `status`. Several
non-soft-deletable tables also carry `is_active` (vouchers, vehicle_types,
driver_schedules, operator_stations, user_devices, subscription_plans), confirming
`is_active` is an independent activation flag, not a soft-delete companion.

The planned `SoftDeleteInterceptor` (BSOT §3.2.1) does not exist yet, and `User` is
the only entity that has ever implemented `ISoftDeletable`, so the blast radius of
fixing the marker now is minimal.

## Decision

**Split the two concerns into two markers in `VietRide.Shared.Kernel`:**

```csharp
/// Soft delete. Row is excluded from normal queries once DeletedAt is set
/// (partial unique indexes use `WHERE deleted_at IS NULL`). Read-only here to
/// preserve domain encapsulation — entities mutate via a domain method
/// (e.g. User.SoftDelete()); EF/interceptors write the column via property
/// metadata, not a public setter. Aggregates: Operator, User, Station, Stop, Route, Vehicle.
public interface ISoftDeletable
{
    DateTimeOffset? DeletedAt { get; }
}

/// Activation toggle — a business enable/disable flag, DISTINCT from soft delete.
/// An inactive row still exists (deleted_at IS NULL) and can be reactivated.
/// NOT every soft-deletable entity is activatable: User uses its `status` enum
/// for this axis and has no `is_active` column. Aggregates with `is_active`:
/// Operator, Station, Stop, Route, Vehicle (+ non-soft-deletable lookups).
public interface IActivatable
{
    bool IsActive { get; }
}
```

- **Soft delete = `deleted_at` only** (+ partial unique index `WHERE deleted_at IS NULL`
  + the future `SoftDeleteInterceptor` global query filter). This is the canonical
  soft-delete convention for all six aggregates, including `User`.
- **`is_active` is a separate optional activation flag.** Entities that need a manual
  enable/disable toggle implement `IActivatable`. `User` does **not** — it uses `status`.
- **No DDL changes.** The schema was already correct; only the C# marker and the
  convention prose (AGENTS.md, BSOT §9.6 / datatype list, `db-schema/_global/README.md`)
  are corrected to stop describing soft-delete as "`is_active` + `deleted_at`".
- Both markers are **getter-only** to keep mutation inside domain methods (BSOT §3.2.3).

## Rationale

1. **Single source of truth for "is this row alive?"** Soft-delete keyed on `deleted_at`
   alone removes the desync bug class (`is_active=true` while `deleted_at != null`).
2. **Matches the schema as designed.** 5/6 aggregates carry `is_active` as a pause flag;
   `User` deliberately uses `status`. The marker now mirrors reality instead of fighting it.
3. **Stops the recurring confusion.** A worker building any aggregate no longer has to
   reconcile "interface wants `IsActive`" with "my table has no `is_active`".
4. **Honors encapsulation.** Getter-only `DeletedAt` lets entities keep `private set` and
   mutate via `SoftDelete()` (the Task 3.1 reviewer's requirement), while the interceptor
   writes the column through EF metadata.
5. **Minimal blast radius now.** Only `User` implements the marker and no `SoftDeleteInterceptor`
   exists yet — the change is cheap today and expensive later.

## Consequences

### Positive

- `User : ISoftDeletable` is now correct and uniform with the other five aggregates.
- Future aggregates (Operator Day 6, trip-route-vehicle entities) opt into `IActivatable`
  only when they genuinely have an enable/disable toggle — no dead `IsActive`.
- The future `SoftDeleteInterceptor` can target `ISoftDeletable` polymorphically and apply
  one global query filter (`DeletedAt == null`) across every soft-deletable entity.

### Negative

- A second marker (`IActivatable`) to learn — mitigated by clear XML docs + this ADR.
- Aggregates that are both soft-deletable and activatable implement two markers
  (e.g. `Operator : ISoftDeletable, IActivatable`) — explicit, but two interfaces instead of one.

### Follow-ups

- Day 6 `Operator` and the trip-route-vehicle aggregates implement `IActivatable` where the
  schema has `is_active`, plus a domain `Activate()`/`Deactivate()` pair.
- When the `SoftDeleteInterceptor` lands (BSOT §3.2.1, ~Day 10), it registers the global
  query filter for every `ISoftDeletable` and sets `DeletedAt` via EF property metadata.
