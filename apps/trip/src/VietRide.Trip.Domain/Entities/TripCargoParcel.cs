using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Domain.Entities;

public sealed class TripCargoParcel : BaseEntity<Guid>
{
    public const string ReservedState = "RESERVED";
    public const string LoadedState = "LOADED";
    public const string ReleasedState = "RELEASED";

    public Guid TripId { get; private set; }
    public Guid ParcelId { get; private set; }
    public decimal WeightKg { get; private set; }
    public decimal VolumeM3 { get; private set; }
    public decimal? ActualWeightKg { get; private set; }
    public decimal? ActualVolumeM3 { get; private set; }
    public string State { get; private set; } = ReservedState;
    public DateTimeOffset? LoadedAt { get; private set; }
    public DateTimeOffset? ReleasedAt { get; private set; }

    private TripCargoParcel() { }

    public static TripCargoParcel Reserve(Guid tripId, Guid parcelId, decimal weightKg, decimal volumeM3)
    {
        ValidateGuid(tripId, nameof(tripId));
        ValidateGuid(parcelId, nameof(parcelId));
        ValidatePositiveWeight(weightKg);
        ValidatePositiveVolume(volumeM3);

        return new TripCargoParcel
        {
            Id = Guid.NewGuid(),
            TripId = tripId,
            ParcelId = parcelId,
            WeightKg = weightKg,
            VolumeM3 = volumeM3,
            State = ReservedState,
        };
    }

    public (decimal PreviousWeightKg, decimal PreviousVolumeM3) Remeasure(decimal actualWeightKg, decimal actualVolumeM3)
    {
        if (State != ReservedState)
        {
            throw new InvalidOperationException("Only reserved cargo can be remeasured.");
        }

        ValidatePositiveWeight(actualWeightKg);
        ValidatePositiveVolume(actualVolumeM3);

        var previous = (WeightKg, VolumeM3);
        WeightKg = actualWeightKg;
        VolumeM3 = actualVolumeM3;
        ActualWeightKg = actualWeightKg;
        ActualVolumeM3 = actualVolumeM3;
        return previous;
    }

    public void MarkLoaded(DateTimeOffset now)
    {
        if (State == LoadedState)
        {
            return;
        }

        if (State != ReservedState)
        {
            throw new InvalidOperationException("Only reserved cargo can be loaded.");
        }

        State = LoadedState;
        LoadedAt = now;
    }

    public string Release(DateTimeOffset now)
    {
        if (State == ReleasedState)
        {
            return ReleasedState;
        }

        var previousState = State;
        State = ReleasedState;
        ReleasedAt = now;
        return previousState;
    }

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }
    }

    private static void ValidatePositiveWeight(decimal value)
    {
        if (value <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Weight must be positive.");
        }
    }

    private static void ValidatePositiveVolume(decimal value)
    {
        if (value <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Volume must be positive.");
        }
    }
}
