using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Booking.Application.Exceptions;
using VietRide.Booking.Application.Features.Bookings.HandleTripDisrupted;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Booking.Infrastructure.DependencyInjection;
using VietRide.Booking.Infrastructure.Messaging;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Messaging.DependencyInjection;
using VietRide.Shared.Messaging.RabbitMq;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.UnitTests.Features.Bookings;

public sealed class HandleTripDisruptedCommandHandlerTests
{
    private static readonly Guid TripId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OperatorId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OriginStationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid DestinationStationId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset TerminalAt = DateTimeOffset.Parse("2026-07-30T03:00:00Z");

    [Fact]
    public void DistancePath_UsesFarthestArrivedStopAndRoundsAwayFromZero()
    {
        var booking = CreateBooking(BookingStatus.CONFIRMED, 400_000);
        var trip = CreateTrip(
            1700d,
            Stop(1, 450d, "ARRIVED"),
            Stop(2, 970d, "ARRIVED"),
            Stop(3, 1400d, "PENDING"));

        var result = BookingDisruptionRefundCalculator.Calculate(booking, trip);

        result.TraveledRatio.Should().BeApproximately(970m / 1700m, 0.000000001m);
        result.RefundAmount.Should().Be(171_765);
    }

    [Fact]
    public void DistancePath_WhenPickupHasNotBeenPassed_RefundsFullAmount()
    {
        var pickup = Guid.NewGuid();
        var booking = CreateBooking(
            BookingStatus.CONFIRMED,
            300_000,
            pickupStationId: null,
            pickupStopId: pickup);
        var trip = CreateTrip(
            1700d,
            Stop(1, 450d, "ARRIVED", pickup),
            Stop(2, 970d, "PENDING"));

        var result = BookingDisruptionRefundCalculator.Calculate(booking, trip);

        result.TraveledRatio.Should().Be(0m);
        result.RefundAmount.Should().Be(300_000);
    }

    [Fact]
    public void PickupStopNotArrived_RefundsFullEvenIfACommissionedLaterStopLooksArrived()
    {
        var pickup = Guid.NewGuid();
        var booking = CreateBooking(
            BookingStatus.CONFIRMED,
            300_000,
            pickupStationId: null,
            pickupStopId: pickup);
        var trip = CreateTrip(
            1700d,
            Stop(1, 450d, "PENDING", pickup),
            Stop(2, 970d, "ARRIVED"));

        var result = BookingDisruptionRefundCalculator.Calculate(booking, trip);

        result.TraveledRatio.Should().Be(0m);
        result.RefundAmount.Should().Be(300_000);
    }

    [Fact]
    public void MissingRequiredDistance_FallsBackToDeterministicStopOrder()
    {
        var booking = CreateBooking(BookingStatus.CONFIRMED, 500_000);
        var trip = CreateTrip(
            totalDistanceKm: null,
            Stop(1, null, "ARRIVED"),
            Stop(2, null, "ARRIVED"),
            Stop(3, null, "PENDING"),
            Stop(4, null, "PENDING"));

        var result = BookingDisruptionRefundCalculator.Calculate(booking, trip);

        result.TraveledRatio.Should().Be(0.4m);
        result.RefundAmount.Should().Be(300_000);
    }

    [Fact]
    public void ExpressTripWithoutStops_UsesZeroProgressAndRefundsFullAmount()
    {
        var booking = CreateBooking(BookingStatus.CONFIRMED, 250_001);

        var result = BookingDisruptionRefundCalculator.Calculate(
            booking,
            CreateTrip(totalDistanceKm: null));

        result.TraveledRatio.Should().Be(0m);
        result.RefundAmount.Should().Be(250_001);
    }

    [Fact]
    public void NonPositiveDistanceDenominator_UsesExplicitZeroProgressRule()
    {
        var pickup = Guid.NewGuid();
        var booking = CreateBooking(
            BookingStatus.CONFIRMED,
            250_000,
            pickupStationId: null,
            pickupStopId: pickup);
        var trip = CreateTrip(
            100d,
            Stop(1, 100d, "ARRIVED", pickup),
            Stop(2, 100d, "ARRIVED"));

        var result = BookingDisruptionRefundCalculator.Calculate(booking, trip);

        result.TraveledRatio.Should().Be(0m);
        result.RefundAmount.Should().Be(250_000);
    }

