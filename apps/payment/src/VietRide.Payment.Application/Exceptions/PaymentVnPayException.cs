using VietRide.Shared.Application.Exceptions;

namespace VietRide.Payment.Application.Exceptions;

public sealed class PaymentVnPayException : Exception, ICodedHttpException
{
    public int StatusCode => 502;
    public string ErrorCode => "PAYMENT_VNPAY_ERROR";

    public PaymentVnPayException(string message, Exception? innerException = null) : base(message, innerException) { }
}
