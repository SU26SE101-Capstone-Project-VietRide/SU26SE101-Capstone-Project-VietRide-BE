namespace VietRide.Identity.Application.Abstractions.Repositories;

public sealed record AdminDashboardIdentityMetricsReadResult(
    long ActiveUserCount,
    IReadOnlyList<Guid> ApprovedActiveOperatorIds,
    IReadOnlyList<AdminDashboardIdentityMetricCountReadModel> UserRoleCounts,
    IReadOnlyList<AdminDashboardIdentityMetricCountReadModel> OperatorStatusCounts);
