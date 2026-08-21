using MediatR;
using VietRide.Shared.Kernel.Primitives;
namespace VietRide.Parcel.Application.Features.Reliability.Incidents;

public sealed record ListParcelIncidentsQuery(
    Guid OperatorId,
    string? Status,
    string? Type,
    string? Search,
    Guid? TripId,
    Guid? AssigneeId,
    string? SlaState,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Page,
    int PageSize) : IRequest<PagedResult<ParcelIncidentListItem>>;
