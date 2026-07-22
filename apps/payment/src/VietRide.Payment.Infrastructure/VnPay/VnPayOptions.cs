namespace VietRide.Payment.Infrastructure.VnPay;

public sealed class VnPayOptions
{
    public const string SectionName = "VnPay";

    public string TmnCode { get; set; } = string.Empty;
    public string HashSecret { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";
    public string ReturnUrl { get; set; } = "https://app.vietride.online/payments/return";
    public string? IpnUrl { get; set; } = "https://api.vietride.online/v1/payments/vnpay-ipn";
    public int PaymentTimeoutMinutes { get; set; } = 15;
    public long MinimumTopUpAmount { get; set; } = 10_000;
}
