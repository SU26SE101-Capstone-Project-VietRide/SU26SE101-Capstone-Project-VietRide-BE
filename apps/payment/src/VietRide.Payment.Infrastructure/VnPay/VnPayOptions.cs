namespace VietRide.Payment.Infrastructure.VnPay;

public sealed class VnPayOptions
{
    public const string SectionName = "VnPay";

    public string TmnCode { get; set; } = string.Empty;
    public string HashSecret { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";
    public string WebReturnUrl { get; set; } = "https://app.vietride.online/payments/return";
    public string MobileSdkReturnUrl { get; set; } = "https://api.vietride.online/v1/payments/vnpay-mobile-sdk-return";
    public string SdkScheme { get; set; } = "vietride";
    public bool IsSandbox { get; set; }
    public bool WebEnabled { get; set; }
    public bool MobileSdkEnabled { get; set; }
    public string? IpnUrl { get; set; } = "https://api.vietride.online/v1/payments/vnpay-ipn";
    public int PaymentTimeoutMinutes { get; set; } = 15;
    public long MinimumTopUpAmount { get; set; } = 10_000;
}
