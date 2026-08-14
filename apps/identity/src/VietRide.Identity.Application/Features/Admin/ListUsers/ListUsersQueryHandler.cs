using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.Time;

namespace VietRide.Identity.Application.Features.Admin.ListUsers;

public sealed class ListUsersQueryHandler : IRequestHandler<ListUsersQuery, PagedResult<AdminUserListItemDto>>
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

    public ListUsersQueryHandler(IUserRepository users)
    {
        _users = users;
    }

    public async Task<PagedResult<AdminUserListItemDto>> Handle(
        ListUsersQuery request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.CallerRole, UserRole.SYSTEM_ADMIN.ToString(), StringComparison.Ordinal))
            throw new ForbiddenException("FORBIDDEN", "Only SYSTEM_ADMIN can list users.");

        var sortBy = string.IsNullOrWhiteSpace(request.SortBy) ? "createdAt" : request.SortBy.Trim();
        if (!AllowedSortFields.Contains(sortBy))
            throw new BadRequestException("INVALID_SORT_FIELD", "SortBy is not supported.");

        var options = new QueryOptions
        {
            Search = request.Search,
            IncludeDeleted = request.IncludeDeleted,
            Page = request.Page ?? 1,
            PageSize = request.PageSize ?? 20,
            SortBy = sortBy,
            SortDir = string.IsNullOrWhiteSpace(request.SortDir) ? "desc" : request.SortDir,
        };

        DateTimeOffset? fromUtc = request.From.HasValue
            ? BusinessTime.ToUtc(request.From.Value, TimeOnly.MinValue)
            : null;
        DateTimeOffset? toUtcExclusive = request.To.HasValue
            ? BusinessTime.ToUtc(request.To.Value.AddDays(1), TimeOnly.MinValue)
            : null;

        var page = request.From.HasValue || request.To.HasValue
            ? await _users.ListAdminUsersFilteredAsync(
                options,
                ParseEnum<UserRole>(request.Role),
                ParseEnum<UserStatus>(request.Status),
                request.OperatorId,
                fromUtc,
                toUtcExclusive,
                cancellationToken)
            : await _users.ListAdminUsersAsync(
                options,
                ParseEnum<UserRole>(request.Role),
                ParseEnum<UserStatus>(request.Status),
                request.OperatorId,
                cancellationToken);

        return PagedResult<AdminUserListItemDto>.Create(
            page.Items.Select(ToDto).ToArray(),
            page.Page,
            page.PageSize,
            page.TotalItems);
    }

    private static TEnum? ParseEnum<TEnum>(string? value)
        where TEnum : struct, Enum
        => string.IsNullOrWhiteSpace(value) ? null : Enum.Parse<TEnum>(value, ignoreCase: true);

    private static AdminUserListItemDto ToDto(User user)
        => new(
            user.Id,
            user.Email,
            user.DisplayName,
            user.Phone?.Value,
            user.AvatarUrl,
            user.Role.ToString(),
            user.Status.ToString(),
            user.OperatorId,
            user.CreatedAt,
            user.UpdatedAt,
            user.DeletedAt);
}
