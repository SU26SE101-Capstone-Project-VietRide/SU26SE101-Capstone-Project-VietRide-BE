using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Repositories;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Application.Abstractions.Repositories;

public sealed record PlatformWalletTransactionLinkInput(
    PlatformWalletTransactionLinkType LinkType,
    long AllocatedAmount,
    Guid? OperatorId = null,
    Guid? TripId = null,
    Guid? ReferenceId = null,
    string? ReferenceCode = null);

public interface IPlatformWalletRepository : IRepository<PlatformWallet, Guid>
{
    Task<PlatformWalletTransaction?> FindTransactionByReferenceAsync(
        PlatformWalletTransactionRef referenceType,
        Guid referenceId,
        CancellationToken cancellationToken)
        => Task.FromResult<PlatformWalletTransaction?>(null);

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

    Task<PlatformWalletTransaction> CreditWithLinksAsync(
        Money amount,
        PlatformWalletTransactionRef referenceType,
        Guid? referenceId,
        string? note,
        IReadOnlyCollection<PlatformWalletTransactionLinkInput> links,
        CancellationToken cancellationToken)
        => CreditAsync(amount, referenceType, referenceId, note, cancellationToken);

    Task<PlatformWalletTransaction> DebitWithLinksAsync(
        Money amount,
        PlatformWalletTransactionRef referenceType,
        Guid? referenceId,
        string? note,
        IReadOnlyCollection<PlatformWalletTransactionLinkInput> links,
        CancellationToken cancellationToken)
        => DebitAsync(amount, referenceType, referenceId, note, cancellationToken);
}
