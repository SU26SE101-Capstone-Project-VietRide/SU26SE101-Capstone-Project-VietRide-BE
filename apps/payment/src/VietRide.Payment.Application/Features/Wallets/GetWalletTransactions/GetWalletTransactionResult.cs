namespace VietRide.Payment.Application.Features.Wallets.GetWalletTransactions;

public sealed record GetWalletTransactionResult(
    Guid Id,
    string Type,
    long Amount,
    long BalanceBefore,
    long BalanceAfter,
    string ReferenceType,
    Guid? ReferenceId,
    string? Note,
    DateTimeOffset CreatedAt);
