using VietRide.Shared.Application.Exceptions;

namespace VietRide.Payment.Application.Exceptions;

public sealed class MobileAppUpdateRequiredException : Exception, ICodedHttpException
{
    public int StatusCode => 426;
    public string ErrorCode => "MOBILE_APP_UPDATE_REQUIRED";

    public MobileAppUpdateRequiredException()
        : base("Update the mobile app to continue with VNPay.")
    {
    }
}
