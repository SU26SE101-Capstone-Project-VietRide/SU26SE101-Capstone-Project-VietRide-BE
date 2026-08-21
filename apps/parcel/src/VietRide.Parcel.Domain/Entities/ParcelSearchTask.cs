using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Domain.Entities;

public sealed class ParcelSearchTask : BaseEntity<Guid>
{
    public Guid IncidentId { get; private set; }
    public Guid ParcelId { get; private set; }
    public ParcelSearchTaskType TaskType { get; private set; }
    public string? Location { get; private set; }
    public Guid? AssigneeId { get; private set; }
    public DateTimeOffset Deadline { get; private set; }
    public ParcelSearchTaskStatus Status { get; private set; }
    public string? Result { get; private set; }
    public string? EvidenceJson { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    private ParcelSearchTask()
    {
    }

    public static ParcelSearchTask Create(
        Guid incidentId,
        Guid parcelId,
        ParcelSearchTaskType taskType,
        string? location,
        Guid? assigneeId,
        DateTimeOffset deadline)
        => new()
        {
            Id = Guid.NewGuid(),
            IncidentId = incidentId,
            ParcelId = parcelId,
            TaskType = taskType,
            Location = Normalize(location),
            AssigneeId = assigneeId,
            Deadline = deadline,
            Status = ParcelSearchTaskStatus.OPEN,
        };

    public void Start()
    {
        if (Status != ParcelSearchTaskStatus.OPEN)
            throw new InvalidOperationException("Only open search tasks can start.");
        Status = ParcelSearchTaskStatus.IN_PROGRESS;
    }

    public void Assign(Guid assigneeId)
    {
        if (assigneeId == Guid.Empty)
            throw new ArgumentException("Assignee id is required.", nameof(assigneeId));
        if (Status is ParcelSearchTaskStatus.COMPLETED
            or ParcelSearchTaskStatus.FAILED
            or ParcelSearchTaskStatus.CANCELLED)
            throw new InvalidOperationException("A closed search task cannot be assigned.");
        AssigneeId = assigneeId;
        if (Status == ParcelSearchTaskStatus.OPEN)
            Status = ParcelSearchTaskStatus.IN_PROGRESS;
    }

    public void Complete(string result, string? evidenceJson, DateTimeOffset at)
    {
        if (Status is ParcelSearchTaskStatus.COMPLETED
            or ParcelSearchTaskStatus.FAILED
            or ParcelSearchTaskStatus.CANCELLED)
            throw new InvalidOperationException("Search task is already closed.");
        Status = ParcelSearchTaskStatus.COMPLETED;
        Result = Normalize(result) ?? throw new ArgumentException("Result is required.");
        EvidenceJson = Normalize(evidenceJson);
        CompletedAt = at;
    }

    public void Fail(string result, string? evidenceJson, DateTimeOffset at)
    {
        if (Status is ParcelSearchTaskStatus.COMPLETED
            or ParcelSearchTaskStatus.FAILED
            or ParcelSearchTaskStatus.CANCELLED)
            throw new InvalidOperationException("Search task is already closed.");
        Status = ParcelSearchTaskStatus.FAILED;
        Result = Normalize(result) ?? throw new ArgumentException("Result is required.");
        EvidenceJson = Normalize(evidenceJson);
        CompletedAt = at;
    }

    public void Cancel(DateTimeOffset at)
    {
        if (Status == ParcelSearchTaskStatus.CANCELLED)
            return;
        if (Status is ParcelSearchTaskStatus.COMPLETED or ParcelSearchTaskStatus.FAILED)
            throw new InvalidOperationException("Search task is already closed.");
        Status = ParcelSearchTaskStatus.CANCELLED;
        CompletedAt = at;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
