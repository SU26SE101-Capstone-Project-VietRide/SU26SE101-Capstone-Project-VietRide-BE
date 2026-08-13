using System.Text.Json.Serialization;

namespace VietRide.Parcel.Application.Features.PassengerHistory;

public sealed record PassengerHistoryVehicleDto(
    string LicensePlate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    PassengerHistoryVehicleTypeDto? VehicleType = null);
