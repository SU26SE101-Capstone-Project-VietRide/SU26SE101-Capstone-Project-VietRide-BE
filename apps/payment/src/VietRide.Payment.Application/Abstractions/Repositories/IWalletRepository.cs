using VietRide.Payment.Application.Features.Wallets.GetWalletTransactions;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Repositories;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Application.Abstractions.Repositories;

public interface IWalletRepository : IRepository<Wallet, Guid>
{
    /// <summary>
    /// Creates the zero-balance VND wallet for a user if it does not exist yet.
    /// Returns <c>true</c> only when a row was inserted; re-delivery returns <c>false</c>.
    /// </summary>
    Task<bool> EnsureBootstrapWalletAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the authenticated user's wallet without tracking.
    /// </summary>
    Task<Wallet?> GetUserWalletAsync(Guid userId, CancellationToken cancellationToken)
        => throw new NotSupportedException("This wallet repository does not support wallet reads.");

    /// <summary>
    /// Reads the authenticated user's wallet ledger newest-first with optional filters.
    /// </summary>
    Task<PagedResult<GetWalletTransactionResult>> GetUserWalletTransactionsAsync(
        Guid userId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        WalletTransactionType? type,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("This wallet repository does not support wallet transaction reads.");

    /// <summary>
    /// Atomically credits the wallet and inserts an immutable ledger row in the ambient transaction.
    /// </summary>
    Task<WalletTransaction> CreditTopUpAsync(
        Guid userId,
        Money amount,
        Guid topUpRequestId,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("This wallet repository does not support top-up crediting.");

    Task AcquireWalletTransactionReferenceLockAsync(
        WalletTransactionRef referenceType,
        Guid referenceId,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("This wallet repository does not support wallet transaction reference locks.");

    Task<WalletTransaction?> FindTransactionByReferenceAsync(
        WalletTransactionRef referenceType,
        Guid referenceId,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("This wallet repository does not support wallet transaction reference lookups.");

    Task<WalletTransaction?> FindTransactionByIdAsync(
        Guid transactionId,
        CancellationToken cancellationToken)
        => Task.FromResult<WalletTransaction?>(null);

    Task<long> GetTotalRefundedByReferenceAsync(
        WalletTransactionRef referenceType,
        Guid referenceId,
        CancellationToken cancellationToken)
        => Task.FromResult(0L);

    Task<long> GetTotalRefundedByReferenceAndUserAsync(
        WalletTransactionRef referenceType,
        Guid referenceId,
        Guid userId,
        CancellationToken cancellationToken)
        => GetTotalRefundedByReferenceAsync(referenceType, referenceId, cancellationToken);

    Task<IReadOnlyList<WalletTransaction>> ListRefundTransactionsByReferenceAsync(
        WalletTransactionRef referenceType,
        Guid referenceId,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<WalletTransaction>>([]);

    Task<WalletTransaction> CreditRefundAsync(
        Guid userId,
        Money amount,
        WalletTransactionRef referenceType,
        Guid referenceId,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("This wallet repository does not support refund crediting.");

    Task<WalletTransaction> CreditBookingRefundAsync(
        Guid userId,
        Money amount,
        Guid bookingId,
        CancellationToken cancellationToken)
        => CreditRefundAsync(userId, amount, WalletTransactionRef.BOOKING_REFUND, bookingId, cancellationToken);

    Task<WalletTransaction> CreditBookingRefundAsync(
        Guid userId,
        Money amount,
        Guid bookingId,
        Guid transactionId,
        CancellationToken cancellationToken)
        => CreditBookingRefundAsync(userId, amount, bookingId, cancellationToken);
}
