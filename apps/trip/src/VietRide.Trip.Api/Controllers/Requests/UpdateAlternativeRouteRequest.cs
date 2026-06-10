namespace VietRide.Trip.Api.Controllers.Requests;

public sealed class UpdateAlternativeRouteRequest
{
    private string? name;
    private string? description;
    private Guid? destinationStationId;
    private decimal? totalDistanceKm;
    private int? estimatedDurationMinutes;
    private IReadOnlyList<AlternativeRouteStopRequest>? stops;

    public string? Name
    {
        get => name;
        init
        {
            name = value;
            HasName = true;
        }
    }

    public bool HasName { get; private init; }

    public string? Description
    {
        get => description;
        init
        {
            description = value;
            HasDescription = true;
        }
    }

    public bool HasDescription { get; private init; }

    public Guid? DestinationStationId
    {
        get => destinationStationId;
        init
        {
            destinationStationId = value;
            HasDestinationStationId = true;
        }
    }

    public bool HasDestinationStationId { get; private init; }

    public decimal? TotalDistanceKm
    {
        get => totalDistanceKm;
        init
        {
            totalDistanceKm = value;
            HasTotalDistanceKm = true;
        }
    }

    public bool HasTotalDistanceKm { get; private init; }

    public int? EstimatedDurationMinutes
    {
        get => estimatedDurationMinutes;
        init
        {
            estimatedDurationMinutes = value;
            HasEstimatedDurationMinutes = true;
        }
    }

    public bool HasEstimatedDurationMinutes { get; private init; }

    public bool? IsActive { get; init; }

    public IReadOnlyList<AlternativeRouteStopRequest>? Stops
    {
        get => stops;
        init
        {
            stops = value;
            HasStops = true;
        }
    }

    public bool HasStops { get; private init; }
}
