using System.Text.Json.Serialization;
using VietRide.Parcel.Application.Abstractions.ServiceClients;

namespace VietRide.Parcel.Application.Features.Parcels.AvailableTrips;

public sealed record AvailableTripResponse(
    Guid TripId,
    Guid RouteId,
    string Status,
    Guid OperatorId,
    string OperatorName,
    TripStationDto OriginStation,
    TripStationDto DestinationStation,
    DateTimeOffset DepartureDateTime,
    DateTimeOffset EstimatedArrivalTime,
    long EstimatedPriceVnd,
    decimal DepositPercent,
    long EstimatedDepositVnd)
{
    [JsonIgnore]
    public long PriceVnd => EstimatedPriceVnd;

    [JsonIgnore]
    public decimal AvailableCargoWeightKg { get; init; }

    [JsonIgnore]
    public decimal AvailableCargoVolumeM3 { get; init; }
}
