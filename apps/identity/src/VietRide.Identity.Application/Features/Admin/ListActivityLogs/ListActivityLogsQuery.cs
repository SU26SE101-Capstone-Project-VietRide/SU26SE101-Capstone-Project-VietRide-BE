using MediatR;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Application.Features.Admin.ListActivityLogs;

public sealed record ListActivityLogsQuery(
    string CallerRole,
    Guid? UserId,
    string? Action,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int? Page,
    int? PageSize) : IRequest<PagedResult<AdminActivityLogItemDto>>;
