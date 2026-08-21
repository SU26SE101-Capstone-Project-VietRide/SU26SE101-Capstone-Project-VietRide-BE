using VietRide.Payment.Domain.Entities;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Payment.Application.Abstractions.Repositories;

public interface IParcelCompensationPayoutRepository
    : IRepository<ParcelCompensationPayout, Guid>
{
    Task<ParcelCompensationPayout?> FindByClaimIdAsync(Guid claimId, CancellationToken cancellationToken);
}
