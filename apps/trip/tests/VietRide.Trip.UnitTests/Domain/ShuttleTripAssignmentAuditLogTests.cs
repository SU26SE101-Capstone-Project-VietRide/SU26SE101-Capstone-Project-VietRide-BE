using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Domain;

public sealed class ShuttleTripAssignmentAuditLogTests
{
    private const string InitialMetadata = """
        {
          "assignedBy": { "userId": "11111111-1111-1111-1111-111111111111", "displayName": "Điều phối viên", "role": "OPERATOR_ADMIN" },
          "reason": null,
          "previousDriver": null,
          "currentDriver": { "id": "22222222-2222-2222-2222-222222222222", "displayName": "Tài xế mới" },
          "previousVehicle": null,
          "currentVehicle": { "id": "33333333-3333-3333-3333-333333333333", "licensePlate": "30F-170.10" }
        }
        """;

    [Fact]
    public void Create_InitialAssignment_PersistsImmutableAuditData()
    {
        var id = Guid.NewGuid();
        var shuttleTripId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;

        var audit = ShuttleTripAssignmentAuditLog.Create(
            id,
            shuttleTripId,
            operatorId,
            actorUserId,
            ShuttleTripAssignmentAuditLog.InitialAssignedAction,
            InitialMetadata,
            occurredAt);

        Assert.Equal(id, audit.Id);
        Assert.Equal(shuttleTripId, audit.ShuttleTripId);
        Assert.Equal(operatorId, audit.OperatorId);
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Equal(ShuttleTripAssignmentAuditLog.InitialAssignedAction, audit.Action);
        Assert.Equal(occurredAt, audit.OccurredAt);
        Assert.Equal("Điều phối viên", audit.Metadata.GetProperty("assignedBy").GetProperty("displayName").GetString());
    }

    [Fact]
    public void Create_ReassignmentWithoutReason_IsRejected()
    {
        var metadata = InitialMetadata.Replace(
            "\"previousDriver\": null",
            "\"previousDriver\": { \"id\": \"22222222-2222-2222-2222-222222222222\", \"displayName\": \"Tài xế cũ\" }");

        Assert.Throws<ArgumentException>(() => ShuttleTripAssignmentAuditLog.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ShuttleTripAssignmentAuditLog.ReassignedAction,
            metadata,
            DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData("")]
    [InlineData("UNKNOWN")]
    public void Create_InvalidAction_IsRejected(string action)
    {
        Assert.Throws<ArgumentException>(() => ShuttleTripAssignmentAuditLog.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            action,
            InitialMetadata,
            DateTimeOffset.UtcNow));
    }
}
