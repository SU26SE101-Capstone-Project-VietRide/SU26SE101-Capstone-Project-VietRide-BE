using VietRide.Payment.Domain.Entities;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Payment.Application.Abstractions.Repositories;

public interface IOperatorTripSettlementRepository : IRepository<OperatorTripSettlement, Guid>
{
    Task<OperatorTripSettlement?> FindByOperatorTripAsync(Guid operatorId, Guid tripId, CancellationToken cancellationToken);
    Task<OperatorTripSettlement?> GetForUpdateAsync(Guid settlementId, CancellationToken cancellationToken)
        => GetByIdAsync(settlementId, cancellationToken);
}
