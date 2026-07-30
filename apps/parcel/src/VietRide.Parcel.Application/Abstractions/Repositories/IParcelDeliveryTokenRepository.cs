using VietRide.Parcel.Domain.Entities;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Parcel.Application.Abstractions.Repositories;

public interface IParcelDeliveryTokenRepository : IRepository<ParcelDeliveryToken, Guid>
{
    Task<ParcelDeliveryToken?> FindByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken);

    Task<ParcelDeliveryToken?> FindActiveByParcelIdAsync(
        Guid parcelId,
        CancellationToken cancellationToken);

    Task<bool> RevokeActiveAsync(
        Guid parcelId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken);

    Task<bool> RevokeAsync(
        Guid tokenId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken);
}
