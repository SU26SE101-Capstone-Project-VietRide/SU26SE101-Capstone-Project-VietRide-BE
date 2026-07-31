namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record TripSummarySnapshot(
    Guid TripId,
    string Status,
    DateTimeOffset DepartureAt,
    DateTimeOffset ArrivalEstimate,
    TripRouteSummarySnapshot Route,
    TripVehicleSummarySnapshot Vehicle);
