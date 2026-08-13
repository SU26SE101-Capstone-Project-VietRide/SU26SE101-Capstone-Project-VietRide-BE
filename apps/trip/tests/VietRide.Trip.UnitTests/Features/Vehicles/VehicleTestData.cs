using System.Text.Json;
using VietRide.Trip.Application.Features.Vehicles;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Features.Vehicles;

internal static class VehicleTestData
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    public static SeatLayoutDto CreateSeatLayout(int totalSeats = 2)
    {
        var seats = Enumerable.Range(1, totalSeats)
            .Select(index => new SeatLayoutSeatDto(
                $"A{index:00}",
                1,
                index,
                1,
                "STANDARD",
                false,
                false,
                false))
            .ToList();

        return new SeatLayoutDto(1, "STANDARD_BUS", totalSeats, 1, totalSeats, 1, [], seats);
    }

    public static Vehicle CreateVehicle(Guid operatorId, Guid? vehicleTypeId = null)
        => Vehicle.Create(
            operatorId,
            vehicleTypeId ?? Guid.NewGuid(),
            "51A-12345",
            JsonSerializer.SerializeToElement(CreateSeatLayout(), WebJsonOptions),
            2,
            1000m,
            10m);
}
