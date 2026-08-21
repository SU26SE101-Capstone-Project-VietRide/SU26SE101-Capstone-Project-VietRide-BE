namespace VietRide.Parcel.Application.Features.Reliability.ReportIncident;

public sealed record ReportParcelIncidentResponse(
    Guid IncidentId,
    Guid ParcelId,
    string IncidentType,
    string Status,
    DateTimeOffset SearchDeadline);
