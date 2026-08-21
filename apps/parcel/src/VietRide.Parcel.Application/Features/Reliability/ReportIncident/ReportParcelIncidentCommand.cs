using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.ReportIncident;

public sealed record ReportParcelIncidentCommand(
    Guid ParcelId,
    Guid ReporterUserId,
    Guid? OperatorId,
    string IncidentType,
    string? Description,
    IReadOnlyCollection<string>? EvidenceUrls) : IRequest<ReportParcelIncidentResponse>;
