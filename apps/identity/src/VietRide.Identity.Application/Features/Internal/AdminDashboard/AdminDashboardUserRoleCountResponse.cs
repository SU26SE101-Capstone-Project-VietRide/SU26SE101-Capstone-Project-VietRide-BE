namespace VietRide.Identity.Application.Features.Internal.AdminDashboard;

public sealed record AdminDashboardUserRoleCountResponse(
    string Role,
    long Count);
