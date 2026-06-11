using VietRide.Trip.Application.Features.Vehicles;

namespace VietRide.Trip.Api.Controllers.Requests;

public sealed class UpdateVehicleRequest
{
    private SeatLayoutDto? seatLayoutJson;
    private decimal? maxCargoWeightKg;
    private decimal? maxCargoVolumeM3;

    public Guid? VehicleTypeId { get; init; }

    public string? LicensePlate { get; init; }

    public SeatLayoutDto? SeatLayoutJson
    {
        get => seatLayoutJson;
        init
        {
            seatLayoutJson = value;
            HasSeatLayoutJson = true;
        }
    }

    public bool HasSeatLayoutJson { get; private init; }

    public int? TotalSeats { get; init; }

    public decimal? MaxCargoWeightKg
    {
        get => maxCargoWeightKg;
        init
        {
            maxCargoWeightKg = value;
            HasMaxCargoWeightKg = true;
        }
    }

    public bool HasMaxCargoWeightKg { get; private init; }

    public decimal? MaxCargoVolumeM3
    {
        get => maxCargoVolumeM3;
        init
        {
            maxCargoVolumeM3 = value;
            HasMaxCargoVolumeM3 = true;
        }
    }

    public bool HasMaxCargoVolumeM3 { get; private init; }

    public VehicleStatusDto? Status { get; init; }

    public bool? IsActive { get; init; }
}
