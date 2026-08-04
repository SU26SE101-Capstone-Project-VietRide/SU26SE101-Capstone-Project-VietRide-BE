using MediatR;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Features.AlternativeRoutes;

namespace VietRide.Trip.Application.Features.RouteChangeProposals;

public sealed record ListAssignedTripAlternativeRoutesQuery(Guid TripId, Guid UserId, int? Page, int? PageSize) : IRequest<PagedResult<AlternativeRouteDto>>;
