using MediatR;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.Time;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Incidents.OperatorIncidents;

public sealed class ListOperatorIncidentsHandler
    : IRequestHandler<ListOperatorIncidentsQuery, PagedResult<OperatorIncidentDto>>
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 20;
    private const int MaximumPageSize = 100;
    private readonly IIncidentRepository incidents;
    private readonly IIdentityInternalClient identity;

    public ListOperatorIncidentsHandler(IIncidentRepository incidents, IIdentityInternalClient identity)
    {
        this.incidents = incidents;
        this.identity = identity;
    }

    public async Task<PagedResult<OperatorIncidentDto>> Handle(
        ListOperatorIncidentsQuery request,
        CancellationToken cancellationToken)
    {
        var page = request.Page ?? DefaultPage;
        var pageSize = request.PageSize ?? DefaultPageSize;
        var category = ParseCategory(request.Category);
        var status = ParseStatus(request.Status);
        var rows = await incidents.ListOperatorIncidentsAsync(
            request.OperatorId,
            request.TripId,
            category,
            status == OperatorIncidentStatusFilter.RESOLVED
                ? true
                : status == OperatorIncidentStatusFilter.OPEN ? false : null,
            request.From.HasValue ? BusinessTime.ToUtc(request.From.Value, TimeOnly.MinValue) : null,
            request.To.HasValue ? BusinessTime.ToUtc(request.To.Value.AddDays(1), TimeOnly.MinValue) : null,
            page,
            Math.Min(pageSize, MaximumPageSize),
            cancellationToken);
        var reporterIds = rows.Items.Select(row => row.ReportedByUserId).Distinct().ToArray();
        var profiles = await identity.GetUsersAsync(reporterIds, cancellationToken);
        var items = rows.Items.Select(row => OperatorIncidentMapper.ToDto(row, profiles)).ToArray();
        return PagedResult<OperatorIncidentDto>.Create(items, rows.Page, rows.PageSize, rows.TotalItems);
    }

    private static IncidentCategory? ParseCategory(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : Enum.Parse<IncidentCategory>(value.Trim(), true);

    private static OperatorIncidentStatusFilter? ParseStatus(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : Enum.Parse<OperatorIncidentStatusFilter>(value.Trim(), true);
}
