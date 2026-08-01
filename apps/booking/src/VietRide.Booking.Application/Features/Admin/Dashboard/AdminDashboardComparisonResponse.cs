namespace VietRide.Booking.Application.Features.Admin.Dashboard;

public sealed record AdminDashboardComparisonResponse(
    long CurrentValue,
    long PreviousValue,
    decimal ChangePercent,
    string Trend);
