using System.Text.Json.Serialization;

namespace VietRide.Trip.Api.Controllers.Requests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CargoTransferRequest(
    Guid ParcelId,
    Guid TargetTripId,
    string TargetState,
    bool AllowCapacityOverflow);
