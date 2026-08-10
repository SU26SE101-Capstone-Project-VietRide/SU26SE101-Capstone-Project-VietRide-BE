using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Payment.Application.Exceptions;

public sealed class VnPayReturnModeDisabledException : Exception, ICodedHttpException
{
    public int StatusCode => 503;
    public string ErrorCode { get; }

    public VnPayReturnModeDisabledException(VnPayReturnMode returnMode)
        : base($"VNPay return mode '{returnMode}' is disabled.")
    {
        ErrorCode = returnMode switch
        {
            VnPayReturnMode.OPERATOR_WEB => "VNPAY_WEB_DISABLED",
            VnPayReturnMode.MOBILE_SDK => "VNPAY_MOBILE_SDK_DISABLED",
            _ => "VNPAY_RETURN_MODE_DISABLED",
        };
    }
}
