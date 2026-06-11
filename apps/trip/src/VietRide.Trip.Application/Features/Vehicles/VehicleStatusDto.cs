using System.Text.Json.Serialization;

namespace VietRide.Trip.Application.Features.Vehicles;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VehicleStatusDto
{
    ACTIVE,
    MAINTENANCE,
    OFF_DUTY,
    RETIRED,
}
