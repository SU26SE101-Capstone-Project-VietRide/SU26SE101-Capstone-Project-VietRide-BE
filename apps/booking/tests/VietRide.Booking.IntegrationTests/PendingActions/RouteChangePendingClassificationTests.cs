using FluentAssertions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Jobs;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.PendingActions;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.IntegrationTests.PendingActions;

public sealed class RouteChangePendingClassificationTests
{
    private static readonly DateTimeOffset OccurredAt =
        DateTimeOffset.Parse("2026-07-23T01:00:00Z");

    [Fact]
    public async Task TerminalPickupDoesNotCreatePendingAction()
    {
        var fixture = new Fixture();
        var booking = CreateConfirmedBooking(pickupStationId: Guid.NewGuid());
        fixture.SetConfirmedBookings(booking);

        var created = await fixture.HandleAsync(
            CreateAffectedBooking(booking.Id, Guid.NewGuid()));

        created.Should().Be(0);
        await fixture.PendingActions.DidNotReceiveWithAnyArgs()
            .AddAsync(default!, default);
        fixture.Scheduler.DidNotReceiveWithAnyArgs()
            .EnsureScheduled(default, default);
    }

    [Fact]
    public async Task RetainedAlongRoutePickupDoesNotCreatePendingAction()
    {
        var fixture = new Fixture();
        var pickupStopId = Guid.NewGuid();
        var booking = CreateConfirmedBooking(pickupStopId: pickupStopId);
        fixture.SetConfirmedBookings(booking);

        var created = await fixture.HandleAsync(
            CreateAffectedBooking(booking.Id, pickupStopId));

        created.Should().Be(0);
        await fixture.PendingActions.DidNotReceiveWithAnyArgs()
            .AddAsync(default!, default);
        fixture.Scheduler.DidNotReceiveWithAnyArgs()
            .EnsureScheduled(default, default);
    }

    [Fact]
    public async Task MissingAlongRoutePickupCreatesPendingActionWithFrozenCandidates()
    {
        var fixture = new Fixture();
        var originalPickupStopId = Guid.NewGuid();
        var booking = CreateConfirmedBooking(pickupStopId: originalPickupStopId);
        fixture.SetConfirmedBookings(booking);

        var created = await fixture.HandleAsync(
            CreateAffectedBooking(booking.Id, Guid.NewGuid()));

        created.Should().Be(1);
        fixture.AddedActions.Should().ContainSingle();
        var action = fixture.AddedActions.Single();
        action.BookingId.Should().Be(booking.Id);
        action.Reason.Should().Be(BookingPendingActionReason.ROUTE_CHANGE);
        action.Metadata.Should().Contain(originalPickupStopId.ToString());
        fixture.Scheduler.Received(1)
            .EnsureScheduled(action.Id, action.Deadline.AddSeconds(1));
    }

    [Fact]
    public async Task MixedBatchCreatesOnlyForMissingAlongRoutePickup()
    {
        var fixture = new Fixture();
        var tripId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var terminal = CreateConfirmedBooking(
            pickupStationId: Guid.NewGuid(),
            tripId: tripId,
            operatorId: operatorId);
        var retainedStopId = Guid.NewGuid();
        var retained = CreateConfirmedBooking(
            pickupStopId: retainedStopId,
            tripId: tripId,
            operatorId: operatorId);
        var missing = CreateConfirmedBooking(
            pickupStopId: Guid.NewGuid(),
            tripId: tripId,
            operatorId: operatorId);
        fixture.SetConfirmedBookings(terminal, retained, missing);

        var created = await fixture.HandleAsync(
            CreateAffectedBooking(terminal.Id, Guid.NewGuid()),
            CreateAffectedBooking(retained.Id, retainedStopId),
            CreateAffectedBooking(missing.Id, Guid.NewGuid()));

        created.Should().Be(1);
        fixture.AddedActions.Should().ContainSingle(action => action.BookingId == missing.Id);
        fixture.Scheduler.Received(1).EnsureScheduled(
            fixture.AddedActions.Single().Id,
            fixture.AddedActions.Single().Deadline.AddSeconds(1));
    }

    [Fact]
    public async Task UnaffectedBookingSupersedesStaleRouteChangeAction()
    {
        var fixture = new Fixture();
        var pickupStopId = Guid.NewGuid();
        var booking = CreateConfirmedBooking(pickupStopId: pickupStopId);
        var stale = BookingPendingAction.Create(
            booking.Id,
            BookingPendingActionReason.ROUTE_CHANGE,
            OccurredAt.AddMinutes(15));
        fixture.SetConfirmedBookings(booking);
        fixture.PendingActions.GetActiveByBookingIdAsync(
                booking.Id,
                Arg.Any<CancellationToken>())
            .Returns(stale);

        var created = await fixture.HandleAsync(
            CreateAffectedBooking(booking.Id, pickupStopId));

        created.Should().Be(0);
        stale.ResolvedAction.Should().Be(BookingPendingActionResolved.SUPERSEDED);
        stale.ResolvedAt.Should().Be(OccurredAt);
        fixture.PendingActions.Received(1).Update(stale);
        await fixture.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await fixture.PendingActions.DidNotReceiveWithAnyArgs()
            .AddAsync(default!, default);
    }

