using VietRide.Booking.Domain.Enums;

namespace VietRide.Booking.Application.Features.OperatorReports;

public static class BookingReportLabels
{
    public const string Unknown = "Không xác định";

    public static string Status(BookingStatus value) => value switch
    {
        BookingStatus.PENDING_PAYMENT => "Chờ thanh toán",
        BookingStatus.CONFIRMED => "Đã xác nhận",
        BookingStatus.COMPLETED => "Hoàn thành",
        BookingStatus.EXPIRED => "Hết hạn",
        BookingStatus.CANCELLED => "Đã hủy",
        BookingStatus.NO_SHOW => "Vắng mặt",
        BookingStatus.PARTIAL_NO_SHOW => "Vắng mặt một phần",
        BookingStatus.REFUNDED => "Đã hoàn tiền",
        BookingStatus.DISRUPTED => "Gián đoạn",
        _ => Unknown,
    };

    public static string CancellationReason(BookingCancellationReason? value) => value switch
    {
        null => string.Empty,
        BookingCancellationReason.USER_INITIATED => "Khách hàng chủ động hủy",
        BookingCancellationReason.OPERATOR_CANCELLED_TRIP => "Nhà xe hủy chuyến",
        BookingCancellationReason.OPERATOR_DISRUPTED_IN_PROGRESS => "Nhà xe dừng chuyến đang chạy",
        BookingCancellationReason.SCHEDULE_CHANGED => "Khách không đồng ý đổi lịch",
        BookingCancellationReason.ROUTE_CHANGED_REFUSED => "Khách không đồng ý đổi tuyến",
        BookingCancellationReason.VEHICLE_SUBSTITUTION_DOWNGRADE => "Đổi xe làm giảm hạng dịch vụ",
        BookingCancellationReason.VEHICLE_SUBSTITUTION_NO_SEAT => "Đổi xe không còn ghế phù hợp",
        BookingCancellationReason.STOP_DISABLED_REFUSED => "Khách không đồng ý đổi điểm đón hoặc trả",
        _ => Unknown,
    };
}
