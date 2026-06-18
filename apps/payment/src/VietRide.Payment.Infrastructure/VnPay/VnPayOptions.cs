namespace VietRide.Payment.Infrastructure.VnPay;

public sealed class VnPayOptions
{
    public const string SectionName = "VnPay";

    public string TmnCode { get; set; } = string.Empty;
    public string HashSecret { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://sandbox.vnpayment.vn";
    public string ReturnUrl { get; set; } = "https://app.vietride.app/payments/return";
    public string? IpnUrl { get; set; }
    public long MinimumTopUpAmount { get; set; } = 10_000;
}
