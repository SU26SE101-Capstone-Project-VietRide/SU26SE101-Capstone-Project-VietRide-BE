using MediatR;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Application.Features.Reliability.ApprovalRequests;

public sealed record ListParcelApprovalRequestsQuery(
    Guid DriverUserId,
    Guid OperatorId,
    string? Type,
    string? Status,
    int Page,
    int PageSize) : IRequest<PagedResult<ParcelApprovalRequestListItem>>;

public sealed record ParcelApprovalRequestListItem(
    Guid RequestId,
    string RequestType,
    string Status,
    Guid TripId,
    Guid? ParcelId,
    Guid? IncidentId,
    Guid? StopId,
    IReadOnlyList<Guid> UnresolvedParcelIds,
    string Reason,
    IReadOnlyList<string> EvidenceReferences,
    Guid RequestedByUserId,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ExpiresAt,
    string ValidityCondition,
    IReadOnlyList<string> AvailableActions);
