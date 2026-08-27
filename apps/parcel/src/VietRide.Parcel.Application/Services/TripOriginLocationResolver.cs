using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;

namespace VietRide.Parcel.Application.Services;

internal static class TripOriginLocationResolver
{
    public static async Task<(Guid Id, string Name)> ResolveAsync(
        ITripServiceClient trips,
        Guid tripId,
        CancellationToken cancellationToken)
    {
        var outcome = await trips.GetTripSummariesAsync([tripId], cancellationToken);
        var trip = outcome.Kind == TripSummaryBatchOutcomeKind.Success
            ? outcome.Summaries.FirstOrDefault(candidate => candidate.TripId == tripId)
            : null;

        if (trip is null
            || trip.Route.OriginStationId == Guid.Empty
            || string.IsNullOrWhiteSpace(trip.Route.OriginName))
        {
            throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                outcome.ErrorMessage ?? "Trip origin station is temporarily unavailable.");
        }

        return (trip.Route.OriginStationId, trip.Route.OriginName);
    }
}
