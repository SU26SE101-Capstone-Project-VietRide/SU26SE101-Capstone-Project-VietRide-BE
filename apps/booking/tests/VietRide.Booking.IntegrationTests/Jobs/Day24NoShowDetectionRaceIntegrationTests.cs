using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Events;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Infrastructure.Jobs;
using VietRide.Booking.IntegrationTests.Messaging;
using VietRide.Shared.Kernel.Abstractions;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.IntegrationTests.Jobs;

public sealed class Day24NoShowDetectionRaceIntegrationTests(
    Day24StopDisabledAutoFallbackFixture fixture)
    : IClassFixture<Day24StopDisabledAutoFallbackFixture>
{
    [Fact]
    public async Task ConcurrentJobsAndRerun_ProduceOneTransitionHistoryAndOutbox()
    {
        var anchor = DateTimeOffset.Parse("2026-07-26T10:00:00Z");
        var now = anchor.AddMinutes(16);
        await using var seed = fixture.CreateDb(now);
        var seeded = await Day24NoShowTestData.SeedAsync(seed, false, false);
        var snapshot = Day24NoShowTestData.Trip(seeded.Booking, anchor);
        await seed.DisposeAsync();

        var firstHasBookingLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRequestedBookingLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = ExecuteAsync(now, snapshot, afterLock: async () =>
        {
            firstHasBookingLock.TrySetResult();
            await releaseFirst.Task;
        });
        await firstHasBookingLock.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = ExecuteAsync(now, snapshot, beforeLock: () =>
        {
            secondRequestedBookingLock.TrySetResult();
            return Task.CompletedTask;
        });
        await secondRequestedBookingLock.Task.WaitAsync(TimeSpan.FromSeconds(10));
        releaseFirst.TrySetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));
        await ExecuteAsync(now.AddMinutes(5), snapshot);

        await using var verify = fixture.CreateDb(now);
        (await verify.Bookings.AsNoTracking().SingleAsync(row => row.Id == seeded.Booking.Id)).Status
            .Should().Be(BookingStatus.NO_SHOW);
        (await verify.BookingStatusHistories.CountAsync(row => row.BookingId == seeded.Booking.Id)).Should().Be(1);
        (await verify.OutboxEvents.CountAsync(row =>
            row.EventType == BookingPassengerNoShowMarkedIntegrationEvent.EventTypeValue)).Should().Be(1);
    }

    private async Task ExecuteAsync(
        DateTimeOffset now,
        TripSnapshot snapshot,
        Func<Task>? beforeLock = null,
        Func<Task>? afterLock = null)
    {
        await using var db = fixture.CreateDb(now);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        var trips = Substitute.For<ITripServiceClient>();
        trips.GetOperationalTripSnapshotAsync(snapshot.TripId, Arg.Any<CancellationToken>()).Returns(snapshot);
        var realBookings = Day22EventDatabase.CreateBookingRepository(db);
        var bookings = Substitute.For<IBookingRepository>();
        bookings.GetNoShowCandidatesAsync(Arg.Any<CancellationToken>()).Returns(call =>
            CandidatesAsync(realBookings, snapshot.TripId, call.Arg<CancellationToken>()));
        bookings.FindConfirmedWithPassengersForUpdateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                if (beforeLock is not null)
                {
                    await beforeLock();
                }

                var booking = await realBookings.FindConfirmedWithPassengersForUpdateAsync(
                    call.ArgAt<Guid>(0), call.ArgAt<CancellationToken>(1));
                if (booking is not null && afterLock is not null)
                {
                    await afterLock();
                }

                return booking;
            });
        await new NoShowDetectionJob(
            db,
            bookings,
            Day22EventDatabase.CreateStatusHistoryRepository(db),
            trips,
            clock).ExecuteAsync(CancellationToken.None);
    }

    private static async Task<IReadOnlyList<BookingEntity>> CandidatesAsync(
        IBookingRepository bookings,
        Guid tripId,
        CancellationToken cancellationToken)
        => (await bookings.GetNoShowCandidatesAsync(cancellationToken))
            .Where(candidate => candidate.TripId == tripId).ToArray();
}
