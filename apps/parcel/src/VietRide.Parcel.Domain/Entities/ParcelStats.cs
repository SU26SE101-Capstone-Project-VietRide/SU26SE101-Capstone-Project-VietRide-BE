using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Domain.Entities;

public sealed class ParcelStats : BaseEntity<Guid>
{
    public Guid OperatorId { get; private set; }
    public DateOnly StatDate { get; private set; }
    public int TotalParcels { get; private set; }
    public int TotalLoaded { get; private set; }
    public int TotalDelivered { get; private set; }
    public int TotalRejected { get; private set; }
    public int TotalReturned { get; private set; }
    public long TotalRevenue { get; private set; }
    public long TotalRefunded { get; private set; }

    private ParcelStats() { }

    public static ParcelStats Create(Guid operatorId, DateOnly statDate)
    {
        return new ParcelStats
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorId,
            StatDate = statDate,
        };
    }
}
