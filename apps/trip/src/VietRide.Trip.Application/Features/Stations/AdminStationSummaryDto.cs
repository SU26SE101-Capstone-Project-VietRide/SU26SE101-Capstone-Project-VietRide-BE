namespace VietRide.Trip.Application.Features.Stations;

public sealed record AdminStationSummaryDto(
    long Total,
    long Active,
    long Inactive,
    long SupportsShuttle);
