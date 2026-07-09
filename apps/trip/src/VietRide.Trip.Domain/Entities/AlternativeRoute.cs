using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Domain.Entities;

/// <summary>
/// Operator-defined destination variant for a route. Stop sequence is separate from RouteStop.
/// </summary>
public sealed class AlternativeRoute : BaseEntity<Guid>, IActivatable
{
    public Guid RouteId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid DestinationStationId { get; private set; }
    public decimal? TotalDistanceKm { get; private set; }
    public int? EstimatedDurationMinutes { get; private set; }
    public string? PathPolyline { get; private set; }
    public bool IsActive { get; private set; } = true;

    private AlternativeRoute() { }

    public static AlternativeRoute Create(
        Guid routeId,
        string name,
        Guid destinationStationId,
        decimal? totalDistanceKm,
        int? estimatedDurationMinutes,
        string? description = null)
    {
        ValidateGuid(routeId, nameof(routeId));
        ValidateRequired(name, nameof(name));
        ValidateGuid(destinationStationId, nameof(destinationStationId));
        ValidateOptionalDistance(totalDistanceKm, nameof(totalDistanceKm));
        ValidateOptionalDuration(estimatedDurationMinutes, nameof(estimatedDurationMinutes));

        return new AlternativeRoute
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            Name = name.Trim(),
            Description = NormalizeOptional(description),
            DestinationStationId = destinationStationId,
            TotalDistanceKm = totalDistanceKm,
            EstimatedDurationMinutes = estimatedDurationMinutes,
            IsActive = true,
        };
    }

    public void UpdateDetails(
        string name,
        Guid destinationStationId,
        decimal? totalDistanceKm,
        int? estimatedDurationMinutes,
        string? description)
    {
        ValidateRequired(name, nameof(name));
        ValidateGuid(destinationStationId, nameof(destinationStationId));
        ValidateOptionalDistance(totalDistanceKm, nameof(totalDistanceKm));
        ValidateOptionalDuration(estimatedDurationMinutes, nameof(estimatedDurationMinutes));

        Name = name.Trim();
        Description = NormalizeOptional(description);
        DestinationStationId = destinationStationId;
        TotalDistanceKm = totalDistanceKm;
        EstimatedDurationMinutes = estimatedDurationMinutes;
    }

    public void Activate() => IsActive = true;

    public void SetPathGeometry(string? encodedPolyline) => PathPolyline = encodedPolyline;

    public void Deactivate() => IsActive = false;

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidateRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", parameterName);
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

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }
    }
}
