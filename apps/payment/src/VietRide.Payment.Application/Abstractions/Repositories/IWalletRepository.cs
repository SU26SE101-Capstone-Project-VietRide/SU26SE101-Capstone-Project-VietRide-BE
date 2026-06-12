using VietRide.Payment.Domain.Entities;
using VietRide.Shared.Application.Repositories;

namespace VietRide.Payment.Application.Abstractions.Repositories;

public interface IWalletRepository : IRepository<Wallet, Guid>
{
    /// <summary>
    /// Creates the zero-balance VND wallet for a user if it does not exist yet.
    /// Returns <c>true</c> only when a row was inserted; re-delivery returns <c>false</c>.
    /// </summary>
    Task<bool> EnsureBootstrapWalletAsync(Guid userId, CancellationToken cancellationToken);
}
