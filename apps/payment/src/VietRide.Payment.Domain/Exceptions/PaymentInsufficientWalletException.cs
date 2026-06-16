using VietRide.Shared.Kernel.Exceptions;

namespace VietRide.Payment.Domain.Exceptions;

public sealed class PaymentInsufficientWalletException : DomainException
{
    public PaymentInsufficientWalletException(string message)
        : base("PAYMENT_INSUFFICIENT_WALLET", message)
    {
    }
}
