using MediatR;

namespace VietRide.Identity.Application.Features.Internal.AdminDashboard;

public sealed record GetAdminDashboardIdentityMetricsQuery(
    DateOnly? From,
    DateOnly? To) : IRequest<AdminDashboardIdentityMetricsResponse>;
