---
name: scaffold-aggregate
description: Scaffold a new .NET aggregate across all four Clean Architecture layers (Domain entity + Application CQRS use cases + Infrastructure EF config & repository + DI wiring) for a VietRide .NET service, following BACKEND_SOURCE_OF_TRUTH §3.2/§3.5 conventions. Use when adding a new entity/aggregate to identity/trip/booking/payment/parcel.
---

# Scaffold a .NET aggregate (Clean Architecture)

Generate the skeleton for one aggregate inside `apps/<service>/`, respecting the
dependency direction and naming rules. **Read the actual current layout of the target
service first** — the file tree below is the convention, not a fixed list (BSOT §3 callout).

## Inputs to confirm before writing
- **Service**: one of `identity | trip | booking | payment | parcel`.
- **Aggregate name** (PascalCase singular, e.g. `RefreshToken`, `Station`, `Voucher`).
- **Id type** (default `Guid`).
- **Which use cases** to scaffold now (default: `Create`, `GetById`, `List`). Only create what is actually needed — do not pre-create unused handlers.
- **Persisted columns** — pull the canonical DDL from `db-schema/<service>/schema.sql`. Do NOT invent columns.

## Hard rules (must hold)
- **Dependency direction** (CI-enforced by NetArchTest): Domain → nothing; Application → Domain; Infrastructure → Domain+Application; Api → Application+Infrastructure.
- **One class per file.** Naming (fixed, BSOT §3.5): `<Verb><Aggregate>Command/Query/Handler/Validator`, `I<Aggregate>Repository`, `<Aggregate>Service`, `<Entity>Configuration`.
- Domain project has **zero external refs** (no EF Core, no MediatR). Invariants live in entity methods (e.g. `token.Revoke()`), not in handlers.
- Controllers call `MediatR.Send(...)` only — never a service/repo directly.
- Money is `Money` (BIGINT VND, floor-1000) from `VietRide.Shared.Kernel`. Never decimal.
- EF: snake_case columns (shared naming convention), soft-delete (`deleted_at` via getter-only `ISoftDeletable`); entities with an enable/disable toggle ALSO implement `IActivatable` (`is_active`) — separate concern, see ADR 0003. Audit columns via `IAuditable`. Base entity from `VietRide.Shared.Kernel/Primitives/BaseEntity.cs`.
- Repository extends the generic `IRepository<TEntity,TId>` / `EfRepository<TEntity,TId>` from the shared libs; only add aggregate-specific queries.
- Result/error: handlers return `Result<T>`; map failures to RFC 7807 ProblemDetails with UPPER_SNAKE_CASE `errorCode`.

## File set (adapt to the service)
```
Domain/Entities/<Aggregate>.cs                         POCO + invariant methods (: BaseEntity, IAuditable, ISoftDeletable, IActivatable (if activatable))
Domain/Enums/<X>Status.cs                              if the aggregate has a status machine
Domain/Events/<Aggregate><VerbPast>.cs                 domain event(s) if any
Application/Abstractions/Repositories/I<Aggregate>Repository.cs
Application/Abstractions/Services/I<Aggregate>Service.cs        only if logic is reused across handlers
Application/Services/<Aggregate>Service.cs                      impl (Application layer, NOT Infrastructure)
Application/Features/<Aggregate>/Create<Aggregate>/{Command,CommandHandler,CommandValidator,<Aggregate>Dto}.cs
Application/Features/<Aggregate>/Get<Aggregate>ById/{Query,QueryHandler}.cs
Application/Features/<Aggregate>/List<Aggregate>/{Query,QueryHandler}.cs
Infrastructure/Persistence/Configurations/<Aggregate>Configuration.cs   IEntityTypeConfiguration<T>
Infrastructure/Persistence/Repositories/<Aggregate>Repository.cs        : EfRepository<>, I<Aggregate>Repository
```
Also: add `DbSet<<Aggregate>>` to the service `DbContext`, register repo + service in `InfrastructureServiceCollectionExtensions.AddInfrastructure()` (or the Application DI extension for the service).

## Steps
1. Read `db-schema/<service>/schema.sql` + README for the canonical table/columns + status enum values.
2. Read an existing aggregate in the SAME service if one exists, and mirror its style exactly. If none yet, mirror the shared-lib base types.
3. Write Domain entity (private setters, factory/`Create` method enforcing invariants), enums, events.
4. Write Application layer (repo interface, optional service, the requested Command/Query handlers + validators + DTOs).
5. Write Infrastructure EF configuration (snake_case, soft-delete filter, audit) + repository impl, register DbSet + DI.
6. Do NOT add a migration here — use the `ef-migration` skill afterward.

## Verify
- `dotnet build apps/<service>/VietRide.<Service>.sln -c Release` is clean.
- `dotnet format apps/<service>/VietRide.<Service>.sln --verify-no-changes` reports no changes.
- NetArchTest dependency-direction tests still pass (`dotnet test`).
- If you added a `PackageReference`, it must be version-less (CPM) and the version declared in `Directory.Packages.props`.
