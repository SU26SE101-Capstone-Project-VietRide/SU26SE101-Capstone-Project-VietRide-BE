using System.Text.Json.Serialization;

namespace VietRide.Trip.Application.Features.Stations;

public sealed record CreateOrLinkOperatorStationResponse(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? OperatorId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? StationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IsActive,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] OperatorStationWarning? Warning,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<StationSearchResult>? NearbyStations)
{
    public static CreateOrLinkOperatorStationResponse Linked(Guid operatorId, Guid stationId, bool isActive) => new(
        operatorId,
        stationId,
        isActive,
        null,
        null);

    public static CreateOrLinkOperatorStationResponse DuplicateNearby(IReadOnlyList<StationSearchResult> nearbyStations) => new(
        null,
        null,
        null,
        new OperatorStationWarning(
            "STATION_DUPLICATE_NEARBY",
            "A nearby active station already exists. Link an existing station instead."),
        nearbyStations);
}
