using MediatR;

namespace VietRide.Booking.Application.Features.BookingStats.UpdateBookingStats;

public sealed record UpdateBookingStatsCommand(
    string EventType,
    Guid BookingId,
    BookingStatsTransition Transition,
    long Amount = 0,
    Guid? DedupeId = null) : IRequest<bool>;
