using VietRide.Trip.Domain.Constants;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Domain;

public sealed class DriverScheduleAuditLogTests
{
    [Fact]
    public void Create_WithApprovedAction_CreatesAuditLog()
    {
        var id = Guid.NewGuid();
        var driverScheduleId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;

        var auditLog = DriverScheduleAuditLog.Create(
            id,
            driverScheduleId,
            actorUserId,
            DriverScheduleAuditAction.DriverScheduleEdited,
            "{\"changedFields\":[\"vehicleId\"],\"requestId\":\"request-1\"}",
            occurredAt);

        Assert.Equal(id, auditLog.Id);
        Assert.Equal(driverScheduleId, auditLog.DriverScheduleId);
        Assert.Equal(actorUserId, auditLog.ActorUserId);
        Assert.Equal(DriverScheduleAuditAction.DriverScheduleEdited, auditLog.Action);
        Assert.Equal(occurredAt, auditLog.OccurredAt);
        Assert.Equal("request-1", auditLog.Metadata?.GetProperty("requestId").GetString());
    }

    [Fact]
    public void Create_WithNullActor_AllowsSystemAudit()
    {
        var auditLog = DriverScheduleAuditLog.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            DriverScheduleAuditAction.DriverScheduleEdited,
            "{}",
            DateTimeOffset.UtcNow);

        Assert.Null(auditLog.ActorUserId);
    }

    [Fact]
    public void Create_WithEmptyAuditId_Throws()
    {
        Assert.Throws<ArgumentException>(() => CreateValid(id: Guid.Empty));
    }

    [Fact]
    public void Create_WithEmptyDriverScheduleId_Throws()
    {
        Assert.Throws<ArgumentException>(() => CreateValid(driverScheduleId: Guid.Empty));
    }

    [Fact]
    public void Create_WithEmptyActorId_Throws()
    {
        Assert.Throws<ArgumentException>(() => CreateValid(actorUserId: Guid.Empty));
    }

    [Fact]
    public void Create_WithUnapprovedAction_Throws()
    {
        Assert.Throws<ArgumentException>(() => CreateValid(action: "TRIP_EDITED"));
    }

    [Fact]
    public void Create_WithMalformedMetadata_Throws()
    {
        Assert.Throws<ArgumentException>(() => CreateValid(metadata: "{not-json}"));
    }

    [Fact]
    public void ApprovedActionConstants_MatchFrozenContractsExactly()
    {
        var tripActions = typeof(TripAuditAction)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral && !field.IsInitOnly)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();
        var scheduleActions = typeof(DriverScheduleAuditAction)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral && !field.IsInitOnly)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "DRIVER_SCHEDULE_CASCADE_APPLIED",
                "ROUTE_CHANGE_PROPOSAL_APPROVED",
                "ROUTE_CHANGE_PROPOSAL_CREATED",
                "ROUTE_CHANGE_PROPOSAL_EXPIRED",
                "ROUTE_CHANGE_PROPOSAL_REJECTED",
                "ROUTE_CHANGE_PROPOSAL_SUPERSEDED",
                "TRIP_COMPLETED_MANUAL",
                "TRIP_EDITED",
                "TRIP_ROUTE_CHANGED",
                "TRIP_VEHICLE_SWAPPED",
                "VEHICLE_SUBSTITUTION_TRIGGERED",
            },
            tripActions.OrderBy(action => action));
        Assert.Equal(new[] { "DRIVER_SCHEDULE_EDITED" }, scheduleActions);
    }

    private static DriverScheduleAuditLog CreateValid(
        Guid? id = null,
        Guid? driverScheduleId = null,
        Guid? actorUserId = default,
        string action = DriverScheduleAuditAction.DriverScheduleEdited,
        string? metadata = "{}") =>
        DriverScheduleAuditLog.Create(
            id ?? Guid.NewGuid(),
            driverScheduleId ?? Guid.NewGuid(),
            actorUserId == default ? Guid.NewGuid() : actorUserId,
            action,
            metadata,
            DateTimeOffset.UtcNow);
}
