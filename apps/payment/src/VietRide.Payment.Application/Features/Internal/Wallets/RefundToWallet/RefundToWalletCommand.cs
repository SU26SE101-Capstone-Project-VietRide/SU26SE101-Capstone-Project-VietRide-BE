using MediatR;

namespace VietRide.Payment.Application.Features.Internal.Wallets.RefundToWallet;

public sealed record RefundToWalletCommand(
    Guid UserId,
    long Amount,
    string ReferenceType,
    Guid ReferenceId,
    string? IdempotencyKey,
    Guid? PaymentId = null) : IRequest<RefundToWalletResult>;
