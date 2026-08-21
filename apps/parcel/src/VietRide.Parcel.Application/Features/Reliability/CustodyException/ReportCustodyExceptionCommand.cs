using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.CustodyException;

public sealed record ReportCustodyExceptionCommand(
    Guid ParcelId,
    Guid ActorUserId,
    Guid OperatorId,
    string ActorRole,
    string IncidentType,
    string ActualLocationType,
    Guid? ActualLocationId,
    string? LocationSnapshot,
    string? TemporaryExceptionTag,
    string? Description,
    decimal? ObservedWeightKg,
    IReadOnlyCollection<string>? EvidenceUrls,
    string Reason,
    Guid? SupervisorApprovalUserId,
    Guid? IdempotencyKey) : IRequest<ReportCustodyExceptionResponse>;
