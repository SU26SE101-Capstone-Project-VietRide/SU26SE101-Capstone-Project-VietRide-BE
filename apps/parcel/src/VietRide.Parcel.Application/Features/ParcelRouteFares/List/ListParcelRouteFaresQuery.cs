using VietRide.Shared.Application.Cqrs;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Application.Features.ParcelRouteFares.List;

public sealed record ListParcelRouteFaresQuery(
    Guid OperatorId,
    Guid? RouteId,
    string? SizeCategory,
    int Page,
    int PageSize,
    string? Search = null,
    string? SortBy = null,
    string? SortDir = null,
    DateOnly? EffectiveAt = null,
    string? Status = null) : IQuery<PagedResult<ParcelRouteFareGroupResponse>>;
