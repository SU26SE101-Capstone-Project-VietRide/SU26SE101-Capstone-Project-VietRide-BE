using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Application.Features.Admin.ExportOperators;

public static class OperatorExportLabels
{
    public const string Unknown = "Không xác định";

    public static string RegistrationStatus(OperatorRegistrationStatus value) => value switch
    {
        OperatorRegistrationStatus.PENDING => "Chờ duyệt",
        OperatorRegistrationStatus.APPROVED => "Đã duyệt",
        OperatorRegistrationStatus.REJECTED => "Bị từ chối",
        OperatorRegistrationStatus.SUSPENDED => "Tạm ngưng",
        _ => Unknown,
    };
}
