using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Reliability.CustodyException;
using VietRide.Parcel.Domain.Entities;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Reliability.Incidents;

public sealed class AssignIncidentSearchTasksCommandHandler
    : IRequestHandler<AssignIncidentSearchTasksCommand, IReadOnlyList<ParcelSearchTaskResponse>>
{
    private readonly IParcelReliabilityRepository _reliability;
    private readonly IParcelCustodyExceptionRequestRepository _custodyExceptionRequests;

    public AssignIncidentSearchTasksCommandHandler(
        IParcelReliabilityRepository reliability,
        IParcelCustodyExceptionRequestRepository custodyExceptionRequests)
    {
        _reliability = reliability;
        _custodyExceptionRequests = custodyExceptionRequests;
    }

    public async Task<IReadOnlyList<ParcelSearchTaskResponse>> Handle(
        AssignIncidentSearchTasksCommand command,
        CancellationToken cancellationToken)
    {
        var incident = await _reliability.GetIncidentAsync(command.IncidentId, cancellationToken)
            ?? throw new CodedNotFoundException("PARCEL_INCIDENT_NOT_FOUND", "Incident was not found.");
        if (incident.OperatorId != command.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Incident does not belong to this operator.");
        await CustodyExceptionApprovalGuard.EnsureNotPendingAsync(
            _custodyExceptionRequests,
            incident.Id,
            cancellationToken);
        var tasks = await _reliability.ListSearchTasksAsync(incident.Id, cancellationToken);
        foreach (var task in tasks.Where(x => x.Status is
            Domain.Enums.ParcelSearchTaskStatus.OPEN or Domain.Enums.ParcelSearchTaskStatus.IN_PROGRESS))
        {
            task.Assign(command.AssigneeUserId);
            await _reliability.UpdateSearchTaskAsync(task, cancellationToken);
        }
        return tasks.Select(Map).ToArray();
    }

    internal static ParcelSearchTaskResponse Map(ParcelSearchTask task)
        => new(
            task.Id,
            task.IncidentId,
            task.TaskType.ToString(),
            task.Status.ToString(),
            task.AssigneeId,
            task.Location,
            task.Deadline,
            task.Result,
            task.CompletedAt);
}
