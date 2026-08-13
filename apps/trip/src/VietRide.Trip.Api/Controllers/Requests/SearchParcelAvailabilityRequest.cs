namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record SearchParcelAvailabilityRequest(
    Guid OriginStationId,
    Guid DestinationStationId,
    DateOnly DepartureDate,
    decimal EstimatedWeightKg,
    decimal EstimatedVolumeM3,
    string SizeCategory,
    IReadOnlyCollection<Guid> EligibleRouteIds,
    int Page = 1,
    int PageSize = 20);
