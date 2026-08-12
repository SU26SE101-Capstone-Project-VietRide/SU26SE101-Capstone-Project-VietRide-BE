using System.Text.Json.Serialization;

namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record BookingHistoryVehicleDto(
    string LicensePlate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    BookingHistoryVehicleTypeDto? VehicleType = null);
