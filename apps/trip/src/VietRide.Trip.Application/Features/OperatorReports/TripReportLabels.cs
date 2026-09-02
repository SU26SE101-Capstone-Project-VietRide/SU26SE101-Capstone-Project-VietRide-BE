using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.OperatorReports;

public static class TripReportLabels
{
    public const string Unknown = "Không xác định";

    public static string Status(TripStatus value) => value switch
    {
        TripStatus.SCHEDULED => "Đã lên lịch",
        TripStatus.BOARDING => "Đang đón khách",
        TripStatus.IN_PROGRESS => "Đang chạy",
        TripStatus.COMPLETED => "Hoàn thành",
        TripStatus.CANCELLED => "Đã hủy",
        TripStatus.DISRUPTED => "Gián đoạn",
        _ => Unknown,
    };
}
