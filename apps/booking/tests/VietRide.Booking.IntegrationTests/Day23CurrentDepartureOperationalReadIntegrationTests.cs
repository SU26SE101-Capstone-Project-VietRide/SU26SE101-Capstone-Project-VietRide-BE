using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.Bookings.HandleStopDisabled;
using VietRide.Booking.Application.Features.OperatorBookings.ListOperatorBookings;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Booking.Infrastructure;
using VietRide.Booking.IntegrationTests.Messaging;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Shared.Persistence.UnitOfWork;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.IntegrationTests;

[Collection(VoucherPersistenceCollection.CollectionName)]
public sealed class Day23CurrentDepartureOperationalReadIntegrationTests
    : IClassFixture<VoucherPersistenceIntegrationTests.DbBackedVoucherFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly VoucherPersistenceIntegrationTests.DbBackedVoucherFactory _factory;

    public Day23CurrentDepartureOperationalReadIntegrationTests(
        VoucherPersistenceIntegrationTests.DbBackedVoucherFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DateFilterUsesCurrentProjectionAcrossBothVietnamHalfOpenBoundaries()
    {
        await _factory.InitializeAsync();
        var operatorId = Guid.NewGuid();
        var fromUtc = DateTimeOffset.Parse("2026-07-17T17:00:00Z");
        var toUtc = DateTimeOffset.Parse("2026-07-18T17:00:00Z");
        var movedIntoStart = CreateBooking(operatorId, fromUtc.AddTicks(-10), fromUtc);
        var movedBeforeStart = CreateBooking(operatorId, fromUtc.AddHours(1), fromUtc.AddTicks(-10));
        var movedIntoEnd = CreateBooking(operatorId, toUtc.AddTicks(10), toUtc.AddTicks(-10));
        var movedAtEnd = CreateBooking(operatorId, toUtc.AddHours(-1), toUtc);
        await SeedAsync(movedIntoStart, movedBeforeStart, movedIntoEnd, movedAtEnd);

        await using var scope = _factory.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IBookingRepository>();
        var identity = Substitute.For<IIdentityUserServiceClient>();
        var handler = new ListOperatorBookingsQueryHandler(repository, identity);
        var result = await handler.Handle(new ListOperatorBookingsQuery(
            operatorId,
            null,
            null,
            new DateOnly(2026, 7, 18),
            null,
            null,
            SortBy: "departureAt",
            SortDir: "asc"), CancellationToken.None);

        result.Items.Select(item => item.Id).Should().Equal(movedIntoStart.Id, movedIntoEnd.Id);
        result.Items.Select(item => item.Trip.CurrentDepartureAt).Should().Equal(fromUtc, toUtc.AddTicks(-10));
        result.Items.Should().OnlyContain(item => item.Trip.DepartureAt != item.Trip.CurrentDepartureAt);
        await identity.DidNotReceiveWithAnyArgs().GetUserIdByPhoneAsync(default!, default);
    }

    [Fact]
    public async Task DepartureSortUsesCurrentProjectionThenIdInTheRequestedDirection()
    {
        await _factory.InitializeAsync();
        var operatorId = Guid.NewGuid();
        var anchor = DateTimeOffset.Parse("2026-07-20T01:00:00Z");
        var early = CreateBooking(
            operatorId,
            anchor.AddHours(10),
            anchor,
            Guid.Parse("10000000-0000-0000-0000-000000000001"));
        var tieLow = CreateBooking(
            operatorId,
            anchor.AddHours(9),
            anchor.AddHours(1),
            Guid.Parse("10000000-0000-0000-0000-000000000002"));
        var tieHigh = CreateBooking(
            operatorId,
            anchor.AddHours(8),
            anchor.AddHours(1),
            Guid.Parse("10000000-0000-0000-0000-000000000003"));
        var late = CreateBooking(
            operatorId,
            anchor.AddHours(7),
            anchor.AddHours(2),
            Guid.Parse("10000000-0000-0000-0000-000000000004"));
        await SeedAsync(early, tieLow, tieHigh, late);

        await using var scope = _factory.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IBookingRepository>();
        var ascending = await repository.ListOperatorBookingsAsync(Criteria(operatorId, descending: false));
        var descending = await repository.ListOperatorBookingsAsync(Criteria(operatorId, descending: true));

        ascending.Items.Select(item => item.Id).Should().Equal(early.Id, tieLow.Id, tieHigh.Id, late.Id);
        descending.Items.Select(item => item.Id).Should().Equal(late.Id, tieHigh.Id, tieLow.Id, early.Id);
    }

    [Fact]
    public async Task ListAndDetailSerializeCurrentDepartureOnlyInsideTripAndKeepSortKeyClosed()
    {
        await _factory.InitializeAsync();
        var operatorId = Guid.NewGuid();
        var snapshot = DateTimeOffset.Parse("2026-07-20T01:00:00Z");
        var current = snapshot.AddHours(3);
        var booking = CreateBooking(operatorId, snapshot, current);
        await SeedAsync(booking);

        await using var scope = _factory.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IBookingRepository>();
        var list = await repository.ListOperatorBookingsAsync(Criteria(operatorId, descending: false));
        var detail = await repository.GetOperatorBookingDetailAsync(booking.Id, operatorId);
        var listJson = JsonSerializer.SerializeToElement(list.Items.Single(), JsonOptions);
        detail.Should().NotBeNull();
        var detailJson = JsonSerializer.SerializeToElement(detail!, JsonOptions);

        AssertNestedTripShape(listJson, snapshot, current);
        AssertNestedTripShape(detailJson, snapshot, current);
        listJson.TryGetProperty("currentDepartureAt", out _).Should().BeFalse();
        detailJson.TryGetProperty("currentDepartureAt", out _).Should().BeFalse();

        var handler = new ListOperatorBookingsQueryHandler(
            repository,
            Substitute.For<IIdentityUserServiceClient>());
        var act = () => handler.Handle(new ListOperatorBookingsQuery(
            operatorId,
            null,
            null,
            null,
            null,
            null,
            SortBy: "currentDepartureAt"), CancellationToken.None);
        var exception = (await act.Should().ThrowAsync<BadRequestException>()).Which;
        exception.ErrorCode.Should().Be("INVALID_SORT_FIELD");
    }

    [Fact]
    public async Task StopDisabledDeadlineUsesCurrentProjectionInsteadOfImmutableSnapshot()
    {
        await _factory.InitializeAsync();
        var now = DateTimeOffset.Parse("2026-07-15T01:00:00Z");
        var operatorId = Guid.NewGuid();
        var stopId = Guid.NewGuid();
        var booking = CreateBooking(
            operatorId,
            now.AddDays(10),
            now.AddHours(3),
            pickupStopId: stopId);
        await SeedAsync(booking);
        var stats = Substitute.For<IBookingStatsRepository>();
        stats.TryClaimProcessedEventAsync(
                "trip.stop.disabled",
                Arg.Any<Guid>(),
                now,
                Arg.Any<CancellationToken>())
            .Returns(true);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
            var clock = new FixedClock(now);
            var tripClient = Substitute.For<ITripServiceClient>();
            tripClient.GetTripSnapshotAsync(booking.TripId, Arg.Any<CancellationToken>())
                .Returns(new TripSnapshot(
                    booking.TripId, operatorId, Guid.NewGuid(), Guid.NewGuid(), "SCHEDULED",
                    now.AddHours(3), now.AddHours(4), 100_000,
                    new TripStationSnapshot(Guid.NewGuid(), "Origin"),
                    new TripStationSnapshot(Guid.NewGuid(), "Destination"), [], new TripSeatSummary(10, 10)));
            var handler = new HandleStopDisabledCommandHandler(
                scope.ServiceProvider.GetRequiredService<IBookingRepository>(),
                scope.ServiceProvider.GetRequiredService<IBookingPendingActionRepository>(),
                stats,
                new IntegrationEventOutbox(new OutboxStore(db, clock)),
                new EfUnitOfWork(db),
                clock,
                tripClient);
            (await handler.Handle(
                new HandleStopDisabledCommand(Guid.NewGuid(), stopId, operatorId, null),
                CancellationToken.None)).Should().Be(1);
        }

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var action = await verify.BookingPendingActions.AsNoTracking()
            .SingleAsync(row => row.BookingId == booking.Id);
        action.Reason.Should().Be(BookingPendingActionReason.STOP_DISABLED);
        action.Deadline.Should().Be(now.AddHours(1));
    }

    private async Task SeedAsync(params BookingEntity[] bookings)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        db.Bookings.AddRange(bookings);
        await db.SaveChangesAsync();
    }

    private static OperatorBookingListCriteria Criteria(Guid operatorId, bool descending)
        => new(
            operatorId,
            null,
            null,
            null,
            null,
            null,
            null,
            1,
            20,
            "departureAt",
            descending);

    private static BookingEntity CreateBooking(
        Guid operatorId,
        DateTimeOffset snapshot,
        DateTimeOffset current,
        Guid? id = null,
        Guid? pickupStopId = null)
    {
        var booking = BookingEntity.CreatePendingPayment(
            BookingCode.Generate(snapshot),
            Guid.NewGuid(),
            Guid.NewGuid(),
            operatorId,
            pickupStopId.HasValue ? null : Guid.NewGuid(),
            pickupStopId,
            null,
            null,
            Money.FromRaw(100_000),
            Money.Zero,
            Money.FromRaw(100_000),
            tripSnapshotOriginName: "Origin",
            tripSnapshotDestName: "Destination",
            tripSnapshotDeparture: snapshot,
            tripSnapshotRouteName: "Route");
        if (id.HasValue)
        {
            typeof(BookingEntity).GetProperty(nameof(BookingEntity.Id))!.SetValue(booking, id.Value);
        }

        SetCurrentDeparture(booking, current);
        booking.Confirm(snapshot.AddHours(-1));
        return booking;
    }

    private static void SetCurrentDeparture(BookingEntity booking, DateTimeOffset departure)
        => typeof(BookingEntity).GetProperty(nameof(BookingEntity.TripCurrentDeparture))!
            .SetValue(booking, departure);

    private static void AssertNestedTripShape(
        JsonElement root,
        DateTimeOffset snapshot,
        DateTimeOffset current)
    {
        var trip = root.GetProperty("trip");
        trip.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            ["routeName", "originName", "destinationName", "departureAt", "currentDepartureAt"]);
        trip.GetProperty("departureAt").GetDateTimeOffset().Should().Be(snapshot);
        trip.GetProperty("currentDepartureAt").GetDateTimeOffset().Should().Be(current);
    }
}
