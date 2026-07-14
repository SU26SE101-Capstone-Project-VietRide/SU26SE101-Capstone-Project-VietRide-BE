using VietRide.Identity.Domain.Entities;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Identity.Application.Abstractions.Repositories;

public interface IOperatorWalletBackfillMarkerRepository
    : IRepository<OperatorWalletBackfillMarker, Guid>
{
    Task<OperatorWalletBackfillMarker?> FindByOperatorIdAsync(
        Guid operatorId,
        CancellationToken cancellationToken);
}