    [Fact]
    public async Task PartialNoShow_TransitionsAndWritesDistinctCanonicalFactsAtomically()
    {
        var booking = CreateBooking(BookingStatus.PARTIAL_NO_SHOW, 500_000);
        var fixture = new Fixture([booking], CreateTrip(
            totalDistanceKm: null,
            Stop(1, null, "ARRIVED"),
            Stop(2, null, "ARRIVED"),
            Stop(3, null, "PENDING"),
            Stop(4, null, "PENDING")));

        var command = Command();
        var affected = await fixture.Handler.Handle(command, CancellationToken.None);
        fixture.Bookings.GetDisruptionBookingsForUpdateAsync(
                TripId,
                OperatorId,
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<BookingEntity>());
        var replayAffected = await fixture.Handler.Handle(command, CancellationToken.None);

        affected.Should().Be(1);
        replayAffected.Should().Be(0);
        booking.Status.Should().Be(BookingStatus.DISRUPTED);
        booking.CancellationReason.Should().Be(
            BookingCancellationReason.OPERATOR_DISRUPTED_IN_PROGRESS);
        booking.RefundOverride.Should().BeTrue();
        booking.CancelledAt.Should().Be(TerminalAt);
        fixture.Rows.Should().HaveCount(2);
        fixture.Rows.Select(row => row.EventType).Should().BeEquivalentTo(
            ["booking.booking.cancelled", "booking.booking.disrupted"]);
        fixture.Rows.Select(row => row.EventId).Should().OnlyHaveUniqueItems();
        fixture.Rows.Should().OnlyContain(row =>
            ReadEventId(row.Payload) == row.EventId);

        using var cancelled = JsonDocument.Parse(
            fixture.Rows.Single(row => row.EventType == "booking.booking.cancelled").Payload);
        cancelled.RootElement.GetProperty("refundAmount").GetInt64().Should().Be(300_000);
        cancelled.RootElement.GetProperty("refundOverride").GetBoolean().Should().BeTrue();
        cancelled.RootElement.GetProperty("cancellationReason").GetString().Should().Be(
            "OPERATOR_DISRUPTED_IN_PROGRESS");

        using var disrupted = JsonDocument.Parse(
            fixture.Rows.Single(row => row.EventType == "booking.booking.disrupted").Payload);
        disrupted.RootElement.GetProperty("traveledRatio").GetDecimal().Should().Be(0.4m);
        disrupted.RootElement.GetProperty("refundAmount").GetInt64().Should().Be(300_000);
        disrupted.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(
            [
                "eventId",
                "occurredAt",
                "bookingId",
                "bookingCode",
                "tripId",
                "operatorId",
                "userId",
                "traveledRatio",
                "refundAmount",
                "cancellationReason",
            ]);

        await fixture.History.Received(1).AddAsync(
            Arg.Is<BookingStatusHistory>(history =>
                history.BookingId == booking.Id
                && history.Status == BookingStatus.DISRUPTED
                && history.OccurredAt == TerminalAt
                && history.Source == "DISRUPT_ON_TRIP_DISRUPTED"
                && history.ActorUserId == null
                && history.ReasonCode == "OPERATOR_DISRUPTED_IN_PROGRESS"),
            Arg.Any<CancellationToken>());
        await fixture.VoucherService.Received(1).CompensateAsync(
            booking.Id,
            Arg.Any<CancellationToken>());
        await fixture.UnitOfWork.Received(2).ExecuteInTransactionAsync(
            Arg.Any<Func<Task<int>>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoShowAndDuplicateDeliveries_AreNoOpsWithoutSnapshotOrOutbox()
    {
        var noShow = CreateBooking(BookingStatus.NO_SHOW, 100_000);
        var fixture = new Fixture([], CreateTrip(100d));

        var first = await fixture.Handler.Handle(Command(), CancellationToken.None);
        var replay = await fixture.Handler.Handle(Command(), CancellationToken.None);

        first.Should().Be(0);
        replay.Should().Be(0);
        noShow.Status.Should().Be(BookingStatus.NO_SHOW);
        fixture.Rows.Should().BeEmpty();
        await fixture.TripClient.DidNotReceiveWithAnyArgs()
            .GetOperationalTripSnapshotAsync(default, default);
    }

    [Fact]
    public async Task SubstitutionDisruption_IsIgnoredBeforeDatabaseAndSnapshotAccess()
    {
        var fixture = new Fixture([], CreateTrip(100d));

        var affected = await fixture.Handler.Handle(
            Command() with { HasSubstitution = true },
            CancellationToken.None);

        affected.Should().Be(0);
        await fixture.UnitOfWork.DidNotReceiveWithAnyArgs()
            .ExecuteInTransactionAsync<int>(default!, default);
        await fixture.Bookings.DidNotReceiveWithAnyArgs()
            .AcquireEventLockAsync(default, default);
    }

    [Fact]
    public async Task SnapshotFailure_FailsClosedWithoutStateHistoryOrOutbox()
    {
        var booking = CreateBooking(BookingStatus.CONFIRMED, 100_000);
        var fixture = new Fixture([booking], CreateTrip(100d));
        fixture.TripClient.GetOperationalTripSnapshotAsync(
                TripId,
                Arg.Any<CancellationToken>())
            .Returns<Task<TripSnapshot>>(_ =>
                throw new BookingUpstreamUnavailableException("Trip unavailable."));

        var act = () => fixture.Handler.Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<BookingUpstreamUnavailableException>();
        booking.Status.Should().Be(BookingStatus.CONFIRMED);
        fixture.Rows.Should().BeEmpty();
        await fixture.History.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await fixture.VoucherService.DidNotReceiveWithAnyArgs()
            .CompensateAsync(default, default);
    }

    [Fact]
    public void ConsumerRegistration_UsesCanonicalRoutingKey()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Trip:UseDevStub"] = "true",
                ["Identity:UseDevStub"] = "true",
                ["Payment:UseDevStub"] = "true",
                ["REDIS_URL"] = "localhost:6379",
                ["RabbitMq:ExchangeName"] = "vietride.events",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddVietRideMessaging(configuration);
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptions<RabbitMqConsumerOptions<TripDisruptedIntegrationEvent>>>()
            .Value.Value;

        options.QueueName.Should().Be("booking.trip-disrupted");
        options.BindingKeys.Should().Equal("trip.trip.disrupted");
    }

    [Fact]
    public void ConsumerContract_DeserializesExactTripPayload()
    {
        var eventId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            eventId,
            occurredAt = TerminalAt.UtcDateTime,
            tripId = TripId,
            operatorId = OperatorId,
            terminalAt = TerminalAt,
            hasSubstitution = false,
            reason = "Road closure",
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var integrationEvent = JsonSerializer.Deserialize<TripDisruptedIntegrationEvent>(
            payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        integrationEvent.Should().NotBeNull();
        integrationEvent!.EventId.Should().Be(eventId);
        integrationEvent.TripId.Should().Be(TripId);
        integrationEvent.OperatorId.Should().Be(OperatorId);
        integrationEvent.TerminalAt.Should().Be(TerminalAt);
        integrationEvent.HasSubstitution.Should().BeFalse();
        integrationEvent.Reason.Should().Be("Road closure");
    }

    [Fact]
    public void DomainRejectsNoShowDisruptionButAllowsDisruptedRefundCompletion()
    {
        var noShow = CreateBooking(BookingStatus.NO_SHOW, 100_000);
        var disrupted = CreateBooking(BookingStatus.CONFIRMED, 100_000);
        disrupted.Disrupt(TerminalAt);

        var act = () => noShow.Disrupt(TerminalAt);
        act.Should().Throw<InvalidOperationException>();

        disrupted.MarkRefunded(TerminalAt.AddMinutes(1));
        disrupted.Status.Should().Be(BookingStatus.REFUNDED);
        disrupted.RefundedAt.Should().Be(TerminalAt.AddMinutes(1));
    }

    private static HandleTripDisruptedCommand Command()
        => new(
            Guid.NewGuid(),
            TerminalAt,
            TripId,
            OperatorId,
            TerminalAt,
            HasSubstitution: false,
            "Road closure");

    private static Guid ReadEventId(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.GetProperty("eventId").GetGuid();
    }

    private static BookingEntity CreateBooking(
        BookingStatus status,
        long totalAmount,
        Guid? pickupStationId = null,
        Guid? pickupStopId = null)
    {
        var booking = BookingEntity.CreatePendingPayment(
            BookingCode.Generate(TerminalAt),
            Guid.NewGuid(),
            TripId,
            OperatorId,
            pickupStationId ?? (pickupStopId.HasValue ? null : OriginStationId),
            pickupStopId,
            null,
            null,
            Money.FromRaw(totalAmount),
            Money.Zero,
            Money.FromRaw(totalAmount));
        typeof(BookingEntity).GetProperty(nameof(BookingEntity.Status))!.SetValue(booking, status);
        return booking;
    }

    private static TripStopSnapshot Stop(
        int order,
        double? distance,
        string status,
        Guid? stopId = null)
        => new(
            stopId ?? Guid.NewGuid(),
            order,
            true,
            true,
            TerminalAt.AddHours(order),
            distance,
            null,
            Status: status);

    private static TripSnapshot CreateTrip(
        double? totalDistanceKm,
        params TripStopSnapshot[] stops)
        => new(
            TripId,
            OperatorId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "DISRUPTED",
            TerminalAt.AddHours(-5),
            TerminalAt.AddHours(5),
            100_000,
            new TripStationSnapshot(OriginStationId, "Origin"),
            new TripStationSnapshot(DestinationStationId, "Destination"),
            stops,
            new TripSeatSummary(40, 10),
            TotalDistanceKm: totalDistanceKm);

    private sealed class Fixture
    {
        public Fixture(IReadOnlyList<BookingEntity> bookings, TripSnapshot trip)
        {
            Bookings.AcquireEventLockAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
            Bookings.GetDisruptionBookingsForUpdateAsync(
                    TripId,
                    OperatorId,
                    Arg.Any<CancellationToken>())
                .Returns(bookings);
            TripClient.GetOperationalTripSnapshotAsync(
                    TripId,
                    Arg.Any<CancellationToken>())
                .Returns(trip);
            UnitOfWork.ExecuteInTransactionAsync(
                    Arg.Any<Func<Task<int>>>(),
                    Arg.Any<CancellationToken>())
                .Returns(call => call.Arg<Func<Task<int>>>()());
            Clock.UtcNow.Returns(TerminalAt.AddSeconds(1));
            Outbox.EnqueueAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    Rows.Add(new OutboxRow(
                        call.ArgAt<Guid>(0),
                        call.ArgAt<string>(1),
                        call.ArgAt<string>(2)));
                    return Task.CompletedTask;
                });
            Handler = new HandleTripDisruptedCommandHandler(
                Bookings,
                History,
                TripClient,
                VoucherService,
                Outbox,
                UnitOfWork,
                Clock);
        }

        public IBookingRepository Bookings { get; } = Substitute.For<IBookingRepository>();
        public IBookingStatusHistoryRepository History { get; } =
            Substitute.For<IBookingStatusHistoryRepository>();
        public ITripServiceClient TripClient { get; } = Substitute.For<ITripServiceClient>();
        public IVoucherService VoucherService { get; } = Substitute.For<IVoucherService>();
        public IIntegrationEventOutbox Outbox { get; } =
            Substitute.For<IIntegrationEventOutbox>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public IClock Clock { get; } = Substitute.For<IClock>();
        public List<OutboxRow> Rows { get; } = [];
        public HandleTripDisruptedCommandHandler Handler { get; }
    }

    private sealed record OutboxRow(Guid EventId, string EventType, string Payload);
}
