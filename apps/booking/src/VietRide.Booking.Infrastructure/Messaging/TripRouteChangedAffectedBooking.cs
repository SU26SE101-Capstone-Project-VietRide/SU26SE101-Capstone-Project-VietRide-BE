using System.Text.Json.Serialization;

namespace VietRide.Booking.Infrastructure.Messaging;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TripRouteChangedAffectedBooking
{
    public Guid BookingId { get; init; }
    public IReadOnlyList<TripRouteChangedCandidateStop> CandidateStops { get; init; } = [];
}
