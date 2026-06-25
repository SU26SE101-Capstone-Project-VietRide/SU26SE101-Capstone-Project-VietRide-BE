using VietRide.Payment.Application.Features.Internal.Wallets.RefundToWallet;

namespace VietRide.Payment.Api.Controllers.Requests;

public sealed record RefundToWalletRequest(
    Guid UserId,
    long Amount,
    string ReferenceType,
    Guid ReferenceId)
{
    public RefundToWalletCommand ToCommand(string? idempotencyKey)
        => new(UserId, Amount, ReferenceType, ReferenceId, idempotencyKey);
}
