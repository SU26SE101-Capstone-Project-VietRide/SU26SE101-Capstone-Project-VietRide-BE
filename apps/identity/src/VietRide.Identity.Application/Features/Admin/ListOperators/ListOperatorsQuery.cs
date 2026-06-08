using MediatR;

namespace VietRide.Identity.Application.Features.Admin.ListOperators;

public sealed record ListOperatorsQuery(
    string CallerRole,
    int? Page,
    int? PageSize,
    string? Search,
    string? SortBy,
    string? SortDir,
    string? Status) : IRequest<VietRide.Shared.Kernel.Primitives.PagedResult<OperatorListItemDto>>;
