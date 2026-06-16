using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Application.Features.OperatorUsers.ListOperatorUsers;

public sealed class ListOperatorUsersQueryHandler
    : IRequestHandler<ListOperatorUsersQuery, PagedResult<OperatorUserListItemDto>>
{
    private static readonly HashSet<string> AllowedSortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "createdAt",
        "email",
        "displayName",
        "role",
        "status",
    };

    private readonly IUserRepository _users;

    public ListOperatorUsersQueryHandler(IUserRepository users)
    {
        _users = users;
    }

    public async Task<PagedResult<OperatorUserListItemDto>> Handle(
        ListOperatorUsersQuery request,
        CancellationToken cancellationToken)
    {
        var operatorId = ResolveOperatorScope(request);
        var role = ParseRole(request.Role);
        var status = ParseStatus(request.Status);
        ValidateSortBy(request.SortBy);

        var options = new QueryOptions
        {
            Page = request.Page ?? 1,
            PageSize = request.PageSize ?? 20,
            Search = request.Search,
            SortBy = string.IsNullOrWhiteSpace(request.SortBy) ? "createdAt" : request.SortBy,
            SortDir = string.IsNullOrWhiteSpace(request.SortDir) ? "desc" : request.SortDir,
        };

        var page = await _users.ListOperatorUsersAsync(
            options,
            operatorId,
            role,
            status,
            cancellationToken);

        return PagedResult<OperatorUserListItemDto>.Create(
            page.Items.Select(ToDto).ToArray(),
            page.Page,
            page.PageSize,
            page.TotalItems);
    }

    private static Guid? ResolveOperatorScope(ListOperatorUsersQuery request)
    {
        return request.Scope switch
        {
            ListOperatorUsersScope.Operator => ResolveOperatorRouteScope(request),
            ListOperatorUsersScope.Admin => ResolveAdminRouteScope(request),
            _ => throw new ForbiddenException("FORBIDDEN", "Only OPERATOR_ADMIN or SYSTEM_ADMIN can list operator users."),
        };
    }

    private static Guid? ResolveOperatorRouteScope(ListOperatorUsersQuery request)
    {
        if (!string.Equals(request.CallerRole, UserRole.OPERATOR_ADMIN.ToString(), StringComparison.Ordinal))
            throw new ForbiddenException("FORBIDDEN", "Only OPERATOR_ADMIN can list operator users.");

        if (!request.CallerOperatorId.HasValue)
            throw new ForbiddenException("FORBIDDEN", "Operator scope is required to list operator users.");

        return request.CallerOperatorId.Value;
    }

    private static Guid? ResolveAdminRouteScope(ListOperatorUsersQuery request)
    {
        if (!string.Equals(request.CallerRole, UserRole.SYSTEM_ADMIN.ToString(), StringComparison.Ordinal))
            throw new ForbiddenException("FORBIDDEN", "Only SYSTEM_ADMIN can list operator users.");

        return request.OperatorId;
    }

    private static UserRole? ParseRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return null;

        return Enum.Parse<UserRole>(role, ignoreCase: true);
    }

    private static UserStatus? ParseStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return null;

        return Enum.Parse<UserStatus>(status, ignoreCase: true);
    }

    private static void ValidateSortBy(string? sortBy)
    {
        if (!string.IsNullOrWhiteSpace(sortBy) && !AllowedSortFields.Contains(sortBy))
            throw new BadRequestException("INVALID_SORT_FIELD", $"Unsupported sort field '{sortBy}'.");
    }

    private static OperatorUserListItemDto ToDto(User user)
        => new(
            user.Id,
            user.Email,
            user.Phone?.Value,
            user.DisplayName,
            user.Role.ToString(),
            user.Status.ToString(),
            user.OperatorId!.Value,
            user.CreatedAt,
            user.AvatarUrl);
}
