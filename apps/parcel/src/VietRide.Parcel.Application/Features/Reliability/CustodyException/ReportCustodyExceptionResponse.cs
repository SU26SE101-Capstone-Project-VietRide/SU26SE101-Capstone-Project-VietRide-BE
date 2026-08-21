namespace VietRide.Parcel.Application.Features.Reliability.CustodyException;

public sealed record ReportCustodyExceptionResponse(
    Guid ParcelId,
    Guid IncidentId,
    string IncidentType,
    string IncidentStatus,
    Guid CustodyEventId,
    string CustodyEventType,
    DateTimeOffset SearchDeadline);
