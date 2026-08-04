using MediatR;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Application.Features.RouteChangeProposals;

public sealed record ListOperatorRouteChangeProposalsQuery(Guid OperatorId, Guid? TripId, string? Status, string? Type, int? Page, int? PageSize) : IRequest<PagedResult<RouteChangeProposalDto>>;
