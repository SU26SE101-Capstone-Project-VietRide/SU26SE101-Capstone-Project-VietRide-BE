using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Domain.Entities;

/// <summary>
/// Stub for Day 3 FK satisfaction; Day 6 extends with full schema + behavior.
/// </summary>
public sealed class Operator : BaseEntity<Guid>
{
    // No behavior, no fields beyond Id (inherited from BaseEntity<Guid>).
    // Day 6 will add all remaining columns, the operator_registration_status enum,
    // business methods, and the full EF configuration.
    private Operator() { }
}
