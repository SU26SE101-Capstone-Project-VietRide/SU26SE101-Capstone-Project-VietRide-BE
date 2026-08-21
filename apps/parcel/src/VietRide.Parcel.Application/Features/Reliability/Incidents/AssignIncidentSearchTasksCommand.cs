using MediatR;

namespace VietRide.Parcel.Application.Features.Reliability.Incidents;

public sealed record AssignIncidentSearchTasksCommand(
    Guid IncidentId,
    Guid OperatorId,
    Guid AssigneeUserId) : IRequest<IReadOnlyList<ParcelSearchTaskResponse>>;
