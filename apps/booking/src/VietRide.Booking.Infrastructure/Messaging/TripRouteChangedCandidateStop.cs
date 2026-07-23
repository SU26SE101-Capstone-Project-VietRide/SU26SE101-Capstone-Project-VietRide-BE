using System.Text.Json.Serialization;

namespace VietRide.Booking.Infrastructure.Messaging;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TripRouteChangedCandidateStop
{
    public Guid? StopId { get; init; }
    public Guid? StationId { get; init; }
    public string StationName { get; init; } = string.Empty;
    public int Sequence { get; init; }
    public DateTimeOffset EstimatedArrivalAt { get; init; }
}
