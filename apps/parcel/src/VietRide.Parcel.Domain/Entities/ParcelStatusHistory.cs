using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Domain.Entities;

public sealed class ParcelStatusHistory
{
    private ParcelStatusHistory() { }

    public Guid Id { get; private set; }
    public Guid ParcelId { get; private set; }
    public ParcelStatus Status { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string ActorType { get; private set; } = null!;
    public Guid? ActorId { get; private set; }
    public string Source { get; private set; } = null!;
    public string? Reason { get; private set; }
}
