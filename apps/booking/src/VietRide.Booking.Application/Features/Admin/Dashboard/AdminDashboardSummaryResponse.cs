namespace VietRide.Booking.Application.Features.Admin.Dashboard;

public sealed record AdminDashboardSummaryResponse(
    AdminDashboardPeriodResponse Period,
    AdminDashboardComparisonResponse TotalRevenue,
    AdminDashboardComparisonResponse ActiveOperators,
    AdminDashboardComparisonResponse ActiveUsers,
    AdminDashboardComparisonResponse Bookings,
    IReadOnlyList<AdminDashboardUserDistributionResponse> UserDistribution,
    IReadOnlyList<AdminDashboardOperatorStatusDistributionResponse> OperatorStatusDistribution);
