namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record TripSummarySnapshot(
    Guid TripId,
    string Status,
    DateTimeOffset DepartureAt,
    DateTimeOffset ArrivalEstimate,
    TripRouteSummarySnapshot Route,
    TripVehicleSummarySnapshot Vehicle)
{
    public Guid? DriverUserId { get; init; }
    public Guid? AssistantUserId { get; init; }
    public IReadOnlyList<TripStopSummarySnapshot> Stops { get; init; } = [];
}
