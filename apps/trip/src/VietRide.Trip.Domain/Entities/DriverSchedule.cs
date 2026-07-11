using System.Text.Json;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Domain.Entities;

/// <summary>
/// Recurring driver assignment. Identity user references remain logical cross-service keys.
/// </summary>
public sealed class DriverSchedule : BaseEntity<Guid>, IActivatable
{
    public Guid OperatorId { get; private set; }
    public Guid RouteId { get; private set; }
    public Guid? VehicleId { get; private set; }
    public Guid DriverUserId { get; private set; }
    public Guid? AssistantUserId { get; private set; }
    public JsonElement DayOfWeek { get; private set; }
    public TimeOnly DepartureTime { get; private set; }
    public DateOnly ValidFrom { get; private set; }
    public DateOnly? ValidUntil { get; private set; }
    public bool IsActive { get; private set; } = true;

    private DriverSchedule() { }

    public static DriverSchedule Create(
        Guid operatorId,
        Guid routeId,
        Guid? vehicleId,
        Guid driverUserId,
        Guid? assistantUserId,
        JsonElement dayOfWeek,
        TimeOnly departureTime,
        DateOnly validFrom,
        DateOnly? validUntil,
        bool isActive)
    {
        ValidateGuid(operatorId, nameof(operatorId));
        ValidateGuid(routeId, nameof(routeId));
        ValidateOptionalGuid(vehicleId, nameof(vehicleId));
        ValidateGuid(driverUserId, nameof(driverUserId));
        ValidateOptionalGuid(assistantUserId, nameof(assistantUserId));
        ValidateDayOfWeek(dayOfWeek);
        ValidateDateRange(validFrom, validUntil);

        return new DriverSchedule
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorId,
            RouteId = routeId,
            VehicleId = vehicleId,
            DriverUserId = driverUserId,
            AssistantUserId = assistantUserId,
            DayOfWeek = dayOfWeek.Clone(),
            DepartureTime = departureTime,
            ValidFrom = validFrom,
            ValidUntil = validUntil,
            IsActive = isActive,
        };
    }

    public void AssignVehicle(Guid? vehicleId)
    {
        ValidateOptionalGuid(vehicleId, nameof(vehicleId));
        VehicleId = vehicleId;
    }

    public void ChangeCrew(Guid driverUserId, Guid? assistantUserId)
    {
        ValidateGuid(driverUserId, nameof(driverUserId));
        ValidateOptionalGuid(assistantUserId, nameof(assistantUserId));
        DriverUserId = driverUserId;
        AssistantUserId = assistantUserId;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }
    }

    private static void ValidateOptionalGuid(Guid? value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }
    }

    private static void ValidateDayOfWeek(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("Day of week must be a JSON array.", nameof(value));
        }

        var days = value.EnumerateArray();
        if (!days.MoveNext())
        {
            throw new ArgumentException("Day of week must contain at least one day.", nameof(value));
        }

        do
        {
            if (days.Current.ValueKind != JsonValueKind.Number
                || !days.Current.TryGetInt32(out var day)
                || day is < 1 or > 7)
            {
                throw new ArgumentException("Each day of week must be an integer from 1 through 7.", nameof(value));
            }
        }
        while (days.MoveNext());
    }

    private static void ValidateDateRange(DateOnly validFrom, DateOnly? validUntil)
    {
        if (validUntil < validFrom)
        {
            throw new ArgumentOutOfRangeException(nameof(validUntil), validUntil, "Valid until cannot precede valid from.");
        }
    }
}
