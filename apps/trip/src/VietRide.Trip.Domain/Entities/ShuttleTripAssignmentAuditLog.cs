using System.Text.Json;

namespace VietRide.Trip.Domain.Entities;

public sealed class ShuttleTripAssignmentAuditLog
{
    public const string InitialAssignedAction = "INITIAL_ASSIGNED";
    public const string ReassignedAction = "REASSIGNED";

    private ShuttleTripAssignmentAuditLog()
    {
        Action = null!;
    }

    private ShuttleTripAssignmentAuditLog(
        Guid id,
        Guid shuttleTripId,
        Guid operatorId,
        Guid actorUserId,
        string action,
        JsonElement metadata,
        DateTimeOffset occurredAt)
    {
        Id = id;
        ShuttleTripId = shuttleTripId;
        OperatorId = operatorId;
        ActorUserId = actorUserId;
        Action = action;
        Metadata = metadata;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }
    public Guid ShuttleTripId { get; private set; }
    public Guid OperatorId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string Action { get; private set; }
    public JsonElement Metadata { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static ShuttleTripAssignmentAuditLog Create(
        Guid id,
        Guid shuttleTripId,
        Guid operatorId,
        Guid actorUserId,
        string action,
        string metadata,
        DateTimeOffset occurredAt)
    {
        ValidateId(id, nameof(id));
        ValidateId(shuttleTripId, nameof(shuttleTripId));
        ValidateId(operatorId, nameof(operatorId));
        ValidateId(actorUserId, nameof(actorUserId));
        if (action is not (InitialAssignedAction or ReassignedAction))
        {
            throw new ArgumentException("Assignment audit action is invalid.", nameof(action));
        }

        if (occurredAt == default)
        {
            throw new ArgumentException("Assignment audit occurrence time is required.", nameof(occurredAt));
        }

        var parsedMetadata = ParseAndValidateMetadata(metadata, action);
        return new ShuttleTripAssignmentAuditLog(
            id,
            shuttleTripId,
            operatorId,
            actorUserId,
            action,
            parsedMetadata,
            occurredAt);
    }

    private static JsonElement ParseAndValidateMetadata(string metadata, string action)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            throw new ArgumentException("Assignment audit metadata is required.", nameof(metadata));
        }

        try
        {
            using var document = JsonDocument.Parse(metadata);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !HasNonEmptyString(root, "assignedBy", "displayName")
                || !HasNonEmptyString(root, "assignedBy", "role")
                || !HasNonEmptyString(root, "currentVehicle", "licensePlate")
                || !HasNonEmptyString(root, "currentDriver", "displayName"))
            {
                throw new ArgumentException("Assignment audit metadata is incomplete.", nameof(metadata));
            }

            if (action == ReassignedAction
                && (!root.TryGetProperty("reason", out var reason)
                    || reason.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(reason.GetString())))
            {
                throw new ArgumentException("Reassignment audit reason is required.", nameof(metadata));
            }

            return root.Clone();
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Assignment audit metadata must be valid JSON.", nameof(metadata), exception);
        }
    }

    private static bool HasNonEmptyString(JsonElement root, string objectName, string propertyName)
        => root.TryGetProperty(objectName, out var nested)
            && nested.ValueKind == JsonValueKind.Object
            && nested.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString());

    private static void ValidateId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Assignment audit identifiers cannot be empty.", parameterName);
        }
    }
}
