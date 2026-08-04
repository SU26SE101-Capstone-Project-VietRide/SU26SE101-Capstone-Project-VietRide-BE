using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Features.AlternativeRoutes;
using VietRide.Trip.Application.Features.RouteChangeProposals;

namespace VietRide.Trip.UnitTests.Features.RouteChangeProposals;

internal sealed class FakeRouteChangeProposalService : IRouteChangeProposalService
{
    private readonly RouteChangeProposalDto result;
    public FakeRouteChangeProposalService(RouteChangeProposalDto result) => this.result = result;
    public int CreateCalls { get; private set; }
    public (Guid OperatorId, Guid? TripId, string? Status, string? Type, int? Page, int? PageSize)? OperatorListRequest { get; private set; }
    public Task<RouteChangeProposalDto> CreateAsync(Guid tripId, Guid userId, string type, Guid? alternativeRouteId, RouteChangeProposalSnapshotInput? customRoute, Guid? incidentId, string reason, CancellationToken cancellationToken) { CreateCalls++; return Task.FromResult(result); }
    public Task<PagedResult<AlternativeRouteDto>> ListAlternativeRoutesForAssignedCrewAsync(Guid tripId, Guid userId, int? page, int? pageSize, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<PagedResult<RouteChangeProposalDto>> ListForAssignedCrewAsync(Guid tripId, Guid userId, string? type, int? page, int? pageSize, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<PagedResult<RouteChangeProposalDto>> ListForOperatorAsync(Guid operatorId, Guid? tripId, string? status, string? type, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        OperatorListRequest = (operatorId, tripId, status, type, page, pageSize);
        return Task.FromResult(PagedResult<RouteChangeProposalDto>.Create([result], page ?? 1, pageSize ?? 20, 1));
    }
    public Task<RouteChangeProposalDto> GetForOperatorAsync(Guid operatorId, Guid proposalId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<ApproveRouteChangeProposalResponse> ApproveAsync(Guid operatorId, Guid actorUserId, Guid proposalId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<RouteChangeProposalDto> RejectAsync(Guid operatorId, Guid actorUserId, Guid proposalId, string? rejectionReason, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task SupersedePendingAsync(Guid tripId, Guid? actorUserId, Guid? approvedProposalId, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task ExpirePendingForSourceAsync(Guid sourceAlternativeRouteId, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task ExpirePendingForTripAsync(Guid tripId, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
}
