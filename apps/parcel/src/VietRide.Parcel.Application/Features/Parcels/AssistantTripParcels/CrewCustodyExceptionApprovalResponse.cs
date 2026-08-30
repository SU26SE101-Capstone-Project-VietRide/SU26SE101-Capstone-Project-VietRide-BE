namespace VietRide.Parcel.Application.Features.Parcels.AssistantTripParcels;

public sealed record CrewCustodyExceptionApprovalResponse(
    Guid RequestId,
    Guid IncidentId,
    string IncidentType,
    string Status,
    string Reason,
    DateTimeOffset ReportedAt);
