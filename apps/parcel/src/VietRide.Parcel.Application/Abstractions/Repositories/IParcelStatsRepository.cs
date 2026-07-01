using VietRide.Parcel.Domain.Entities;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Parcel.Application.Abstractions.Repositories;

public interface IParcelStatsRepository : IRepository<ParcelStats, Guid>
{
    Task UpsertIncrementAsync(
        Guid operatorId,
        DateOnly statDate,
        int totalParcels,
        int totalLoaded,
        int totalDelivered,
        int totalRejected,
        int totalReturned,
        long totalRevenue,
        long totalRefunded,
        CancellationToken ct);
}
