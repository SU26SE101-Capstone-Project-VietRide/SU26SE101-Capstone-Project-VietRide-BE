namespace VietRide.Booking.Application.Features.Admin.Dashboard;

public sealed record AdminDashboardSummaryResponse(
    AdminDashboardPeriodResponse Period,
    AdminDashboardComparisonResponse TotalProjectRevenueVnd,
    AdminDashboardComparisonResponse NetTransportRevenueVnd,
    AdminDashboardComparisonResponse NetTicketRevenueVnd,
    AdminDashboardComparisonResponse NetParcelRevenueVnd,
    AdminDashboardComparisonResponse SubscriptionRevenueVnd,
    AdminDashboardComparisonResponse ActiveOperators,
    AdminDashboardComparisonResponse ActiveUsers,
    AdminDashboardComparisonResponse Bookings,
    IReadOnlyList<AdminDashboardUserDistributionResponse> UserDistribution,
    IReadOnlyList<AdminDashboardOperatorStatusDistributionResponse> OperatorStatusDistribution);
