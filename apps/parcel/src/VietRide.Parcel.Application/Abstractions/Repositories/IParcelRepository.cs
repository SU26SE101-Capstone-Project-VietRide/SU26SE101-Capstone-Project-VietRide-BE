using VietRide.Shared.Application.Repositories;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.Application.Abstractions.Repositories;

public interface IParcelRepository : IRepository<ParcelEntity, Guid>
{
    Task<ParcelEntity?> FindByParcelCodeAsync(string parcelCode, CancellationToken ct = default);
}
