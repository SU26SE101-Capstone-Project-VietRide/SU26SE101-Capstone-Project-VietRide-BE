using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Services;

internal static class ParcelIncidentReportPolicy
{
    private const string AllowedStatuses = "UNLOADED,DELIVERED_PENDING_CONFIRM";

    public static bool CanPassengerReport(ParcelStatus status) =>
        status is ParcelStatus.UNLOADED or ParcelStatus.DELIVERED_PENDING_CONFIRM;

    public static CodedConflictException StatusNotReportable(
        ParcelStatus status,
        ParcelIncidentType incidentType)
        => new(
            "PARCEL_INCIDENT_STATUS_NOT_REPORTABLE",
            "Passenger incident reporting is not allowed for the current Parcel status.",
            [
                new ValidationError("status", status.ToString()),
                new ValidationError("incidentType", incidentType.ToString()),
                new ValidationError("allowedStatuses", AllowedStatuses),
            ]);
}
