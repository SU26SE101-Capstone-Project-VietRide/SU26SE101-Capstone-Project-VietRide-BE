using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Repositories;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Application.Abstractions.Repositories;

public interface IPlatformWalletRepository : IRepository<PlatformWallet, Guid>
{
    Task<PlatformWallet> GetSingletonAsync(CancellationToken cancellationToken);

    Task<PlatformWalletTransaction> CreditAsync(
        Money amount,
        PlatformWalletTransactionRef referenceType,
        Guid? referenceId,
        string? note,
        CancellationToken cancellationToken);

    Task<PlatformWalletTransaction> DebitAsync(
        Money amount,
        PlatformWalletTransactionRef referenceType,
        Guid? referenceId,
        string? note,
        CancellationToken cancellationToken);
}
