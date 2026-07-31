namespace VietRide.Booking.Application.Features.Admin.Dashboard;

public sealed record AdminDashboardPeriodResponse(
    DateOnly From,
    DateOnly To,
    string Timezone);
