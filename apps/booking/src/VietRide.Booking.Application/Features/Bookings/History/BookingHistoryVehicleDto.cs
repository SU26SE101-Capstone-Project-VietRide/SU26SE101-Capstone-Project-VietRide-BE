using System.Text.Json.Serialization;

namespace VietRide.Booking.Application.Features.Bookings.History;

public sealed record BookingHistoryVehicleDto(
    string LicensePlate,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    BookingHistoryVehicleTypeDto? VehicleType = null);
