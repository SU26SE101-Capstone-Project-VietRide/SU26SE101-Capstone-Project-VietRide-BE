using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Incidents;

public sealed record RecordSearchTaskResultCommand(
    Guid IncidentId,
    Guid TaskId,
    Guid OperatorId,
    Guid ActorUserId,
    bool Found,
    string Result,
    IReadOnlyCollection<string>? EvidenceReferences) : IRequest<ParcelSearchTaskResponse>;
