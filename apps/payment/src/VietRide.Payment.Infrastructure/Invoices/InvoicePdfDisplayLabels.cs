using System.Globalization;
using VietRide.Shared.Kernel.Time;

namespace VietRide.Payment.Infrastructure.Invoices;

public static class InvoicePdfDisplayLabels
{
    private static readonly CultureInfo VietnameseCulture = CultureInfo.GetCultureInfo("vi-VN");

    public static string BillingPeriod(string value) => value switch
    {
        "MONTHLY" => "Hàng tháng",
        "YEARLY" => "Hàng năm",
        _ => "Không xác định",
    };

    public static string Amount(long amountVnd)
        => string.Format(VietnameseCulture, "{0:N0} VNĐ", amountVnd);

    public static string IssuedAt(DateTimeOffset value)
        => $"{BusinessTime.ToLocalDateTime(value):dd/MM/yyyy HH:mm} (giờ Việt Nam)";
}
