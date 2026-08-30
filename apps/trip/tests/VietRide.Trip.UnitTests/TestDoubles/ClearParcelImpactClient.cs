using VietRide.Trip.Application.Abstractions.ExternalClients;

namespace VietRide.Trip.UnitTests.TestDoubles;

internal sealed class ClearParcelImpactClient : IParcelImpactClient
{
    public Task<ParcelTripCompletionClearanceProjection> GetTripCompletionClearanceAsync(
        Guid tripId,
        Guid operatorId,
        CancellationToken cancellationToken)
        => Task.FromResult(new ParcelTripCompletionClearanceProjection(
            tripId,
            operatorId,
            "CLEAR",
            [],
            []));

    public Task<ParcelStopDepartureClearanceProjection> GetStopDepartureClearanceAsync(
        Guid tripId,
        Guid stopId,
        Guid operatorId,
        CancellationToken cancellationToken)
        => Task.FromResult(new ParcelStopDepartureClearanceProjection(
            tripId,
            stopId,
            operatorId,
            "CLEAR",
            [],
            null,
            null,
            null));

    public Task<TripParcelCancellationImpactProjection> GetTripCancellationImpactAsync(
        Guid tripId,
        Guid operatorId,
        CancellationToken cancellationToken)
        => Task.FromResult(new TripParcelCancellationImpactProjection(tripId, []));
}
