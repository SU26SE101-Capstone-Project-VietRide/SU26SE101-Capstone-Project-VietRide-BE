namespace VietRide.Trip.Application.Features.Internal.Trips.BatchTripSummaries;

public sealed record InternalTripRouteSummaryDto(
    Guid RouteId,
    string Name,
    string OriginName,
    string DestinationName)
{
    public Guid OriginStationId { get; init; }

    public Guid DestinationStationId { get; init; }
}
