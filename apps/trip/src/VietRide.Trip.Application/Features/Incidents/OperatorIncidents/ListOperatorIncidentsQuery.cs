using MediatR;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Application.Features.Incidents.OperatorIncidents;

public sealed record ListOperatorIncidentsQuery(
    Guid OperatorId,
    Guid? TripId,
    string? Category,
    string? Status,
    DateOnly? From,
    DateOnly? To,
    int? Page,
    int? PageSize,
    string? Search = null,
    Guid? ReportedByUserId = null,
    string? SortBy = null,
    string? SortDir = null)
    : IRequest<PagedResult<OperatorIncidentDto>>;
