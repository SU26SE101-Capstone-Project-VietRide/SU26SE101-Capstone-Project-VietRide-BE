using VietRide.Payment.Domain.Enums;

namespace VietRide.Payment.Application.Features.Management;

public static class FinancialReportLabels
{
    public const string Unknown = "Không xác định";

    public static string TransactionType(string value) => value switch
    {
        "CREDIT" => "Ghi có",
        "DEBIT" => "Ghi nợ",
        _ => Unknown,
    };

    public static string ReferenceType(string value) => value switch
    {
        nameof(PlatformWalletTransactionRef.BOOKING_PAYMENT_HOLD) => "Giữ tiền đặt vé",
        nameof(PlatformWalletTransactionRef.PARCEL_PAYMENT_HOLD) => "Giữ tiền bưu kiện",
        nameof(PlatformWalletTransactionRef.PARCEL_ADDITIONAL_PAYMENT_HOLD) => "Giữ tiền bổ sung bưu kiện",
        nameof(PlatformWalletTransactionRef.BOOKING_REFUND) => "Hoàn tiền đặt vé",
        nameof(PlatformWalletTransactionRef.PARCEL_REFUND) => "Hoàn tiền bưu kiện",
        nameof(PlatformWalletTransactionRef.TRIP_SETTLEMENT) => "Quyết toán chuyến",
        nameof(PlatformWalletTransactionRef.SUBSCRIPTION_PAYMENT) => "Thanh toán gói dịch vụ",
        nameof(PlatformWalletTransactionRef.MANUAL_ADJUSTMENT) => "Điều chỉnh thủ công",
        nameof(PlatformWalletTransactionRef.PARCEL_COMPENSATION) => "Bồi thường bưu kiện",
        nameof(OperatorLedgerReferenceType.BOOKING) => "Đặt vé",
        nameof(OperatorLedgerReferenceType.PARCEL) => "Bưu kiện",
        nameof(OperatorLedgerReferenceType.VOUCHER_USAGE) => "Lượt dùng voucher",
        nameof(OperatorLedgerReferenceType.MANUAL) => "Điều chỉnh thủ công",
        nameof(OperatorWalletTransactionRef.ADJUSTMENT) => "Điều chỉnh ví",
        _ => Unknown,
    };

    public static string BusinessGroup(string? value) => value switch
    {
        "TICKET" => "Vé xe",
        "PARCEL" => "Bưu kiện",
        "REFUND" => "Hoàn tiền",
        "SETTLEMENT" => "Quyết toán",
        "SUBSCRIPTION" => "Gói dịch vụ",
        "COMPENSATION" => "Bồi thường",
        "MANUAL_ADJUSTMENT" => "Điều chỉnh thủ công",
        null or "" => string.Empty,
        _ => Unknown,
    };

    public static string CashFlowPurpose(string? value) => value switch
    {
        "CUSTOMER_FUNDS_HELD" => "Giữ tiền của khách hàng",
        "CUSTOMER_REFUND" => "Hoàn tiền cho khách hàng",
        "OPERATOR_PAYOUT" => "Chi trả cho nhà xe",
        "PLATFORM_REVENUE" => "Doanh thu nền tảng",
        "PARCEL_COMPENSATION_PAYOUT" => "Chi bồi thường bưu kiện",
        "OPERATOR_PAYOUT_RECEIVED" => "Nhận tiền quyết toán",
        "PLATFORM_SERVICE_PAYMENT" => "Thanh toán dịch vụ nền tảng",
        "MANUAL_ADJUSTMENT" => "Điều chỉnh thủ công",
        null or "" => string.Empty,
        _ => Unknown,
    };

    public static string ActorType(string value) => value switch
    {
        "SYSTEM" => "Hệ thống",
        "USER" => "Người dùng",
        _ => Unknown,
    };

    public static string OperatorEffect(string? value) => value switch
    {
        "INCREASES_ENTITLEMENT" => "Tăng khoản được nhận",
        "DECREASES_ENTITLEMENT" => "Giảm khoản được nhận",
        "INCREASES_WALLET_BALANCE" => "Tăng số dư ví",
        "DECREASES_WALLET_BALANCE" => "Giảm số dư ví",
        "AUDIT_ONLY" => "Chỉ đối soát",
        null or "" => string.Empty,
        _ => Unknown,
    };

    public static string SettlementStatus(string value) => value switch
    {
        nameof(OperatorTripSettlementStatus.PENDING_HOLD) => "Đang tạm giữ",
        nameof(OperatorTripSettlementStatus.ELIGIBLE) => "Đủ điều kiện quyết toán",
        nameof(OperatorTripSettlementStatus.SETTLED) => "Đã quyết toán",
        nameof(OperatorTripSettlementStatus.CANCELLED) => "Đã hủy",
        _ => Unknown,
    };

    public static string SettlementMethod(string? value) => value switch
    {
        nameof(OperatorTripSettlementMethod.AUTO_WEEKLY) => "Tự động hằng tuần",
        nameof(OperatorTripSettlementMethod.ADMIN_MANUAL) => "Quản trị viên thực hiện",
        null or "" => string.Empty,
        _ => Unknown,
    };

    public static string ProcessingState(string value) => value switch
    {
        "ON_HOLD" => "Đang tạm giữ",
        "RETRY_SCHEDULED" => "Đã lên lịch thử lại",
        "READY_FOR_SETTLEMENT" => "Sẵn sàng quyết toán",
        "COMPLETED" => "Hoàn tất",
        "CANCELLED" => "Đã hủy",
        _ => Unknown,
    };

    public static string LedgerEntryType(string value)
        => OperatorReports.PaymentReportLabels.EntryType(value);

    public static string Description(string entryType, string? adjustmentReason, string? note)
        => OperatorReports.PaymentReportLabels.Description(entryType, adjustmentReason, note);

    public static string TransactionDescription(string? adjustmentReason, string? note, string referenceType)
    {
        if (adjustmentReason == nameof(OperatorLedgerAdjustmentReason.MANUAL_WALLET_ADJUSTMENT))
            return string.IsNullOrWhiteSpace(note) ? "Điều chỉnh số dư thủ công" : note;
        if (referenceType == nameof(PlatformWalletTransactionRef.MANUAL_ADJUSTMENT)
            || referenceType == nameof(OperatorWalletTransactionRef.ADJUSTMENT))
            return string.IsNullOrWhiteSpace(note) ? "Điều chỉnh số dư thủ công" : note;
        return ReferenceType(referenceType);
    }
}
