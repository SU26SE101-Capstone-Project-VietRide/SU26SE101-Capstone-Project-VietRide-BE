using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Jobs;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Infrastructure.Jobs;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.IntegrationTests.Jobs;

public sealed class Day23RealertPhaseSeparationIntegrationTests(
    Day23ScheduleChangeTimeoutFixture fixture)
    : IClassFixture<Day23ScheduleChangeTimeoutFixture>
{
    [Fact]
    public async Task InitialPhase_CommitsOnceAndRetryRepairsTerminalSchedule()
    {
        var initial = DateTimeOffset.Parse("2026-07-17T10:00:00Z");
        var terminal = initial.AddHours(1);
        var seeded = await fixture.SeedAsync(
            BookingPendingActionSeverity.MAJOR, initial, terminal);
        var failing = Substitute.For<IScheduleChangeAutoAcceptScheduler>();
        failing.When(x => x.EnsureScheduled(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>()))
            .Do(_ => throw new InvalidOperationException("scheduler unavailable"));

        await using (var firstDb = fixture.CreateDb(initial.AddTicks(1)))
        {
            var act = () => new ScheduleChangeAutoAcceptJob(firstDb, failing, SubstituteClock(initial.AddTicks(1)))
                .ExecuteAsync(seeded.ActionId, CancellationToken.None);
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("scheduler unavailable");
        }

        var repaired = Substitute.For<IScheduleChangeAutoAcceptScheduler>();
        await using (var retryDb = fixture.CreateDb(initial.AddMinutes(1)))
        {
            await new ScheduleChangeAutoAcceptJob(retryDb, repaired, SubstituteClock(initial.AddMinutes(1)))
                .ExecuteAsync(seeded.ActionId, CancellationToken.None);
        }

        await using var verify = fixture.CreateDb(initial.AddMinutes(1));
        var phaseId = ScheduleChangeAutoAcceptJob.DeriveMajorInitialPhaseEventId(seeded.ActionId);
        (await verify.OutboxEvents.CountAsync(row => row.Id == phaseId)).Should().Be(1);
        (await verify.OutboxEvents.CountAsync(row => row.Id == PendingActionRealertJob.DeriveEventId(seeded.ActionId)))
            .Should().Be(0);
        (await verify.BookingPendingActions.SingleAsync(row => row.Id == seeded.ActionId)).ResolvedAt
            .Should().BeNull();
        repaired.Received(1).EnsureScheduled(seeded.ActionId, terminal.AddSeconds(1));
    }

    [Fact]
    public async Task DuplicateInitialPhaseJobs_PersistOnePhaseIdentityAndRemainUnresolved()
    {
        var initial = DateTimeOffset.Parse("2026-07-18T10:00:00Z");
        var terminal = initial.AddHours(1);
        var now = initial.AddTicks(1);
        var seeded = await fixture.SeedAsync(
            BookingPendingActionSeverity.MAJOR, initial, terminal);
        var firstScheduler = Substitute.For<IScheduleChangeAutoAcceptScheduler>();
        var secondScheduler = Substitute.For<IScheduleChangeAutoAcceptScheduler>();

        await using var firstDb = fixture.CreateDb(now);
        await using var secondDb = fixture.CreateDb(now);
        await Task.WhenAll(
            new ScheduleChangeAutoAcceptJob(firstDb, firstScheduler, SubstituteClock(now))
                .ExecuteAsync(seeded.ActionId, CancellationToken.None),
            new ScheduleChangeAutoAcceptJob(secondDb, secondScheduler, SubstituteClock(now))
                .ExecuteAsync(seeded.ActionId, CancellationToken.None));

        await using var verify = fixture.CreateDb(now);
        var phaseId = ScheduleChangeAutoAcceptJob.DeriveMajorInitialPhaseEventId(seeded.ActionId);
        (await verify.OutboxEvents.CountAsync(row => row.Id == phaseId)).Should().Be(1);
        (await verify.BookingPendingActions.AsNoTracking()
            .SingleAsync(row => row.Id == seeded.ActionId)).ResolvedAt.Should().BeNull();
        firstScheduler.Received(1).EnsureScheduled(seeded.ActionId, terminal.AddSeconds(1));
        secondScheduler.Received(1).EnsureScheduled(seeded.ActionId, terminal.AddSeconds(1));
    }

    private static IClock SubstituteClock(DateTimeOffset now)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        return clock;
    }
}
