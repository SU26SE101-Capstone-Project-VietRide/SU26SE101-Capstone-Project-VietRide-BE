using VietRide.Trip.Domain.Constants;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Domain;

public sealed class TripAuditLogTests
{
    [Fact]
    public void Create_WithValidManualCompletion_CreatesAuditLog()
    {
        var id = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;

        var auditLog = TripAuditLog.Create(
            id,
            tripId,
            actorUserId,
            TripAuditAction.TripCompletedManual,
            $"{{\"tripId\":\"{tripId}\",\"role\":\"DRIVER\"}}",
            occurredAt);

        Assert.Equal(id, auditLog.Id);
        Assert.Equal(tripId, auditLog.TripId);
        Assert.Equal(actorUserId, auditLog.ActorUserId);
        Assert.Equal(TripAuditAction.TripCompletedManual, auditLog.Action);
        Assert.Equal(occurredAt, auditLog.OccurredAt);
        Assert.Equal("DRIVER", auditLog.Metadata?.GetProperty("role").GetString());
    }

    [Fact]
    public void Create_WithEmptyAuditId_Throws()
    {
        Assert.Throws<ArgumentException>(() => CreateValid(id: Guid.Empty));
    }

    [Fact]
    public void Create_WithEmptyTripId_Throws()
    {
        Assert.Throws<ArgumentException>(() => CreateValid(tripId: Guid.Empty));
    }

    [Fact]
    public void Create_WithEmptyActorId_Throws()
    {
        Assert.Throws<ArgumentException>(() => CreateValid(actorUserId: Guid.Empty));
    }

    [Fact]
    public void Create_WithUnapprovedAction_Throws()
    {
        Assert.Throws<ArgumentException>(() => CreateValid(action: "TRIP_STARTED_MANUAL"));
    }

    [Fact]
    public void Create_WithMalformedMetadata_Throws()
    {
        Assert.Throws<ArgumentException>(() => CreateValid(metadata: "{not-json}"));
    }

    [Fact]
    public void Create_ManualCompletionWithoutActor_Throws()
    {
        Assert.Throws<ArgumentException>(() => TripAuditLog.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            TripAuditAction.TripCompletedManual,
            "{}",
            DateTimeOffset.UtcNow));
    }

    private static TripAuditLog CreateValid(
        Guid? id = null,
        Guid? tripId = null,
        Guid? actorUserId = default,
        string action = TripAuditAction.TripCompletedManual,
        string? metadata = "{}") =>
        TripAuditLog.Create(
            id ?? Guid.NewGuid(),
            tripId ?? Guid.NewGuid(),
            actorUserId == default ? Guid.NewGuid() : actorUserId,
            action,
            metadata,
            DateTimeOffset.UtcNow);
}
