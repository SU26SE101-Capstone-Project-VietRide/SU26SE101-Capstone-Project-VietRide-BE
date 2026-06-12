namespace VietRide.Payment.Application.Features.Wallets.BootstrapWallet;

public sealed record BootstrapWalletCommand(
    Guid UserId,
    string Role,
    string Email,
    DateTimeOffset CreatedAt);
