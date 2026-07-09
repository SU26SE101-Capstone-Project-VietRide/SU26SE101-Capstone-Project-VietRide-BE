using System.Text.Json.Serialization;

namespace VietRide.Parcel.Application.Features.Parcels.AvailableTrips;

public sealed record AvailableTripResponse(
    Guid TripId,
    Guid RouteId,
    string OperatorName,
    DateTimeOffset DepartureDateTime,
    long EstimatedPriceVnd,
    long EstimatedDepositVnd)
{
    [JsonIgnore]
    public long PriceVnd => EstimatedPriceVnd;

    [JsonIgnore]
    public decimal AvailableCargoWeightKg { get; init; }
}
