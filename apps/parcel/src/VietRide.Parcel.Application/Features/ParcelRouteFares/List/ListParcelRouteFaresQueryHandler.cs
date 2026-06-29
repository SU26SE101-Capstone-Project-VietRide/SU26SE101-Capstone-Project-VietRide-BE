using MediatR;
using Microsoft.EntityFrameworkCore;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.ParcelRouteFares.Create;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Application.Features.ParcelRouteFares.List;

public sealed class ListParcelRouteFaresQueryHandler : IRequestHandler<ListParcelRouteFaresQuery, PagedResult<ParcelRouteFareResponse>>
{
    private readonly IParcelRouteFareRepository _repository;

    public ListParcelRouteFaresQueryHandler(IParcelRouteFareRepository repository)
    {
        _repository = repository;
    }

    public async Task<PagedResult<ParcelRouteFareResponse>> Handle(ListParcelRouteFaresQuery query, CancellationToken cancellationToken)
    {
        if (query.Page < 1)
            throw new CodedValidationException("VALIDATION_ERROR", "Page must be >= 1.");
        if (query.PageSize is < 1 or > 100)
            throw new CodedValidationException("VALIDATION_ERROR", "PageSize must be between 1 and 100.");

        var q = _repository.QueryNoTracking().Where(f => f.OperatorId == query.OperatorId);

        if (query.RouteId.HasValue)
            q = q.Where(f => f.RouteId == query.RouteId.Value);

        ParcelSizeCategory? parsedSize = null;
        if (query.SizeCategory is not null)
        {
            if (!Enum.TryParse<ParcelSizeCategory>(query.SizeCategory, ignoreCase: true, out var s))
                throw new VietRide.Shared.Application.Exceptions.CodedValidationException(
                    "INVALID_SIZE_CATEGORY",
                    $"'{query.SizeCategory}' is not a valid ParcelSizeCategory.");
            parsedSize = s;
            q = q.Where(f => f.SizeCategory == s);
        }

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
