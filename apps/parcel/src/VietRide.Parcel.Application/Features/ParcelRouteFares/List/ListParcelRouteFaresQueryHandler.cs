using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.ParcelRouteFares.Create;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Application.Features.ParcelRouteFares.List;

public sealed class ListParcelRouteFaresQueryHandler : IRequestHandler<ListParcelRouteFaresQuery, PagedResult<ParcelRouteFareResponse>>
{
    private readonly IParcelRouteFareRepository _repository;
    private readonly ITripServiceClient? _tripClient;

    public ListParcelRouteFaresQueryHandler(
        IParcelRouteFareRepository repository,
        ITripServiceClient? tripClient = null)
    {
        _repository = repository;
        _tripClient = tripClient;
    }

    public async Task<PagedResult<ParcelRouteFareResponse>> Handle(ListParcelRouteFaresQuery query, CancellationToken cancellationToken)
    {
        if (query.Page < 1)
            throw new CodedValidationException("VALIDATION_ERROR", "Page must be >= 1.");
        if (query.PageSize is < 1 or > 100)
            throw new CodedValidationException("VALIDATION_ERROR", "PageSize must be between 1 and 100.");
        if (query.Search?.Length > 255)
            throw new CodedValidationException("VALIDATION_ERROR", "Search must not exceed 255 characters.");

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
                return PagedResult<ParcelRouteFareResponse>.Create([], query.Page, query.PageSize, 0);
            q = q.Where(fare => routeSearch.RouteIds.Contains(fare.RouteId));
        }

        if (sizeCategory.HasValue)
            q = q.Where(f => f.SizeCategory == sizeCategory.Value);

        var totalItems = await q.LongCountAsync(cancellationToken);

        var items = await q
            .OrderByDescending(f => f.EffectiveFrom)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(f => new ParcelRouteFareResponse(
                f.RouteId,
                f.SizeCategory.ToString(),
                f.OperatorId,
                f.PriceVnd.Amount,
                f.EffectiveFrom,
                f.EffectiveUntil,
                f.CreatedAt,
                f.UpdatedAt))
            .ToListAsync(cancellationToken);

        return PagedResult<ParcelRouteFareResponse>.Create(items, query.Page, query.PageSize, totalItems);
    }
}
