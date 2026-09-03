using VietRide.Payment.Domain.Entities;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Payment.Application.Abstractions.Repositories;

public interface IOperatorWalletTransactionRepository : IRepository<OperatorWalletTransaction, Guid>
{
    Task<OperatorWalletTransaction?> FindByReferenceAsync(
        Guid operatorId,
        Domain.Enums.OperatorWalletTransactionRef referenceType,
        Guid referenceId,
        CancellationToken cancellationToken)
        => Task.FromResult<OperatorWalletTransaction?>(null);
}
