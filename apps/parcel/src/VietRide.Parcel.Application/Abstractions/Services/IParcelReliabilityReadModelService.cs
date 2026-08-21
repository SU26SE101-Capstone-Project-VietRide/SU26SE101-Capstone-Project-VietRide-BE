using VietRide.Parcel.Application.Features.Reliability.ReadModels;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.Application.Abstractions.Services;

public interface IParcelReliabilityReadModelService
{
    Task<IReadOnlyDictionary<Guid, ParcelScreenReadModel>> BuildAsync(
        IReadOnlyCollection<ParcelEntity> parcels,
        Guid? viewerUserId,
        bool includeClaim,
        CancellationToken cancellationToken = default);
}
