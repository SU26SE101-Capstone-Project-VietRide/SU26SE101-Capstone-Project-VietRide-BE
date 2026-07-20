using MediatR;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Application.Features.PassengerHistory;

public sealed record GetPassengerHistoryQuery(
    Guid UserId,
    string Type,
    string? Status,
    string? From,
    string? To,
    int Page,
    int PageSize) : IRequest<PagedResult<PassengerHistoryItemDto>>;
