namespace VietRide.Trip.Application.Events;

public sealed class TripRouteChangedAffectedBooking
{
    public TripRouteChangedAffectedBooking(
        Guid bookingId,
        IReadOnlyList<TripRouteChangedCandidateStop> candidateStops)
    {
        if (bookingId == Guid.Empty)
            throw new ArgumentException("Booking id cannot be empty.", nameof(bookingId));

        BookingId = bookingId;
        CandidateStops = Array.AsReadOnly(
            candidateStops?.OrderBy(stop => stop.Sequence).ToArray()
                ?? throw new ArgumentNullException(nameof(candidateStops)));
    }

    public Guid BookingId { get; }
    public IReadOnlyList<TripRouteChangedCandidateStop> CandidateStops { get; }
}
