namespace VietRide.Trip.Application.Features.Stations.MergeStations;

public sealed record MergeStationsResponse(
    StationDto PrimaryStation,
    Guid DuplicateStationId,
    StationRelinkedCounts RelinkedCounts);
