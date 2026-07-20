using MediatR;

namespace VietRide.Identity.Application.Features.Admin.OutboxDlq;

public sealed record GetAdminOutboxDlqQuery(
    string? Cursor,
    int PageSize,
    string? Service,
    string? EventType,
    string SortDir) : IRequest<AdminOutboxDlqResponseDto>;
