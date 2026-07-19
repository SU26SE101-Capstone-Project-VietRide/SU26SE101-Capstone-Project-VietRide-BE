using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Domain.Entities;

/// <summary>
/// Operator-owned route waypoint created from Google Places. Soft-delete is tracked by
/// <see cref="DeletedAt"/> only; <see cref="IsActive"/> is a separate activation flag.
/// </summary>
public sealed class Stop : BaseEntity<Guid>, ISoftDeletable, IActivatable
{
    public Guid OperatorId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid? LocationId { get; private set; }
    public decimal Latitude { get; private set; }
    public decimal Longitude { get; private set; }
    public string? Address { get; private set; }
    public string? GooglePlaceId { get; private set; }
    public bool SharedSuggestion { get; private set; }
    public Guid? ReplacedByStopId { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset? DeletedAt { get; private set; }

    private Stop() { }

    public static Stop Create(
        Guid operatorId,
        string name,
        decimal latitude,
        decimal longitude,
        string? description = null,
        string? address = null,
        string? googlePlaceId = null,
        Guid? locationId = null)
    {
        ValidateGuid(operatorId, nameof(operatorId));
        ValidateRequired(name, nameof(name));
        ValidateOptionalGuid(locationId, nameof(locationId));
        ValidateCoordinates(latitude, longitude);

        return new Stop
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorId,
            Name = name.Trim(),
            LocationId = locationId,
            Latitude = latitude,
            Longitude = longitude,
            Description = NormalizeOptional(description),
            Address = NormalizeOptional(address),
            GooglePlaceId = NormalizeOptional(googlePlaceId),
            SharedSuggestion = false,
            IsActive = true,
        };
    }

    public void UpdateDetails(
        string name,
        decimal latitude,
        decimal longitude,
        string? description,
        Guid? locationId,
        string? address,
        string? googlePlaceId)
    {
        ValidateRequired(name, nameof(name));
        ValidateOptionalGuid(locationId, nameof(locationId));
        ValidateCoordinates(latitude, longitude);

        Name = name.Trim();
        LocationId = locationId;
        Latitude = latitude;
        Longitude = longitude;
        Description = NormalizeOptional(description);
        Address = NormalizeOptional(address);
        GooglePlaceId = NormalizeOptional(googlePlaceId);
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public void SoftDelete(DateTimeOffset deletedAt)
    {
        DeletedAt = deletedAt;
        IsActive = false;
    }

    public void SoftDelete(DateTimeOffset deletedAt, Guid? replacedByStopId)
    {
        ValidateOptionalGuid(replacedByStopId, nameof(replacedByStopId));
        if (replacedByStopId == Id)
        {
            throw new ArgumentException("A stop cannot replace itself.", nameof(replacedByStopId));
        }

        ReplacedByStopId = replacedByStopId;
        SoftDelete(deletedAt);
    }

    /// <summary>Disables the stop without changing its soft-delete history.</summary>
    public void Disable(Guid? replacedByStopId)
    {
        ValidateOptionalGuid(replacedByStopId, nameof(replacedByStopId));
        if (replacedByStopId == Id)
        {
            throw new ArgumentException("A stop cannot replace itself.", nameof(replacedByStopId));
        }

        ReplacedByStopId = replacedByStopId;
        IsActive = false;
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidateRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", parameterName);
        }
    }

    private static void ValidateCoordinates(decimal latitude, decimal longitude)
    {
        if (latitude < -90m || latitude > 90m)
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), latitude, "Latitude must be between -90 and 90.");
        }

        if (longitude < -180m || longitude > 180m)
        {
            throw new ArgumentOutOfRangeException(nameof(longitude), longitude, "Longitude must be between -180 and 180.");
        }
    }

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
}
