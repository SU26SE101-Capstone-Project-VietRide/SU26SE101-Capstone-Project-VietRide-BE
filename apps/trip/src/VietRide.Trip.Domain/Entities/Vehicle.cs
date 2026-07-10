using System.Text.Json;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Domain.Entities;

public enum VehicleStatus
{
    ACTIVE,
    MAINTENANCE,
    OFF_DUTY,
    RETIRED,
}

/// <summary>
/// Operator-owned vehicle with an opaque seat-layout document.
/// </summary>
public sealed class Vehicle : BaseEntity<Guid>, ISoftDeletable, IActivatable
{
    public Guid OperatorId { get; private set; }
    public Guid VehicleTypeId { get; private set; }
    public string LicensePlate { get; private set; } = string.Empty;
    public JsonElement SeatLayoutJson { get; private set; }
    public int TotalSeats { get; private set; }
    public decimal? MaxCargoWeightKg { get; private set; }
    public decimal? MaxCargoVolumeM3 { get; private set; }
    public IReadOnlyCollection<string>? ImageUrls { get; private set; }
    public VehicleStatus Status { get; private set; } = VehicleStatus.ACTIVE;
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset? DeletedAt { get; private set; }

    private Vehicle() { }

    public static Vehicle Create(
        Guid operatorId,
        Guid vehicleTypeId,
        string licensePlate,
        JsonElement seatLayoutJson,
        int totalSeats,
        decimal? maxCargoWeightKg,
        decimal? maxCargoVolumeM3,
        IReadOnlyCollection<string>? imageUrls = null)
    {
        ValidateGuid(operatorId, nameof(operatorId));
        ValidateGuid(vehicleTypeId, nameof(vehicleTypeId));
        ValidateRequired(licensePlate, nameof(licensePlate));
        ValidateSeatLayout(seatLayoutJson);
        ValidatePositive(totalSeats, nameof(totalSeats));
        ValidateOptionalNonNegative(maxCargoWeightKg, nameof(maxCargoWeightKg));
        ValidateOptionalNonNegative(maxCargoVolumeM3, nameof(maxCargoVolumeM3));

        return new Vehicle
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorId,
            VehicleTypeId = vehicleTypeId,
            LicensePlate = licensePlate.Trim(),
            SeatLayoutJson = seatLayoutJson.Clone(),
            TotalSeats = totalSeats,
            MaxCargoWeightKg = maxCargoWeightKg,
            MaxCargoVolumeM3 = maxCargoVolumeM3,
            ImageUrls = imageUrls?.Select(url => url.Trim()).ToArray(),
            Status = VehicleStatus.ACTIVE,
            IsActive = true,
        };
    }

    public void UpdateSeatLayout(JsonElement seatLayoutJson, int totalSeats)
    {
        ValidateSeatLayout(seatLayoutJson);
        ValidatePositive(totalSeats, nameof(totalSeats));

        SeatLayoutJson = seatLayoutJson.Clone();
        TotalSeats = totalSeats;
    }

    public void ChangeVehicleType(Guid vehicleTypeId)
    {
        ValidateGuid(vehicleTypeId, nameof(vehicleTypeId));
        VehicleTypeId = vehicleTypeId;
    }

    public void ChangeLicensePlate(string licensePlate)
    {
        ValidateRequired(licensePlate, nameof(licensePlate));
        LicensePlate = licensePlate.Trim();
    }

    public void UpdateCargoCapacity(decimal? maxCargoWeightKg, decimal? maxCargoVolumeM3)
    {
        ValidateOptionalNonNegative(maxCargoWeightKg, nameof(maxCargoWeightKg));
        ValidateOptionalNonNegative(maxCargoVolumeM3, nameof(maxCargoVolumeM3));

        MaxCargoWeightKg = maxCargoWeightKg;
        MaxCargoVolumeM3 = maxCargoVolumeM3;
    }

    public void UpdateImageUrls(IReadOnlyCollection<string>? imageUrls)
        => ImageUrls = imageUrls?.Select(url => url.Trim()).ToArray();

    public void ChangeStatus(VehicleStatus status) => Status = status;

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public void SoftDelete(DateTimeOffset deletedAt)
    {
        DeletedAt = deletedAt;
        IsActive = false;
    }

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }
    }

    private static void ValidateRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", parameterName);
        }
    }

    private static void ValidateSeatLayout(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new ArgumentException("Seat layout JSON is required.", nameof(value));
        }
    }

    private static void ValidatePositive(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be positive.");
        }
    }

    private static void ValidateOptionalNonNegative(decimal? value, string parameterName)
    {
        if (value < 0m)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value cannot be negative.");
        }
    }
}
