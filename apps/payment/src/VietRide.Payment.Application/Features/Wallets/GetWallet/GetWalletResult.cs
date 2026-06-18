namespace VietRide.Payment.Application.Features.Wallets.GetWallet;

public sealed record GetWalletResult(
    Guid UserId,
    long Balance,
    string Currency);
