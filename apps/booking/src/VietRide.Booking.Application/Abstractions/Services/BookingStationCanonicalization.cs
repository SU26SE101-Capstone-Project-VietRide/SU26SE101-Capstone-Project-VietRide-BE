using VietRide.Booking.Application.Abstractions.ServiceClients;

namespace VietRide.Booking.Application.Abstractions.Services;

public static class BookingStationCanonicalization
{
    public static IReadOnlyCollection<Guid> Collect(params Guid?[] stationIds)
        => stationIds
            .Where(stationId => stationId.HasValue && stationId.Value != Guid.Empty)
            .Select(stationId => stationId!.Value)
            .Distinct()
            .ToArray();

    public static TripSnapshot ResolveTrip(
        TripSnapshot trip,
        StationCanonicalizationResult canonicalization)
        => trip with
        {
            OriginStation = trip.OriginStation with
            {
                Id = canonicalization.Resolve(trip.OriginStation.Id),
            },
            DestinationStation = trip.DestinationStation with
            {
                Id = canonicalization.Resolve(trip.DestinationStation.Id),
            },
        };
}
