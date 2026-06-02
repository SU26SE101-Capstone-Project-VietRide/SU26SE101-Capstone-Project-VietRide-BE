namespace VietRide.Shared.Kernel.Primitives;

/// Base entity with audit columns. Concrete entities inherit and set Id type.
/// Audit columns auto-populated by AuditingInterceptor in VietRide.Shared.Persistence.
public abstract class BaseEntity<TId> where TId : notnull
{
    public TId Id { get; protected set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int RowVersion { get; set; }
}

/// Marker — entity row carries CreatedAt/UpdatedAt managed by AuditingInterceptor.
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; set; }
    DateTimeOffset UpdatedAt { get; set; }
}

/// Soft delete. Row is excluded from normal queries once DeletedAt is set
/// (partial unique indexes use `WHERE deleted_at IS NULL`). Read-only here to preserve
/// domain encapsulation — entities mutate via a domain method (e.g. User.SoftDelete());
/// EF/interceptors write the column via property metadata, not a public setter.
/// Aggregates: Operator, User, Station, Stop, Route, Vehicle. See ADR 0003.
public interface ISoftDeletable
{
    DateTimeOffset? DeletedAt { get; }
}

/// Activation toggle — a business enable/disable flag, DISTINCT from soft delete.
/// An inactive row still exists (deleted_at IS NULL) and can be reactivated. NOT every
/// soft-deletable entity is activatable: User uses its `status` enum for this axis and
/// has no `is_active` column. Aggregates with `is_active`: Operator, Station, Stop, Route,
/// Vehicle (+ non-soft-deletable lookups). See ADR 0003.
public interface IActivatable
{
    bool IsActive { get; }
}
