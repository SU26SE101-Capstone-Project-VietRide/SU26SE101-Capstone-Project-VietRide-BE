using VietRide.Shared.Application.Cqrs;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.Application.Features.Bookings.History;

public sealed record GetBookingHistoryQuery(
    Guid UserId,
    string? Status,
    string? From,
    string? To,
    int Page,
    int PageSize,
    bool IncludeShuttleRequests = false) : IQuery<PagedResult<BookingHistoryItemDto>>;
