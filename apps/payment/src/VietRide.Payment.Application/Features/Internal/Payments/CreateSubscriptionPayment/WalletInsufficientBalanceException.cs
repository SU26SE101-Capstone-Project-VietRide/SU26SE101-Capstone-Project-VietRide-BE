using VietRide.Shared.Application.Exceptions;

namespace VietRide.Payment.Application.Features.Internal.Payments.CreateSubscriptionPayment;

public sealed class WalletInsufficientBalanceException : Exception, ICodedHttpException
{
    public int StatusCode => 402;
    public string ErrorCode => "WALLET_INSUFFICIENT_BALANCE";

    public WalletInsufficientBalanceException()
        : base("Operator wallet has insufficient balance for this subscription payment.")
    {
    }
}
