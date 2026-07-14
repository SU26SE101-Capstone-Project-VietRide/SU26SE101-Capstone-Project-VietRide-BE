using VietRide.Payment.Domain.Entities;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Payment.Application.Abstractions.Repositories;

public interface IOperatorWalletTransactionRepository : IRepository<OperatorWalletTransaction, Guid>
{
}
