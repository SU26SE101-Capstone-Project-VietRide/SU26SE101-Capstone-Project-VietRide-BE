using MediatR;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.Application.Features.OperatorBookings.ListOperatorBookings;

public sealed record ListOperatorBookingsQuery(
    Guid OperatorId,
    string? Status,
    Guid? TripId,
    DateOnly? Date,
    string? PassengerPhone,
    string? BookingCode,
    int Page = 1,
    int PageSize = 20,
    string? SortBy = null,
    string SortDir = "desc",
    string? Search = null) : IRequest<PagedResult<OperatorBookingListItem>>;
