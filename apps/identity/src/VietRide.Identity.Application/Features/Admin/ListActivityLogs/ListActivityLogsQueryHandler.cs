using System.Text.Json;
using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Application.Features.Admin.ListActivityLogs;

public sealed class ListActivityLogsQueryHandler
    : IRequestHandler<ListActivityLogsQuery, PagedResult<AdminActivityLogItemDto>>
{
    private readonly IActivityLogRepository _activityLogs;

    public ListActivityLogsQueryHandler(IActivityLogRepository activityLogs)
    {
        _activityLogs = activityLogs;
    }

    public async Task<PagedResult<AdminActivityLogItemDto>> Handle(
        ListActivityLogsQuery request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.CallerRole, UserRole.SYSTEM_ADMIN.ToString(), StringComparison.Ordinal))
            throw new ForbiddenException("FORBIDDEN", "Only SYSTEM_ADMIN can list activity logs.");

        var options = new QueryOptions
        {
            Page = request.Page ?? 1,
            PageSize = request.PageSize ?? 20,
        };
        ActivityLogAction? action = string.IsNullOrWhiteSpace(request.Action)
            ? null
            : Enum.Parse<ActivityLogAction>(request.Action, ignoreCase: true);
        var page = await _activityLogs.ListAsync(
            options,
            request.UserId,
            action,
            request.From,
            request.To,
            cancellationToken);

        return PagedResult<AdminActivityLogItemDto>.Create(
            page.Items.Select(ToDto).ToArray(),
            page.Page,
            page.PageSize,
            page.TotalItems);
    }

    private static AdminActivityLogItemDto ToDto(ActivityLog activityLog)
        => new(
            activityLog.Id,
            new AdminActivityLogActorDto(
                activityLog.Actor.Id,
                activityLog.Actor.Email,
                activityLog.Actor.DisplayName,
                activityLog.Actor.Role.ToString()),
            activityLog.Action.ToString(),
            ParseMetadata(activityLog.Metadata),
            activityLog.IpAddress,
            activityLog.UserAgent,
            activityLog.CreatedAt);

    private static JsonElement? ParseMetadata(string? metadata)
    {
        if (metadata is null)
            return null;

        using var document = JsonDocument.Parse(metadata);
        return document.RootElement.Clone();
    }
}
