using VietRide.Payment.Domain.Enums;

namespace VietRide.Payment.Application.Features.OperatorReports;

public static class PaymentReportLabels
{
    public const string Unknown = "Không xác định";

    public static string EntryType(string value) => value switch
    {
        nameof(OperatorLedgerEntryType.BOOKING_REVENUE) => "Doanh thu vé",
        nameof(OperatorLedgerEntryType.PARCEL_REVENUE) => "Doanh thu bưu kiện",
        nameof(OperatorLedgerEntryType.BOOKING_REFUND) => "Hoàn tiền vé",
        nameof(OperatorLedgerEntryType.PARCEL_REFUND) => "Hoàn tiền bưu kiện",
        nameof(OperatorLedgerEntryType.VOUCHER_VIETRIDE_FUNDED_CREDIT) => "VietRide bù voucher",
        nameof(OperatorLedgerEntryType.VOUCHER_OPERATOR_FUNDED_AUDIT) => "Voucher do nhà xe tài trợ",
        nameof(OperatorLedgerEntryType.ADJUSTMENT) => "Điều chỉnh",
        nameof(OperatorLedgerEntryType.PARCEL_COMPENSATION) => "Bồi thường bưu kiện",
        _ => Unknown,
    };

    public static string ReferenceType(string value) => value switch
    {
        nameof(OperatorLedgerReferenceType.BOOKING) => "Đặt vé",
        nameof(OperatorLedgerReferenceType.PARCEL) => "Bưu kiện",
        nameof(OperatorLedgerReferenceType.VOUCHER_USAGE) => "Lượt dùng voucher",
        nameof(OperatorLedgerReferenceType.MANUAL) => "Điều chỉnh thủ công",
        _ => Unknown,
    };

    public static string Description(
        string entryType,
        string? adjustmentReason,
        string? note)
    {
        if (adjustmentReason == nameof(OperatorLedgerAdjustmentReason.MANUAL_WALLET_ADJUSTMENT))
            return string.IsNullOrWhiteSpace(note) ? "Điều chỉnh số dư thủ công" : note;

        return adjustmentReason switch
        {
            nameof(OperatorLedgerAdjustmentReason.VIETRIDE_FUNDED_VOUCHER_REVERSAL) =>
                "Thu hồi phần voucher do VietRide tài trợ",
            nameof(OperatorLedgerAdjustmentReason.GENERIC_BOOKING_REFUND_ENTITLEMENT) =>
                "Ghi nhận quyền hoàn tiền đặt vé",
            nameof(OperatorLedgerAdjustmentReason.LEGACY_UNCLASSIFIED) => "Điều chỉnh cũ chưa phân loại",
            null => EntryType(entryType),
            _ => Unknown,
        };
    }
}
