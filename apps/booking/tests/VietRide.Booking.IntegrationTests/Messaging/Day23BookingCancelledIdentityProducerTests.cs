using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.Bookings.CancelBooking;
using VietRide.Booking.Application.Features.Bookings.HandleTripCancelled;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.IntegrationTests.Messaging;

public sealed class Day23BookingCancelledIdentityProducerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-17T00:00:00Z");
    private static readonly Guid PassengerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TripId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OperatorId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task CancelBookingHandler_UsesExplicitOutboxIdentityInCanonicalPayload()
    {
        var booking = CreateBooking(BookingStatus.CONFIRMED);
        booking.AddPassenger("A01");
        var fixture = new Fixture();
        fixture.Bookings.FindByIdWithPassengersAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
        fixture.Bookings.TryCancelAsync(booking.Id, BookingCancellationReason.USER_INITIATED, Now, false, Arg.Any<CancellationToken>()).Returns(true);
        fixture.TripClient.GetTripSnapshotAsync(TripId, Arg.Any<CancellationToken>()).Returns(CreateTrip());
        fixture.OperatorClient.GetOperatorAsync(OperatorId, Arg.Any<CancellationToken>()).Returns((OperatorLookup?)null);

        await fixture.CancelHandler.Handle(
            new CancelBookingCommand(booking.Id, PassengerId, "cancel-identity", "USER_INITIATED"),
            CancellationToken.None);

        AssertPayloadUsesOutboxIdentity(fixture.OutboxRows.Should().ContainSingle().Which);
    }

    [Fact]
    public async Task TripCancelledHandler_UsesExplicitOutboxIdentityInCanonicalPayload()
    {
        var booking = CreateBooking(BookingStatus.CONFIRMED);
        var fixture = new Fixture();
        fixture.Bookings.GetCancellableByTripAsync(TripId, OperatorId, Arg.Any<CancellationToken>()).Returns([booking]);

        await fixture.TripCancelledHandler.Handle(
            new HandleTripCancelledCommand(
                Guid.NewGuid(), Now, TripId, OperatorId, Now,
                HandleTripCancelledCommandHandler.DriverScheduleDayRemovedReason),
            CancellationToken.None);

        AssertPayloadUsesOutboxIdentity(fixture.OutboxRows.Should().ContainSingle().Which);
    }

    private static void AssertPayloadUsesOutboxIdentity(OutboxRow row)
    {
        row.EventId.Should().NotBeEmpty();
        using var json = JsonDocument.Parse(row.PayloadJson);
        json.RootElement.GetProperty("eventId").GetGuid().Should().Be(row.EventId);
        json.RootElement.GetProperty("occurredAt").GetDateTimeOffset().Should().Be(Now);
    }

    private static BookingEntity CreateBooking(BookingStatus status)
    {
        var booking = BookingEntity.CreatePendingPayment(
            BookingCode.Generate(Now), PassengerId, TripId, OperatorId, Guid.NewGuid(), null, null, null,
            Money.FromRaw(120_000), Money.Zero, Money.FromRaw(120_000));
        if (status == BookingStatus.CONFIRMED)
        {
            booking.Confirm(Now.AddMinutes(-1));
        }

        return booking;
    }

    private static TripSnapshot CreateTrip()
        => new(
            TripId, OperatorId, Guid.NewGuid(), Guid.NewGuid(), "SCHEDULED", Now.AddHours(24), Now.AddHours(28),
            120_000, new TripStationSnapshot(Guid.NewGuid(), "Origin"), new TripStationSnapshot(Guid.NewGuid(), "Destination"), [],
            new TripSeatSummary(40, 39));

    private sealed class Fixture
    {
        public Fixture()
        {
            Clock.UtcNow.Returns(Now);
            UnitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<Task<int>>>(), Arg.Any<CancellationToken>())
                .Returns(call => call.Arg<Func<Task<int>>>()());
            Outbox.EnqueueAsync(
                    Arg.Do<Guid>(eventId => CapturedEventId = eventId),
                    Arg.Any<string>(),
                    Arg.Do<string>(payload => CapturedPayload = payload),
                    Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
            CancelHandler = new CancelBookingCommandHandler(
                Bookings, TripClient, OperatorClient, Outbox, Clock,
                NullLogger<CancelBookingCommandHandler>.Instance, History);
            TripCancelledHandler = new HandleTripCancelledCommandHandler(Bookings, History, Outbox, UnitOfWork, Clock);
        }

        public IBookingRepository Bookings { get; } = Substitute.For<IBookingRepository>();
        public IBookingStatusHistoryRepository History { get; } = Substitute.For<IBookingStatusHistoryRepository>();
        public ITripServiceClient TripClient { get; } = Substitute.For<ITripServiceClient>();
        public IOperatorServiceClient OperatorClient { get; } = Substitute.For<IOperatorServiceClient>();
        public IIntegrationEventOutbox Outbox { get; } = Substitute.For<IIntegrationEventOutbox>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public IClock Clock { get; } = Substitute.For<IClock>();
        public CancelBookingCommandHandler CancelHandler { get; }
        public HandleTripCancelledCommandHandler TripCancelledHandler { get; }
        private Guid CapturedEventId { get; set; }
        private string? CapturedPayload { get; set; }
        public IReadOnlyList<OutboxRow> OutboxRows => CapturedPayload is null ? [] : [new(CapturedEventId, CapturedPayload)];
    }

    private sealed record OutboxRow(Guid EventId, string PayloadJson);
}
