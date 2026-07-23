namespace VietRide.Booking.Application.Features.PendingActions;

public sealed record RouteChangeAffectedBooking(
    Guid BookingId,
    IReadOnlyList<RouteChangeCandidateStop> CandidateStops);
