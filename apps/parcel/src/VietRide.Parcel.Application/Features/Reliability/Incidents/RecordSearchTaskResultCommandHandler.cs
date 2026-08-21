using System.Text.Json;
using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Reliability.Incidents;

public sealed class RecordSearchTaskResultCommandHandler
    : IRequestHandler<RecordSearchTaskResultCommand, ParcelSearchTaskResponse>
{
    private readonly IParcelReliabilityRepository _reliability;
    private readonly IClock _clock;

    public RecordSearchTaskResultCommandHandler(IParcelReliabilityRepository reliability, IClock clock)
    {
        _reliability = reliability;
        _clock = clock;
    }

    public async Task<ParcelSearchTaskResponse> Handle(
        RecordSearchTaskResultCommand command,
        CancellationToken cancellationToken)
    {
        var incident = await _reliability.GetIncidentAsync(command.IncidentId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_INCIDENT_NOT_FOUND", "Incident was not found.");
        if (incident.OperatorId != command.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Incident does not belong to this operator.");
        var task = await _reliability.GetSearchTaskAsync(command.TaskId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_SEARCH_TASK_NOT_FOUND", "Search task was not found.");
        if (task.IncidentId != incident.Id)
            throw new CodedConflictException("PARCEL_SEARCH_TASK_MISMATCH", "Search task does not belong to this incident.");
        if (task.AssigneeId.HasValue && task.AssigneeId != command.ActorUserId)
            throw new ForbiddenException("FORBIDDEN", "Search task is assigned to another user.");

        var evidenceJson = command.EvidenceReferences is null
            ? null
            : JsonSerializer.Serialize(command.EvidenceReferences);
        if (command.Found)
            task.Complete(command.Result, evidenceJson, _clock.UtcNow);
        else
            task.Fail(command.Result, evidenceJson, _clock.UtcNow);
        await _reliability.UpdateSearchTaskAsync(task, cancellationToken);
        return AssignIncidentSearchTasksCommandHandler.Map(task);
    }
}
