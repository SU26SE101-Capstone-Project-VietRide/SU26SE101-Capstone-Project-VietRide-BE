using MediatR;

namespace VietRide.Identity.Application.Features.Admin.ListOperators;

public sealed record ListOperatorsQuery(
    string CallerRole,
    int? Page,
    int? PageSize,
    string? Search,
    string? SortBy,
    string? SortDir,
    string? Status,
    bool? IsActive = null,
    DateOnly? From = null,
    DateOnly? To = null,
    string? DateField = null) : IRequest<VietRide.Shared.Kernel.Primitives.PagedResult<OperatorListItemDto>>;
