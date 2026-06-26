using VietRide.Parcel.Domain.Entities;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Parcel.Application.Abstractions.Repositories;

public interface IParcelStatsRepository : IRepository<ParcelStats, Guid>
{
}
