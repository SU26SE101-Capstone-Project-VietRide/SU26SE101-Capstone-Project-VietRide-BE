namespace VietRide.Parcel.Application.Features.Parcels.OperatorList;

public sealed record OperatorParcelTripResponse(
    Guid TripId,
    string? Status,
    DateTimeOffset? DepartureAt,
    DateTimeOffset? ArrivalEstimate,
    OperatorParcelVehicleResponse? Vehicle);
