namespace VietRide.Trip.Application.Events;

public sealed class TripRouteChangedCandidateStop
{
    public TripRouteChangedCandidateStop(
        Guid? stopId,
        Guid? stationId,
        string stationName,
        int sequence,
        DateTimeOffset estimatedArrivalAt)
    {
        if (stopId.HasValue == stationId.HasValue)
            throw new ArgumentException("Exactly one candidate stop identity must be provided.");
        if (string.IsNullOrWhiteSpace(stationName))
            throw new ArgumentException("Station name is required.", nameof(stationName));
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "Sequence must be positive.");

        StopId = stopId;
        StationId = stationId;
        StationName = stationName.Trim();
        Sequence = sequence;
        EstimatedArrivalAt = estimatedArrivalAt;
    }

    public Guid? StopId { get; }
    public Guid? StationId { get; }
    public string StationName { get; }
    public int Sequence { get; }
    public DateTimeOffset EstimatedArrivalAt { get; }
}
