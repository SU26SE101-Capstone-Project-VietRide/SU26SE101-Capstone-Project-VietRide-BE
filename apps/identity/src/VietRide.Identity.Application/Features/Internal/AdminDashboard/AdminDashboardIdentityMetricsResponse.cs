namespace VietRide.Identity.Application.Features.Internal.AdminDashboard;

public sealed record AdminDashboardIdentityMetricsResponse(
    long ActiveUserCount,
    IReadOnlyList<Guid> ApprovedActiveOperatorIds,
    IReadOnlyList<AdminDashboardUserRoleCountResponse> UserRoleCounts,
    IReadOnlyList<AdminDashboardOperatorStatusCountResponse> OperatorStatusCounts);
