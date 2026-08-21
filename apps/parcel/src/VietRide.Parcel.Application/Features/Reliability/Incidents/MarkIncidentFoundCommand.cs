using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Incidents;

public sealed record MarkIncidentFoundCommand(
    Guid IncidentId,
    Guid OperatorId,
    Guid ActorUserId,
    string ActualLocationType,
    Guid? ActualLocationId,
    string? LocationSnapshot,
    IReadOnlyCollection<string>? EvidenceReferences,
    string? Note) : IRequest<ParcelIncidentListItem>;
