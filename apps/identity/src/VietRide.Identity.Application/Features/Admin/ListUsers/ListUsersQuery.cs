using VietRide.Shared.Application.Cqrs;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Application.Features.Admin.ListUsers;

public sealed record ListUsersQuery(
    string CallerRole,
    string? Search,
    string? Role,
    string? Status,
    Guid? OperatorId,
    bool IncludeDeleted,
    int? Page,
    int? PageSize,
    string? SortBy,
    string? SortDir,
    DateOnly? From = null,
    DateOnly? To = null) : IQuery<PagedResult<AdminUserListItemDto>>;
