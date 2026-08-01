namespace VietRide.Booking.Application.Features.Admin.Dashboard;

public sealed record AdminDashboardOperatorStatusDistributionResponse(
    string Status,
    long Count,
    decimal Percent);
