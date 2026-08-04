using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Features.AlternativeRoutes;
using VietRide.Trip.Application.Features.RouteChangeProposals;

namespace VietRide.Trip.Application.Abstractions.Services;

public interface IRouteChangeProposalService
{
    Task<PagedResult<AlternativeRouteDto>> ListAlternativeRoutesForAssignedCrewAsync(Guid tripId, Guid userId, int? page, int? pageSize, CancellationToken cancellationToken);
    Task<RouteChangeProposalDto> CreateAsync(Guid tripId, Guid userId, string type, Guid? alternativeRouteId, RouteChangeProposalSnapshotInput? customRoute, Guid? incidentId, string reason, CancellationToken cancellationToken);
    Task<PagedResult<RouteChangeProposalDto>> ListForAssignedCrewAsync(Guid tripId, Guid userId, string? type, int? page, int? pageSize, CancellationToken cancellationToken);
    Task<PagedResult<RouteChangeProposalDto>> ListForOperatorAsync(Guid operatorId, Guid? tripId, string? status, string? type, int? page, int? pageSize, CancellationToken cancellationToken);
    Task<RouteChangeProposalDto> GetForOperatorAsync(Guid operatorId, Guid proposalId, CancellationToken cancellationToken);
    Task<ApproveRouteChangeProposalResponse> ApproveAsync(Guid operatorId, Guid actorUserId, Guid proposalId, CancellationToken cancellationToken);
    Task<RouteChangeProposalDto> RejectAsync(Guid operatorId, Guid actorUserId, Guid proposalId, string? rejectionReason, CancellationToken cancellationToken);
    Task SupersedePendingAsync(Guid tripId, Guid? actorUserId, Guid? approvedProposalId, DateTimeOffset now, CancellationToken cancellationToken);
    Task ExpirePendingForSourceAsync(Guid sourceAlternativeRouteId, DateTimeOffset now, CancellationToken cancellationToken);
    Task ExpirePendingForTripAsync(Guid tripId, DateTimeOffset now, CancellationToken cancellationToken);
}
