using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Features.Parcels.Reports;

public static class ParcelReportLabels
{
    public const string Unknown = "Không xác định";

    public static string Status(ParcelStatus value) => value switch
    {
        ParcelStatus.PENDING_OPERATOR_REVIEW => "Chờ nhà xe duyệt",
        ParcelStatus.PENDING_PAYMENT => "Chờ thanh toán",
        ParcelStatus.PENDING => "Đang chờ xử lý",
        ParcelStatus.PENDING_ADDITIONAL_PAYMENT => "Chờ thanh toán bổ sung",
        ParcelStatus.RESERVED => "Đã giữ chỗ",
        ParcelStatus.CHECKED_IN => "Đã tiếp nhận",
        ParcelStatus.PENDING_FINAL_PAYMENT => "Chờ thanh toán phần còn lại",
        ParcelStatus.READY_TO_LOAD => "Sẵn sàng xếp hàng",
        ParcelStatus.LOADED => "Đã xếp lên xe",
        ParcelStatus.IN_TRANSIT => "Đang vận chuyển",
        ParcelStatus.PENDING_TRANSFER_CONFIRM => "Chờ xác nhận chuyển xe",
        ParcelStatus.TRANSFER_ESCALATED => "Chuyển xe cần xử lý",
        ParcelStatus.UNLOADED => "Đã dỡ hàng",
        ParcelStatus.DELIVERED_PENDING_CONFIRM => "Đã giao, chờ xác nhận",
        ParcelStatus.DELIVERY_CONFIRMED => "Đã xác nhận nhận hàng",
        ParcelStatus.DELIVERY_REJECTED => "Người nhận từ chối xác nhận",
        ParcelStatus.RETURN_INITIATED => "Đang hoàn trả",
        ParcelStatus.RETURNED => "Đã hoàn trả",
        ParcelStatus.PENDING_OPERATOR_ACTION => "Chờ nhà xe xử lý",
        ParcelStatus.CANCELLED => "Đã hủy",
        ParcelStatus.REJECTED => "Bị từ chối",
        ParcelStatus.EXPIRED => "Hết hạn",
        _ => Unknown,
    };

    public static string Size(ParcelSizeCategory value) => value switch
    {
        ParcelSizeCategory.SMALL => "Nhỏ",
        ParcelSizeCategory.MEDIUM => "Vừa",
        ParcelSizeCategory.LARGE => "Lớn",
        ParcelSizeCategory.EXTRA_LARGE => "Rất lớn",
        _ => Unknown,
    };
}