    [Fact]
    public async Task UnaffectedBookingPreservesUnrelatedActiveAction()
    {
        var fixture = new Fixture();
        var pickupStopId = Guid.NewGuid();
        var booking = CreateConfirmedBooking(pickupStopId: pickupStopId);
        var unrelated = BookingPendingAction.Create(
            booking.Id,
            BookingPendingActionReason.SCHEDULE_CHANGE,
            OccurredAt.AddHours(2));
        fixture.SetConfirmedBookings(booking);
        fixture.PendingActions.GetActiveByBookingIdAsync(
                booking.Id,
                Arg.Any<CancellationToken>())
            .Returns(unrelated);

        var created = await fixture.HandleAsync(
            CreateAffectedBooking(booking.Id, pickupStopId));

        created.Should().Be(0);
        unrelated.ResolvedAt.Should().BeNull();
        fixture.PendingActions.DidNotReceiveWithAnyArgs().Update(default!);
        await fixture.UnitOfWork.DidNotReceiveWithAnyArgs()
            .SaveChangesAsync(default);
    }

    [Fact]
    public async Task ReplayDoesNotCreateAnotherActionAndSchedulesOnlyPersistedAction()
    {
        var fixture = new Fixture();
        var booking = CreateConfirmedBooking(pickupStopId: Guid.NewGuid());
        var replay = BookingPendingAction.Create(
            booking.Id,
            BookingPendingActionReason.ROUTE_CHANGE,
            OccurredAt.AddMinutes(30));
        fixture.SetConfirmedBookings(booking);
        fixture.PendingActions.GetByBookingAndSourceEventAsync(
                booking.Id,
                fixture.EventId,
                Arg.Any<CancellationToken>())
            .Returns([replay]);

        var created = await fixture.HandleAsync(
            CreateAffectedBooking(booking.Id, Guid.NewGuid()));

        created.Should().Be(0);
        await fixture.PendingActions.DidNotReceiveWithAnyArgs()
            .AddAsync(default!, default);
        fixture.Scheduler.Received(1)
            .EnsureScheduled(replay.Id, replay.Deadline.AddSeconds(1));
        fixture.Scheduler.DidNotReceive().EnsureScheduled(
            Arg.Is<Guid>(id => id != replay.Id),
            Arg.Any<DateTimeOffset>());
    }

    private static RouteChangeAffectedBooking CreateAffectedBooking(
        Guid bookingId,
        Guid candidateStopId)
        => new(
            bookingId,
            [
                new RouteChangeCandidateStop(
                    candidateStopId,
                    null,
                    "Alternative stop",
                    1,
                    OccurredAt.AddMinutes(10)),
                new RouteChangeCandidateStop(
                    null,
                    Guid.NewGuid(),
                    "Destination",
                    2,
                    OccurredAt.AddMinutes(25)),
            ]);

    private static BookingEntity CreateConfirmedBooking(
        Guid? pickupStationId = null,
        Guid? pickupStopId = null,
        Guid? tripId = null,
        Guid? operatorId = null)
    {
        var booking = BookingEntity.CreatePendingPayment(
            BookingCode.Generate(OccurredAt),
            Guid.NewGuid(),
            tripId ?? Guid.NewGuid(),
            operatorId ?? Guid.NewGuid(),
            pickupStationId,
            pickupStopId,
            Guid.NewGuid(),
            null,
            Money.FromRaw(100_000),
            Money.Zero,
            Money.FromRaw(100_000),
            tripSnapshotDeparture: OccurredAt.AddHours(3));
        booking.Confirm(OccurredAt.AddHours(-1));
        return booking;
    }

    private sealed class Fixture
    {
        private IReadOnlyList<BookingEntity> _confirmedBookings = [];

        public Fixture()
        {
            UnitOfWork.ExecuteInTransactionAsync(
                    Arg.Any<Func<Task<int>>>(),
                    Arg.Any<CancellationToken>())
                .Returns(call => call.Arg<Func<Task<int>>>()());
            PendingActions.AddAsync(
                    Arg.Do<BookingPendingAction>(AddedActions.Add),
                    Arg.Any<CancellationToken>())
                .Returns(call => call.Arg<BookingPendingAction>());
            Handler = new CreateRouteChangePendingActionCommandHandler(
                Bookings,
                PendingActions,
                Scheduler,
                UnitOfWork);
        }

        public Guid EventId { get; } = Guid.NewGuid();
        public IBookingRepository Bookings { get; } = Substitute.For<IBookingRepository>();
        public IBookingPendingActionRepository PendingActions { get; } =
            Substitute.For<IBookingPendingActionRepository>();
        public IRouteChangeExpiryScheduler Scheduler { get; } =
            Substitute.For<IRouteChangeExpiryScheduler>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public List<BookingPendingAction> AddedActions { get; } = [];
        public CreateRouteChangePendingActionCommandHandler Handler { get; }

        public void SetConfirmedBookings(params BookingEntity[] bookings)
        {
            var first = bookings[0];
            bookings.Should().OnlyContain(booking =>
                booking.TripId == first.TripId && booking.OperatorId == first.OperatorId);
            _confirmedBookings = bookings;
            Bookings.GetConfirmedByTripAsync(
                    first.TripId,
                    first.OperatorId,
                    Arg.Any<CancellationToken>())
                .Returns(bookings);
        }

        public Task<int> HandleAsync(params RouteChangeAffectedBooking[] affectedBookings)
        {
            var first = affectedBookings[0];
            var booking = GetBooking(first.BookingId);
            return Handler.Handle(new CreateRouteChangePendingActionCommand(
                EventId,
                OccurredAt,
                booking.TripId,
                booking.OperatorId,
                "IN_PROGRESS",
                Guid.NewGuid(),
                affectedBookings), CancellationToken.None);
        }

        private BookingEntity GetBooking(Guid bookingId)
            => _confirmedBookings.Single(booking => booking.Id == bookingId);
    }
}
