using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.CustodyScan;

public sealed record RecordParcelCustodyScanCommand(
    Guid ParcelId,
    Guid OperatorId,
    Guid ActorUserId,
    string ActorRole,
    string ParcelCode,
    string EventType,
    string ActualLocationType,
    Guid? ActualLocationId,
    string? LocationSnapshot,
    IReadOnlyCollection<string>? EvidenceReferences,
    string? Reason,
    Guid IdempotencyKey,
    bool RequireAssignedCrew) : IRequest<ParcelCustodyScanResponse>;

public sealed record ParcelCustodyScanResponse(
    Guid CustodyEventId,
    Guid ParcelId,
    string EventType,
    string? ActualLocationType,
    Guid? ActualLocationId,
    DateTimeOffset OccurredAt,
    int Sequence);
