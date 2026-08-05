using MediatR;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Application.Features.RouteChangeProposals;

public sealed record ListDriverRouteChangeProposalsQuery(Guid TripId, Guid UserId, string? Type, int? Page, int? PageSize) : IRequest<PagedResult<RouteChangeProposalDto>>;
