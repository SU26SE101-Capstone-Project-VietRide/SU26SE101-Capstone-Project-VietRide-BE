using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Events;
using VietRide.Booking.Application.Exceptions;
using VietRide.Booking.Domain.Constants;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Infrastructure.Jobs;
using VietRide.Booking.IntegrationTests.Messaging;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Persistence.Outbox;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.IntegrationTests.Jobs;

public sealed class Day24NoShowDetectionIntegrationTests(
    Day24StopDisabledAutoFallbackFixture fixture)
    : IClassFixture<Day24StopDisabledAutoFallbackFixture>
{
    [Theory]
    [InlineData(false, 5, 0, BookingStatus.NO_SHOW, "TERMINAL")]
    [InlineData(true, 5, 3, BookingStatus.PARTIAL_NO_SHOW, "ALONG_ROUTE")]
    public async Task EligibleBooking_CoversAllPendingAndMixedThreeOfFiveWithExactPendingOutbox(
        bool alongRoute,
        int passengerCount,
        int boardedPassengerCount,
        BookingStatus expectedStatus,
        string triggerType)
    {
        var anchor = DateTimeOffset.Parse("2026-07-24T10:00:00Z");
        var now = anchor.AddMinutes(16);
        await using var seed = fixture.CreateDb(now);
        var seeded = await Day24NoShowTestData.SeedAsync(
            seed,
            alongRoute,
            passengerCount,
            boardedPassengerCount);
        var snapshot = Day24NoShowTestData.Trip(seeded.Booking, anchor);
        await seed.DisposeAsync();

        await ExecuteAsync(now, snapshot);

        await using var verify = fixture.CreateDb(now);
        var booking = await verify.Bookings.AsNoTracking().SingleAsync(row => row.Id == seeded.Booking.Id);
        var passengers = await verify.Passengers.AsNoTracking()
            .Where(row => row.BookingId == seeded.Booking.Id).OrderBy(row => row.SeatNumber).ToListAsync();
        booking.Status.Should().Be(expectedStatus);
        booking.RefundedAt.Should().BeNull();
        booking.CancelledAt.Should().BeNull();
        (await verify.Tickets.AsNoTracking().Where(row => row.BookingId == booking.Id).ToListAsync())
            .Should().OnlyContain(ticket => ticket.Status == TicketStatus.ISSUED);
        passengers.Where(row => seeded.PendingPassengerIds.Contains(row.Id))
            .Should().OnlyContain(passenger => passenger.BoardingStatus == PassengerBoardingStatus.NO_SHOW);
        if (seeded.BoardedPassengerIds.Count > 0)
        {
            passengers.Where(row => seeded.BoardedPassengerIds.Contains(row.Id))
                .Should().OnlyContain(passenger => passenger.BoardingStatus == PassengerBoardingStatus.BOARDED);
        }
        var history = await verify.BookingStatusHistories.AsNoTracking().SingleAsync(row => row.BookingId == booking.Id);
        history.Status.Should().Be(expectedStatus);
        history.Source.Should().Be(BookingStatusHistorySource.MarkNoShow);
        history.ActorUserId.Should().BeNull();
        history.ReasonCode.Should().BeNull();
        var outbox = await verify.OutboxEvents.AsNoTracking().SingleAsync(row =>
            row.Id == NoShowDetectionJob.DeriveEventId(booking.Id, expectedStatus));
        outbox.Status.Should().Be(OutboxEventStatus.PENDING);
        outbox.PublishedAt.Should().BeNull();
        using var payload = JsonDocument.Parse(outbox.Payload);
        var expectedFields = new List<string>
        {
            "eventId", "occurredAt", "eventType", "bookingId", "tripId", "userId",
            "bookingStatus", "newlyNoShowPassengerIds", "triggerType",
        };
        if (triggerType == "ALONG_ROUTE") expectedFields.Add("pickupStopId");
        payload.RootElement.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(expectedFields);
        payload.RootElement.GetProperty("eventId").GetGuid().Should().Be(outbox.Id);
        payload.RootElement.GetProperty("bookingStatus").GetString().Should().Be(expectedStatus.ToString());
        payload.RootElement.GetProperty("triggerType").GetString().Should().Be(triggerType);
        payload.RootElement.TryGetProperty("pickupStopId", out _).Should().Be(triggerType == "ALONG_ROUTE");
        payload.RootElement.GetProperty("newlyNoShowPassengerIds").EnumerateArray()
            .Select(value => value.GetGuid()).Should().BeEquivalentTo(seeded.PendingPassengerIds);
    }

    [Fact]
    public async Task EqualityAndAllBoardedFiveOfFive_RemainUntouchedWithoutHistoryOrOutbox()
    {
        var anchor = DateTimeOffset.Parse("2026-07-24T12:00:00Z");
        var equality = anchor.AddMinutes(15);
        await using var seed = fixture.CreateDb(equality);
        var equalitySeeded = await Day24NoShowTestData.SeedAsync(seed, false, 5, 0);
        var allBoardedSeeded = await Day24NoShowTestData.SeedAsync(seed, true, 5, 5);
        var equalitySnapshot = Day24NoShowTestData.Trip(equalitySeeded.Booking, anchor);
        var allBoardedSnapshot = Day24NoShowTestData.Trip(allBoardedSeeded.Booking, anchor);
        await seed.DisposeAsync();

        await ExecuteAsync(equality, equalitySnapshot);
        await ExecuteAsync(anchor.AddMinutes(16), allBoardedSnapshot);

        await using var verify = fixture.CreateDb(anchor.AddMinutes(16));
        (await verify.Bookings.AsNoTracking().SingleAsync(row => row.Id == equalitySeeded.Booking.Id)).Status
            .Should().Be(BookingStatus.CONFIRMED);
        (await verify.Passengers.AsNoTracking().Where(row => row.BookingId == equalitySeeded.Booking.Id).ToListAsync())
            .Should().OnlyContain(passenger => passenger.BoardingStatus == PassengerBoardingStatus.PENDING);
        (await verify.Bookings.AsNoTracking().SingleAsync(row => row.Id == allBoardedSeeded.Booking.Id)).Status
            .Should().Be(BookingStatus.CONFIRMED);
        (await verify.Passengers.AsNoTracking().Where(row => row.BookingId == allBoardedSeeded.Booking.Id).ToListAsync())
            .Should().OnlyContain(passenger => passenger.BoardingStatus == PassengerBoardingStatus.BOARDED);
        (await verify.BookingStatusHistories.CountAsync(row =>
            row.BookingId == equalitySeeded.Booking.Id || row.BookingId == allBoardedSeeded.Booking.Id)).Should().Be(0);
        var forbiddenEventIds = new[]
        {
            NoShowDetectionJob.DeriveEventId(equalitySeeded.Booking.Id, BookingStatus.NO_SHOW),
            NoShowDetectionJob.DeriveEventId(equalitySeeded.Booking.Id, BookingStatus.PARTIAL_NO_SHOW),
            NoShowDetectionJob.DeriveEventId(allBoardedSeeded.Booking.Id, BookingStatus.NO_SHOW),
            NoShowDetectionJob.DeriveEventId(allBoardedSeeded.Booking.Id, BookingStatus.PARTIAL_NO_SHOW),
        };
        (await verify.OutboxEvents.CountAsync(row => forbiddenEventIds.Contains(row.Id))).Should().Be(0);
    }

    [Fact]
    public async Task UpstreamFailure_FailsClosedWithoutAnyStateHistoryOrOutboxChange()
    {
        var now = DateTimeOffset.Parse("2026-07-25T10:16:00Z");
        await using var seed = fixture.CreateDb(now);
        var seeded = await Day24NoShowTestData.SeedAsync(seed, false, false);
        await seed.DisposeAsync();
        var trips = Substitute.For<ITripServiceClient>();
        trips.GetOperationalTripSnapshotAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<Task<TripSnapshot>>(_ => throw new BookingUpstreamUnavailableException("unavailable"));

        var act = () => ExecuteAsync(now, trips, seeded.Booking.TripId);
        var exception = await act.Should().ThrowAsync<BookingUpstreamUnavailableException>();
        exception.Which.ErrorCode.Should().Be("UPSTREAM_UNAVAILABLE");
        exception.Which.StatusCode.Should().Be(502);

        await using var verify = fixture.CreateDb(now);
        (await verify.Bookings.AsNoTracking().SingleAsync(row => row.Id == seeded.Booking.Id)).Status
            .Should().Be(BookingStatus.CONFIRMED);
        (await verify.Passengers.AsNoTracking().SingleAsync(row => row.Id == seeded.PendingPassengerId)).BoardingStatus
            .Should().Be(PassengerBoardingStatus.PENDING);
        (await verify.BookingStatusHistories.CountAsync(row => row.BookingId == seeded.Booking.Id)).Should().Be(0);
        (await verify.OutboxEvents.CountAsync(row =>
            row.Id == NoShowDetectionJob.DeriveEventId(seeded.Booking.Id, BookingStatus.NO_SHOW))).Should().Be(0);
    }

    [Fact]
    public async Task MissingRequiredAnchor_MapsToUpstreamUnavailableAndFailsClosed()
    {
        var now = DateTimeOffset.Parse("2026-07-25T12:16:00Z");
        await using var seed = fixture.CreateDb(now);
        var seeded = await Day24NoShowTestData.SeedAsync(seed, false, false);
        var snapshot = Day24NoShowTestData.Trip(seeded.Booking, now.AddMinutes(-16)) with
        {
            ActualDepartureTime = null,
        };
        await seed.DisposeAsync();

        var act = () => ExecuteAsync(now, snapshot);
        var exception = await act.Should().ThrowAsync<BookingUpstreamUnavailableException>();
        exception.Which.ErrorCode.Should().Be("UPSTREAM_UNAVAILABLE");

        await using var verify = fixture.CreateDb(now);
        (await verify.Bookings.AsNoTracking().SingleAsync(row => row.Id == seeded.Booking.Id)).Status
            .Should().Be(BookingStatus.CONFIRMED);
        (await verify.BookingStatusHistories.CountAsync(row => row.BookingId == seeded.Booking.Id)).Should().Be(0);
        (await verify.OutboxEvents.CountAsync(row =>
            row.Id == NoShowDetectionJob.DeriveEventId(seeded.Booking.Id, BookingStatus.NO_SHOW))).Should().Be(0);
    }

    private Task ExecuteAsync(DateTimeOffset now, TripSnapshot snapshot)
    {
        var trips = Substitute.For<ITripServiceClient>();
        trips.GetOperationalTripSnapshotAsync(snapshot.TripId, Arg.Any<CancellationToken>()).Returns(snapshot);
        return ExecuteAsync(now, trips, snapshot.TripId);
    }

    private async Task ExecuteAsync(DateTimeOffset now, ITripServiceClient trips, Guid tripId)
    {
        await using var db = fixture.CreateDb(now);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        var realBookings = Day22EventDatabase.CreateBookingRepository(db);
        var bookings = Substitute.For<IBookingRepository>();
        bookings.GetNoShowCandidatesAsync(Arg.Any<CancellationToken>()).Returns(call =>
            CandidatesAsync(realBookings, tripId, call.Arg<CancellationToken>()));
        bookings.FindConfirmedWithPassengersForUpdateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => realBookings.FindConfirmedWithPassengersForUpdateAsync(
                call.ArgAt<Guid>(0), call.ArgAt<CancellationToken>(1)));
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
