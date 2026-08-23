using System.Text.RegularExpressions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Trip.Domain.Entities;

/// <summary>
/// Operator-owned station-to-station route. OperatorId is a logical FK to Identity.
/// Soft-delete is tracked by DeletedAt; IsActive is a separate activation flag.
/// </summary>
public sealed class Route : BaseEntity<Guid>, ISoftDeletable, IActivatable
{
    private static readonly Regex CodePattern = new("^[A-Z0-9][A-Z0-9-]{1,19}$", RegexOptions.CultureInvariant);

    public Guid OperatorId { get; private set; }
    public string? Code { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public Guid OriginStationId { get; private set; }
    public Guid DestinationStationId { get; private set; }
    public Guid? ReturnRouteId { get; private set; }
    public Money BaseFare { get; private set; }
    public decimal? TotalDistanceKm { get; private set; }
    public int? EstimatedDurationMinutes { get; private set; }
    public string? PathPolyline { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset? DeletedAt { get; private set; }

    private Route() { }

    public static Route Create(
        Guid operatorId,
        string name,
        Guid originStationId,
        Guid destinationStationId,
        Money baseFare,
        decimal? totalDistanceKm,
        int? estimatedDurationMinutes,
        Guid? returnRouteId = null,
        string? code = null)
    {
        ValidateGuid(operatorId, nameof(operatorId));
        var normalizedName = ValidateName(name);
        ValidateGuid(originStationId, nameof(originStationId));
        ValidateGuid(destinationStationId, nameof(destinationStationId));
        ValidateDifferentStations(originStationId, destinationStationId);
        ValidateOptionalDistance(totalDistanceKm, nameof(totalDistanceKm));
        ValidateOptionalDuration(estimatedDurationMinutes, nameof(estimatedDurationMinutes));
        ValidateOptionalGuid(returnRouteId, nameof(returnRouteId));

        return new Route
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorId,
            Code = NormalizeCode(code),
            Name = normalizedName,
            OriginStationId = originStationId,
            DestinationStationId = destinationStationId,
            ReturnRouteId = returnRouteId,
            BaseFare = baseFare,
            TotalDistanceKm = totalDistanceKm,
            EstimatedDurationMinutes = estimatedDurationMinutes,
            IsActive = true,
        };
    }

    public void UpdateDetails(
        string name,
        Guid originStationId,
        Guid destinationStationId,
        Money baseFare,
        decimal? totalDistanceKm,
        int? estimatedDurationMinutes,
        Guid? returnRouteId)
    {
        var normalizedName = ValidateName(name);
        ValidateGuid(originStationId, nameof(originStationId));
        ValidateGuid(destinationStationId, nameof(destinationStationId));
        ValidateDifferentStations(originStationId, destinationStationId);
        ValidateOptionalDistance(totalDistanceKm, nameof(totalDistanceKm));
        ValidateOptionalDuration(estimatedDurationMinutes, nameof(estimatedDurationMinutes));
        ValidateOptionalGuid(returnRouteId, nameof(returnRouteId));

        Name = normalizedName;
        OriginStationId = originStationId;
        DestinationStationId = destinationStationId;
        BaseFare = baseFare;
        TotalDistanceKm = totalDistanceKm;
        EstimatedDurationMinutes = estimatedDurationMinutes;
        ReturnRouteId = returnRouteId;
    }

    public void SetReturnRoute(Guid? returnRouteId)
    {
        ValidateOptionalGuid(returnRouteId, nameof(returnRouteId));
        ReturnRouteId = returnRouteId;
    }

    public void SetCode(string code) => Code = NormalizeCode(code);

    public void SetPathGeometry(string? encodedPolyline) => PathPolyline = encodedPolyline;

    public void SetMetrics(decimal? totalDistanceKm, int? estimatedDurationMinutes)
    {
        ValidateOptionalDistance(totalDistanceKm, nameof(totalDistanceKm));
        ValidateOptionalDuration(estimatedDurationMinutes, nameof(estimatedDurationMinutes));
        TotalDistanceKm = totalDistanceKm;
        EstimatedDurationMinutes = estimatedDurationMinutes;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public void SoftDelete(DateTimeOffset deletedAt)
    {
        DeletedAt = deletedAt;
        IsActive = false;
    }

    public (bool OriginChanged, bool DestinationChanged) RelinkStation(
        Guid duplicateStationId,
        Guid primaryStationId)
    {
        ValidateGuid(duplicateStationId, nameof(duplicateStationId));
        ValidateGuid(primaryStationId, nameof(primaryStationId));
        if (duplicateStationId == primaryStationId)
            throw new ArgumentException("Station merge IDs must be different.", nameof(primaryStationId));

        var originChanged = OriginStationId == duplicateStationId;
        var destinationChanged = DestinationStationId == duplicateStationId;
        var newOrigin = originChanged ? primaryStationId : OriginStationId;
        var newDestination = destinationChanged ? primaryStationId : DestinationStationId;
        ValidateDifferentStations(newOrigin, newDestination);
        OriginStationId = newOrigin;
        DestinationStationId = newDestination;
        return (originChanged, destinationChanged);
    }

    private static string ValidateName(string name)
    {
        var normalizedName = name?.Trim() ?? string.Empty;
        if (normalizedName.Length == 0)
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        if (normalizedName.Length > 255)
        {
            throw new ArgumentException("Name cannot exceed 255 characters.", nameof(name));
        }

        return normalizedName;
    }

    private static string? NormalizeCode(string? code)
    {
        if (code is null)
        {
            return null;
        }

        var normalized = code.Trim().ToUpperInvariant();
        if (!CodePattern.IsMatch(normalized))
        {
            throw new ArgumentException("Route code must contain 2 to 20 uppercase letters, digits, or hyphens.", nameof(code));
        }

        return normalized;
    }

    private static void ValidateDifferentStations(Guid originStationId, Guid destinationStationId)
    {
        if (originStationId == destinationStationId)
        {
            throw new ArgumentException("Origin and destination stations must be different.", nameof(destinationStationId));
        }
    }

    private static void ValidateOptionalDistance(decimal? distanceKm, string parameterName)
    {
        if (distanceKm < 0m)
        {
            throw new ArgumentOutOfRangeException(parameterName, distanceKm, "Distance cannot be negative.");
        }
    }

    private static void ValidateOptionalDuration(int? minutes, string parameterName)
    {
        if (minutes < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, minutes, "Duration cannot be negative.");
        }
    }

    private static void ValidateOptionalGuid(Guid? value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }
    }

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }
    }
}
