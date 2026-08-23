namespace VietRide.Trip.Api.Controllers.Requests;

public sealed class UpdateRouteRequest
{
    private Guid? returnRouteId;

    public string? Name { get; init; }

    public string? Code { get; init; }

    public Guid? ReturnRouteId
    {
        get => returnRouteId;
        init
        {
            returnRouteId = value;
            HasReturnRouteId = true;
        }
    }

    public bool HasReturnRouteId { get; private init; }

    public long? BaseFare { get; init; }

    public decimal? TotalDistanceKm { get; init; }

    public int? EstimatedDurationMinutes { get; init; }

    public bool? IsActive { get; init; }
}
