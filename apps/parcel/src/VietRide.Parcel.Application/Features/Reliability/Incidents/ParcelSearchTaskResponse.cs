namespace VietRide.Parcel.Application.Features.Reliability.Incidents;

public sealed record ParcelSearchTaskResponse(
    Guid TaskId,
    Guid IncidentId,
    string TaskType,
    string Status,
    Guid? AssigneeId,
    string? Location,
    DateTimeOffset Deadline,
    string? Result,
    DateTimeOffset? CompletedAt,
    OperatorUserSummaryResponse? Assignee = null);
