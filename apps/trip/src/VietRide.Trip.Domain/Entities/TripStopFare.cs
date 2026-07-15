using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Trip.Domain.Entities;

/// <summary>
/// Per-trip fare override from a specific stop.
/// </summary>
public sealed class TripStopFare : BaseEntity<Guid>
{
    public Guid TripId { get; private set; }
    public Guid StopId { get; private set; }
    public Money FareFromThisStop { get; private set; }
    public TripStopFareSource Source { get; private set; }

    private TripStopFare() { }

    public static TripStopFare Create(
        Guid tripId,
        Guid stopId,
        Money fareFromThisStop,
        TripStopFareSource source = TripStopFareSource.TEMPLATE_SNAPSHOT)
    {
        ValidateGuid(tripId, nameof(tripId));
        ValidateGuid(stopId, nameof(stopId));

        return new TripStopFare
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            StopId = stopId,
            FareFromThisStop = fareFromThisStop,
            Source = source,
        };
    }

    public void ChangeFare(Money fareFromThisStop, TripStopFareSource source)
    {
        FareFromThisStop = fareFromThisStop;
        Source = source;
    }

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }
    }
}
