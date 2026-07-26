using System.Text.Json.Serialization;

namespace VietRide.Trip.Api.Controllers.Requests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SubstituteVehicleRequest(
    Guid ReplacementVehicleId,
    DateTimeOffset EstimatedRecoveryDepartureAt,
    string Reason,
    bool NotifyPassengers = true,
    ReplacementCrewRequest? ReplacementCrew = null);
