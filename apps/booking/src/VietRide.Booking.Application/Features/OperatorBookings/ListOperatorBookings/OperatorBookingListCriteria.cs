using VietRide.Booking.Domain.Enums;

namespace VietRide.Booking.Application.Features.OperatorBookings.ListOperatorBookings;

public sealed record OperatorBookingListCriteria(
    Guid OperatorId,
    IReadOnlyCollection<BookingStatus>? Statuses,
    Guid? TripId,
    DateTimeOffset? DepartureFrom,
    DateTimeOffset? DepartureTo,
    Guid? PassengerUserId,
    string? BookingCode,
    int Page,
    int PageSize,
    string SortBy,
    bool SortDescending);
