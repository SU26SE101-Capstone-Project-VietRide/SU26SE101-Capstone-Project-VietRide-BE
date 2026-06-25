using VietRide.Shared.Application.Exceptions;

namespace VietRide.Payment.Application.Exceptions;

public sealed class PlatformWalletInsufficientBalanceException : Exception, ICodedHttpException
{
    public const string Code = "PLATFORM_WALLET_INSUFFICIENT_BALANCE";

    public PlatformWalletInsufficientBalanceException(string message)
        : base(message)
    {
    }

    public string ErrorCode => Code;

    public int StatusCode => 500;
}
