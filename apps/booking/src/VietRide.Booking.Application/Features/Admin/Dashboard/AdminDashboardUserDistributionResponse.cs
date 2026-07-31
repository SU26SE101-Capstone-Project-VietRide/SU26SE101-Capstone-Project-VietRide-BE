namespace VietRide.Booking.Application.Features.Admin.Dashboard;

public sealed record AdminDashboardUserDistributionResponse(
    string Role,
    long Count);
