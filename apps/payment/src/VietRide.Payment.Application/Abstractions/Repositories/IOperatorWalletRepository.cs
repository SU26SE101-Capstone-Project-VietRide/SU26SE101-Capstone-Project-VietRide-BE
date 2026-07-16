using VietRide.Payment.Domain.Entities;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Payment.Application.Abstractions.Repositories;

public interface IOperatorWalletRepository : IRepository<OperatorWallet, Guid>
{
    Task<OperatorWallet?> FindByOperatorIdAsync(Guid operatorId, CancellationToken cancellationToken);
}
