using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.Time;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Trips.Operations;
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
        if (page < 1 || pageSize is < 1 or > MaximumPageSize)
            throw new CodedValidationException("VALIDATION_ERROR", "Invalid paging values.");
        if (request.From.HasValue && request.To.HasValue && request.From > request.To)
            throw new CodedValidationException("VALIDATION_ERROR", "from must be on or before to.");
        var category = ParseCategory(request.Category);
        var status = ParseStatus(request.Status);
        if (request.Search?.Length > 100)
            throw new CodedValidationException("VALIDATION_ERROR", "search must not exceed 100 characters.");
        var sortBy = string.IsNullOrWhiteSpace(request.SortBy) ? "reportedAt" : request.SortBy.Trim();
        if (sortBy is not ("reportedAt" or "resolvedAt"))
            throw new BadRequestException("INVALID_SORT_FIELD", "sortBy must be reportedAt or resolvedAt.");
        var sortDir = string.IsNullOrWhiteSpace(request.SortDir) ? "desc" : request.SortDir.Trim();
        if (sortDir is not ("asc" or "desc"))
            throw new CodedValidationException("VALIDATION_ERROR", "sortDir must be asc or desc.");
        IReadOnlyCollection<Guid> reporterMatches = [];
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var crew = await identity.SearchOperatorCrewAsync(request.OperatorId, request.Search.Trim(), cancellationToken);
            if (!crew.Succeeded)
                throw new TripIdentityUnavailableException(crew.Message ?? "Identity crew search is unavailable.");
            reporterMatches = crew.Users.Select(user => user.UserId).ToArray();
        }
        var hasExtendedFilters = !string.IsNullOrWhiteSpace(request.Search)
            || request.ReportedByUserId.HasValue || !string.IsNullOrWhiteSpace(request.SortBy)
            || !string.IsNullOrWhiteSpace(request.SortDir);
        var rows = hasExtendedFilters
            ? await incidents.ListOperatorIncidentsFilteredAsync(
                request.OperatorId, request.TripId, category,
                status == OperatorIncidentStatusFilter.RESOLVED ? true : status == OperatorIncidentStatusFilter.OPEN ? false : null,
                request.From.HasValue ? BusinessTime.ToUtc(request.From.Value, TimeOnly.MinValue) : null,
                request.To.HasValue ? BusinessTime.ToUtc(request.To.Value.AddDays(1), TimeOnly.MinValue) : null,
                request.Search, reporterMatches, request.ReportedByUserId, sortBy, sortDir,
                page, Math.Min(pageSize, MaximumPageSize), cancellationToken)
            : await incidents.ListOperatorIncidentsAsync(
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
        => ParseEnum<IncidentCategory>(value, "category");

    private static OperatorIncidentStatusFilter? ParseStatus(string? value)
        => ParseEnum<OperatorIncidentStatusFilter>(value, "status");

    private static TEnum? ParseEnum<TEnum>(string? value, string fieldName)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!Enum.TryParse<TEnum>(value.Trim(), true, out var parsed) || !Enum.IsDefined(parsed))
            throw new CodedValidationException("VALIDATION_ERROR", $"{fieldName} is invalid.");
        return parsed;
    }
}
