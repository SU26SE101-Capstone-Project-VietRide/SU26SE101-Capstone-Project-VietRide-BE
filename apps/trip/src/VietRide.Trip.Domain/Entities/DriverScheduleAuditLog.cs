using System.Text.Json;
using VietRide.Trip.Domain.Constants;

namespace VietRide.Trip.Domain.Entities;

public sealed class DriverScheduleAuditLog
{
    private DriverScheduleAuditLog()
    {
        Action = null!;
    }

    private DriverScheduleAuditLog(
        Guid id,
        Guid driverScheduleId,
        Guid? actorUserId,
        string action,
        JsonElement? metadata,
        DateTimeOffset occurredAt)
    {
        Id = id;
        DriverScheduleId = driverScheduleId;
        ActorUserId = actorUserId;
        Action = action;
        Metadata = metadata;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }

    public Guid DriverScheduleId { get; private set; }

    public Guid? ActorUserId { get; private set; }

    public string Action { get; private set; }

    public JsonElement? Metadata { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static DriverScheduleAuditLog Create(
        Guid id,
        Guid driverScheduleId,
        Guid? actorUserId,
        string action,
        string? metadata,
        DateTimeOffset occurredAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Audit log id cannot be empty.", nameof(id));
        }

        if (driverScheduleId == Guid.Empty)
        {
            throw new ArgumentException("Driver schedule id cannot be empty.", nameof(driverScheduleId));
        }

        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("Actor user id cannot be empty when supplied.", nameof(actorUserId));
        }

        if (string.IsNullOrWhiteSpace(action) || !DriverScheduleAuditAction.IsApproved(action))
        {
            throw new ArgumentException("Audit action is not approved.", nameof(action));
        }

        return new DriverScheduleAuditLog(
            id,
            driverScheduleId,
            actorUserId,
            action,
            ParseMetadata(metadata),
            occurredAt);
    }

    private static JsonElement? ParseMetadata(string? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(metadata);
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Audit metadata must be valid JSON.", nameof(metadata), exception);
        }
    }
}
