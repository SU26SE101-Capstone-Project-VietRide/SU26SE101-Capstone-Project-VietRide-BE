using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.Time;

namespace VietRide.Parcel.Application.Features.ParcelRouteFares.List;

public sealed class ListParcelRouteFaresQueryHandler
    : IRequestHandler<ListParcelRouteFaresQuery, PagedResult<ParcelRouteFareGroupResponse>>
{
    private readonly IParcelRouteFareRepository _repository;
    private readonly ITripServiceClient? _tripClient;
    private readonly IClock _clock;

    public ListParcelRouteFaresQueryHandler(
        IParcelRouteFareRepository repository,
        ITripServiceClient? tripClient = null,
        IClock? clock = null)
    {
        _repository = repository;
        _tripClient = tripClient;
        _clock = clock ?? new SystemClock();
    }

    public async Task<PagedResult<ParcelRouteFareGroupResponse>> Handle(
        ListParcelRouteFaresQuery query,
        CancellationToken cancellationToken)
    {
        if (query.Page < 1)
            throw new CodedValidationException("VALIDATION_ERROR", "Page must be >= 1.");
        if (query.PageSize is < 1 or > 100)
            throw new CodedValidationException("VALIDATION_ERROR", "PageSize must be between 1 and 100.");
        if (query.Search?.Length > 100)
            throw new CodedValidationException("VALIDATION_ERROR", "Search must not exceed 100 characters.");
        var sortBy = string.IsNullOrWhiteSpace(query.SortBy) ? "effectiveFrom" : query.SortBy.Trim();
        if (!sortBy.Equals("priceVnd", StringComparison.OrdinalIgnoreCase)
            && !sortBy.Equals("effectiveFrom", StringComparison.OrdinalIgnoreCase))
            throw new BadRequestException("INVALID_SORT_FIELD", "sortBy must be priceVnd or effectiveFrom.");
        var sortDir = string.IsNullOrWhiteSpace(query.SortDir) ? "desc" : query.SortDir.Trim();
        if (!sortDir.Equals("asc", StringComparison.OrdinalIgnoreCase)
            && !sortDir.Equals("desc", StringComparison.OrdinalIgnoreCase))
            throw new CodedValidationException("VALIDATION_ERROR", "sortDir must be asc or desc.");
        var status = query.Status?.Trim().ToUpperInvariant();
        if (status is not null && status is not ("ACTIVE" or "SCHEDULED" or "EXPIRED"))
            throw new CodedValidationException("VALIDATION_ERROR", "status must be ACTIVE, SCHEDULED or EXPIRED.");

        ParcelSizeCategory? sizeCategory = null;
        if (query.SizeCategory is not null)
        {
            if (!Enum.TryParse<ParcelSizeCategory>(query.SizeCategory, ignoreCase: true, out var parsed)
                || !Enum.IsDefined(parsed))
            {
                throw new CodedValidationException(
                    "INVALID_SIZE_CATEGORY",
                    $"'{query.SizeCategory}' is not a valid ParcelSizeCategory.");
            }

            sizeCategory = parsed;
        }

        var q = _repository.QueryNoTracking().Where(f => f.OperatorId == query.OperatorId);

        if (query.RouteId.HasValue)
            q = q.Where(f => f.RouteId == query.RouteId.Value);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            if (_tripClient is null)
                throw new ParcelDependencyUnavailableException(
                    "UPSTREAM_UNAVAILABLE",
                    "Trip route search client is unavailable.");
            var routeSearch = await _tripClient.SearchRoutesAsync(
                query.OperatorId,
                query.Search,
                cancellationToken);
            if (!routeSearch.Succeeded)
                throw new ParcelDependencyUnavailableException(
                    "UPSTREAM_UNAVAILABLE",
                    routeSearch.Message ?? "Trip route search is unavailable.");
            if (routeSearch.RouteIds.Count == 0)
                return PagedResult<ParcelRouteFareGroupResponse>.Create(
                    [], query.Page, query.PageSize, 0);
            q = q.Where(fare => routeSearch.RouteIds.Contains(fare.RouteId));
        }

        if (sizeCategory.HasValue)
            q = q.Where(f => f.SizeCategory == sizeCategory.Value);

        if (query.EffectiveAt.HasValue || status is not null)
        {
            var anchorDate = query.EffectiveAt ?? BusinessTime.ToLocalDate(_clock.UtcNow);
            var range = BusinessTime.GetUtcDayRange(anchorDate);
            status ??= "ACTIVE";
            q = status switch
            {
                "ACTIVE" => q.Where(f => f.EffectiveFrom < range.ToUtcExclusive
                    && (!f.EffectiveUntil.HasValue || f.EffectiveUntil >= range.FromUtc)),
                "SCHEDULED" => q.Where(f => f.EffectiveFrom >= range.ToUtcExclusive),
                _ => q.Where(f => f.EffectiveUntil.HasValue && f.EffectiveUntil < range.FromUtc),
            };
        }

        var totalItems = await q
            .Select(fare => fare.RouteId)
            .Distinct()
            .LongCountAsync(cancellationToken);
        var descending = sortDir.Equals("desc", StringComparison.OrdinalIgnoreCase);
        var grouped = q.GroupBy(fare => fare.RouteId);
        IOrderedQueryable<IGrouping<Guid, ParcelRouteFare>> orderedGroups;
        if (sortBy.Equals("priceVnd", StringComparison.OrdinalIgnoreCase))
        {
            orderedGroups = descending
                ? grouped.OrderByDescending(group => group.Max(fare => fare.PriceVnd))
                    .ThenByDescending(group => group.Key)
                : grouped.OrderBy(group => group.Min(fare => fare.PriceVnd))
                    .ThenBy(group => group.Key);
        }
        else
        {
            orderedGroups = descending
                ? grouped.OrderByDescending(group => group.Max(fare => fare.EffectiveFrom))
                    .ThenByDescending(group => group.Key)
                : grouped.OrderBy(group => group.Min(fare => fare.EffectiveFrom))
                    .ThenBy(group => group.Key);
        }

        var pageRouteIds = await orderedGroups
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(group => group.Key)
            .ToListAsync(cancellationToken);

        if (pageRouteIds.Count == 0)
        {
            return PagedResult<ParcelRouteFareGroupResponse>.Create(
                [], query.Page, query.PageSize, totalItems);
        }

        var pageFares = await _repository.QueryNoTracking()
            .Where(fare => fare.OperatorId == query.OperatorId
                && pageRouteIds.Contains(fare.RouteId))
            .OrderBy(fare => fare.RouteId)
            .ThenBy(fare => fare.SizeCategory)
            .Select(fare => new
            {
                fare.RouteId,
                Fare = new ParcelRouteFareListItemResponse(
                    fare.SizeCategory.ToString(),
                    fare.PriceVnd.Amount,
                    fare.EffectiveFrom,
                    fare.EffectiveUntil),
            })
            .ToListAsync(cancellationToken);
        var faresByRoute = pageFares
            .GroupBy(item => item.RouteId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ParcelRouteFareListItemResponse>)group
                    .Select(item => item.Fare)
                    .ToArray());
        var items = pageRouteIds
            .Select(routeId => new ParcelRouteFareGroupResponse(
                routeId,
                faresByRoute.TryGetValue(routeId, out var fares) ? fares : []))
            .ToArray();

        return PagedResult<ParcelRouteFareGroupResponse>.Create(
            items, query.Page, query.PageSize, totalItems);
    }
}
