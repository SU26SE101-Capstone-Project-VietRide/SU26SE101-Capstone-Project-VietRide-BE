using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Domain.Entities;

/// <summary>
/// Vehicle category available to operators. System-defined rows are protected by application policy.
/// </summary>
public sealed class VehicleType : BaseEntity<Guid>, IActivatable
{
    public string Code { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public int? EstimatedPassengerLuggageKgPerSeat { get; private set; }
    public int? DefaultSeatCount { get; private set; }
    public bool IsSystemDefined { get; private set; }
    public bool IsActive { get; private set; } = true;

    private VehicleType() { }

    public static VehicleType Create(
        string code,
        string displayName,
        int? estimatedPassengerLuggageKgPerSeat,
        int? defaultSeatCount,
        bool isSystemDefined = false)
    {
        ValidateRequired(code, nameof(code));
        ValidateRequired(displayName, nameof(displayName));
        ValidateOptionalNonNegative(estimatedPassengerLuggageKgPerSeat, nameof(estimatedPassengerLuggageKgPerSeat));
        ValidateOptionalPositive(defaultSeatCount, nameof(defaultSeatCount));

        return new VehicleType
        {
            Id = Guid.NewGuid(),
            Code = code.Trim(),
            DisplayName = displayName.Trim(),
            EstimatedPassengerLuggageKgPerSeat = estimatedPassengerLuggageKgPerSeat,
            DefaultSeatCount = defaultSeatCount,
            IsSystemDefined = isSystemDefined,
            IsActive = true,
        };
    }

    public void UpdateDetails(
        string displayName,
        int? estimatedPassengerLuggageKgPerSeat,
        int? defaultSeatCount)
    {
        ValidateRequired(displayName, nameof(displayName));
        ValidateOptionalNonNegative(estimatedPassengerLuggageKgPerSeat, nameof(estimatedPassengerLuggageKgPerSeat));
        ValidateOptionalPositive(defaultSeatCount, nameof(defaultSeatCount));

        DisplayName = displayName.Trim();
        EstimatedPassengerLuggageKgPerSeat = estimatedPassengerLuggageKgPerSeat;
        DefaultSeatCount = defaultSeatCount;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    private static void ValidateRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", parameterName);
        }
    }

    private static void ValidateOptionalNonNegative(int? value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value cannot be negative.");
        }
    }

    private static void ValidateOptionalPositive(int? value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Value must be positive.");
        }
    }
}
