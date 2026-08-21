using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Domain.Entities;

public sealed class ParcelTransitLeg : BaseEntity<Guid>
{
    public Guid ParcelId { get; private set; }
    public Guid TripId { get; private set; }
    public Guid OperatorId { get; private set; }
    public int Sequence { get; private set; }
    public Guid? ExpectedOriginId { get; private set; }
    public Guid? ExpectedDestinationId { get; private set; }
    public string? ExpectedOriginName { get; private set; }
    public string? ExpectedDestinationName { get; private set; }
    public Guid? ActualOriginId { get; private set; }
    public Guid? ActualDestinationId { get; private set; }
    public Guid? VehicleId { get; private set; }
    public string? VehicleLicensePlate { get; private set; }
    public ParcelTransitLegStatus Status { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }

    private ParcelTransitLeg()
    {
    }

    public static ParcelTransitLeg Create(
        Guid parcelId,
        Guid tripId,
        Guid operatorId,
        int sequence,
        Guid? expectedOriginId,
        Guid? expectedDestinationId,
        string? expectedOriginName,
        string? expectedDestinationName,
        Guid? vehicleId,
        string? vehicleLicensePlate)
    {
        if (parcelId == Guid.Empty || tripId == Guid.Empty || operatorId == Guid.Empty)
            throw new ArgumentException("Parcel, trip and operator ids are required.");
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence));

        return new ParcelTransitLeg
        {
            Id = Guid.NewGuid(),
            ParcelId = parcelId,
            TripId = tripId,
            OperatorId = operatorId,
            Sequence = sequence,
            ExpectedOriginId = expectedOriginId,
            ExpectedDestinationId = expectedDestinationId,
            ExpectedOriginName = Normalize(expectedOriginName),
            ExpectedDestinationName = Normalize(expectedDestinationName),
            VehicleId = vehicleId,
            VehicleLicensePlate = Normalize(vehicleLicensePlate),
            Status = ParcelTransitLegStatus.PLANNED,
        };
    }

    public void Start(DateTimeOffset at)
    {
        if (Status is not (ParcelTransitLegStatus.PLANNED or ParcelTransitLegStatus.ACTIVE))
            throw new InvalidOperationException("Only a planned leg can be started.");

        Status = ParcelTransitLegStatus.ACTIVE;
        StartedAt ??= at;
    }

    public void Complete(Guid? actualDestinationId, DateTimeOffset at)
    {
        if (Status is ParcelTransitLegStatus.COMPLETED or ParcelTransitLegStatus.LOST)
            return;

        ActualDestinationId = actualDestinationId;
        Status = ParcelTransitLegStatus.COMPLETED;
        EndedAt = at;
    }

    public void MarkForwarded(DateTimeOffset at)
    {
        Status = ParcelTransitLegStatus.FORWARDED;
        EndedAt = at;
    }

    public void MarkReturned(DateTimeOffset at)
    {
        Status = ParcelTransitLegStatus.RETURNED;
        EndedAt = at;
    }

    public void MarkLost(DateTimeOffset at)
    {
        Status = ParcelTransitLegStatus.LOST;
        EndedAt = at;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
