using FluentAssertions;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure.Services;

namespace VietRide.Trip.UnitTests.Services;

public sealed class ResourceAvailabilityPolicyTests
{
    private static readonly AvailabilityResource Driver = new(
        ResourceReservationType.CREW,
        ResourceReservationRole.DRIVER,
        Guid.Parse("10000000-0000-0000-0000-000000000001"));

    [Fact]
    public void SameStationRequiresExactThirtyMinuteBoundary()
    {
        var tooEarly = Compare(At(10, 29), At(12, 29), At(8, 0), At(10, 0), travelMinutes: 0);
        var boundary = Compare(At(10, 30), At(12, 30), At(8, 0), At(10, 0), travelMinutes: 0);

        tooEarly.Should().NotBeNull();
        tooEarly!.Reason.Should().Be(nameof(AvailabilityConflictReason.TURNAROUND_REQUIRED));
        tooEarly.EarliestFeasibleStartAt.Should().Be(At(10, 30));
        boundary.Should().BeNull();
    }

    [Fact]
    public void DifferentLocationAddsRepositionTravelTime()
    {
        var conflict = Compare(At(10, 45), At(12, 45), At(8, 0), At(10, 0), travelMinutes: 60);

        conflict.Should().NotBeNull();
        conflict!.Reason.Should().Be(nameof(AvailabilityConflictReason.REPOSITION_REQUIRED));
        conflict.RequiredTravelMinutes.Should().Be(60);
        conflict.EarliestFeasibleStartAt.Should().Be(At(11, 30));
    }

    [Fact]
    public void OverlapHasPriorityOverTurnaroundReason()
    {
        var conflict = Compare(At(9, 30), At(11, 30), At(8, 0), At(10, 0), travelMinutes: 0);

        conflict.Should().NotBeNull();
        conflict!.Reason.Should().Be(nameof(AvailabilityConflictReason.TIME_OVERLAP));
        conflict.BlockingUntil.Should().Be(At(10, 30));
    }

    [Fact]
    public void ShiftedCandidateMustStillFitBeforeNextAssignment()
    {
        ResourceAvailabilityPolicy.CanFitBeforeNext(
                earliestFeasibleStartAt: At(10, 30),
                candidateDuration: TimeSpan.FromHours(2),
                travelMinutesToNext: 0,
                nextStartAt: At(12, 45))
            .Should().BeFalse();

        ResourceAvailabilityPolicy.CanFitBeforeNext(
                earliestFeasibleStartAt: At(10, 30),
                candidateDuration: TimeSpan.FromHours(2),
                travelMinutesToNext: 0,
                nextStartAt: At(13, 0))
            .Should().BeTrue();
    }

    private static ResourceAvailabilityConflict? Compare(
        DateTimeOffset candidateStart,
        DateTimeOffset candidateEnd,
        DateTimeOffset existingStart,
        DateTimeOffset existingEnd,
        int travelMinutes) =>
        ResourceAvailabilityPolicy.Compare(
            candidateStart,
            candidateEnd,
            existingStart,
            existingEnd,
            Driver,
            AssignmentSourceType.TRIP,
            Guid.NewGuid(),
            travelMinutes);

    private static DateTimeOffset At(int hour, int minute) =>
        new(2026, 8, 11, hour, minute, 0, TimeSpan.Zero);
}
