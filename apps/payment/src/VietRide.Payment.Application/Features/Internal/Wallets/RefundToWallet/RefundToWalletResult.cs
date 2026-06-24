namespace VietRide.Payment.Application.Features.Internal.Wallets.RefundToWallet;

public sealed record RefundToWalletResult(Guid WalletTransactionId, long BalanceAfter);
