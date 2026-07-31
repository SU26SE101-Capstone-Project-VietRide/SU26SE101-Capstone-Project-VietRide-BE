using VietRide.Shared.Application.Cqrs;

namespace VietRide.Booking.Application.Features.Admin.Dashboard;

public sealed record GetAdminDashboardSummaryQuery(
    DateOnly? From,
    DateOnly? To) : IQuery<AdminDashboardSummaryResponse>;
