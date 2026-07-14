using System.Text.Json;
using VietRide.Trip.Domain.Constants;

namespace VietRide.Trip.Domain.Entities;

public sealed class TripAuditLog
{
    private TripAuditLog()
    {
        Action = null!;
    }

    private TripAuditLog(
        Guid id,
        Guid tripId,
        Guid? actorUserId,
        string action,
        JsonElement? metadata,
        DateTimeOffset occurredAt)
    {
        Id = id;
        TripId = tripId;
        ActorUserId = actorUserId;
        Action = action;
        Metadata = metadata;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }

    public Guid TripId { get; private set; }

    public Guid? ActorUserId { get; private set; }

    public string Action { get; private set; }

    public JsonElement? Metadata { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static TripAuditLog Create(
        Guid id,
        Guid tripId,
        Guid? actorUserId,
        string action,
        string? metadata,
        DateTimeOffset occurredAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Audit log id cannot be empty.", nameof(id));
        }

        if (tripId == Guid.Empty)
        {
            throw new ArgumentException("Trip id cannot be empty.", nameof(tripId));
        }

        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("Actor user id cannot be empty when supplied.", nameof(actorUserId));
        }

        if (string.IsNullOrWhiteSpace(action) || !TripAuditAction.IsApproved(action))
        {
            throw new ArgumentException("Audit action is not approved.", nameof(action));
        }

        if (action == TripAuditAction.TripCompletedManual && actorUserId is null)
        {
            throw new ArgumentException("Manual trip completion requires an actor.", nameof(actorUserId));
        }

        var parsedMetadata = ParseMetadata(metadata);

        return new TripAuditLog(id, tripId, actorUserId, action, parsedMetadata, occurredAt);
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
