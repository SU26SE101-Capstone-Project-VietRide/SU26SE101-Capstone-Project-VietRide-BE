using MediatR;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Application.Features.OperatorUsers.ListOperatorUsers;

public sealed record ListOperatorUsersQuery(
    ListOperatorUsersScope Scope,
    string CallerRole,
    Guid? CallerOperatorId,
    Guid? OperatorId,
    int? Page,
    int? PageSize,
    string? Search,
    string? SortBy,
    string? SortDir,
    string? Role,
    string? Status) : IRequest<PagedResult<OperatorUserListItemDto>>;
