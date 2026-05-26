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

/// Marker — entity supports soft delete via DeletedAt + IsActive partial index.
/// Per BACKEND_SOURCE_OF_TRUTH 4.4 — Operator, User, Station, Stop, Route, Vehicle.
public interface ISoftDeletable
{
    DateTimeOffset? DeletedAt { get; set; }
    bool IsActive { get; set; }
}
