using MediatR;

namespace VietRide.Booking.Application.Features.Admin.Dashboard;

public sealed record GetAdminDashboardSummaryQuery(
    DateOnly? From,
    DateOnly? To) : IRequest<AdminDashboardSummaryResponse>;
