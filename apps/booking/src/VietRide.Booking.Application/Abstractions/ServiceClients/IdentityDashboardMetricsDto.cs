namespace VietRide.Booking.Application.Abstractions.ServiceClients;

public sealed record IdentityDashboardMetricsDto(
    long ActiveUserCount,
    IReadOnlyList<Guid> ApprovedActiveOperatorIds,
    IReadOnlyList<IdentityDashboardUserRoleCountDto> UserRoleCounts,
    IReadOnlyList<IdentityDashboardOperatorStatusCountDto> OperatorStatusCounts);
