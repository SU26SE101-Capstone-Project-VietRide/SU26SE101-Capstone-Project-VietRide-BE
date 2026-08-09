using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Abstractions.Services;

public sealed record TripEtaStopInput(RouteStop RouteStop, Stop Stop);

public sealed record TripEtaPlan(
    PlannedEtaSource Source,
    DateTimeOffset DestinationArrivalTime,
    IReadOnlyDictionary<Guid, DateTimeOffset> StopArrivalTimes);

public interface ITripEtaPlanner
{
    Task<TripEtaPlan> PlanAsync(
        Route route,
        Station originStation,
        Station destinationStation,
        IReadOnlyList<TripEtaStopInput> stops,
        DateTimeOffset departureTime,
        CancellationToken cancellationToken = default);
}
