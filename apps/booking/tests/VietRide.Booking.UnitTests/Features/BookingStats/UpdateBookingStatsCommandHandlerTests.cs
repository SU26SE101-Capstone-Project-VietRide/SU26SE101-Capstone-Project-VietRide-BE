using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.BookingStats.GetAdminBookingStatsAggregate;
using VietRide.Booking.Application.Features.BookingStats.GetOperatorBookingStats;
using VietRide.Booking.Application.Features.BookingStats.UpdateBookingStats;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;
using BookingStatsEntity = VietRide.Booking.Domain.Entities.BookingStats;

namespace VietRide.Booking.UnitTests.Features.BookingStats;

public sealed class UpdateBookingStatsCommandHandlerTests
{
    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid TripId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly Guid PassengerUserId = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
    private static readonly Guid StationId = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-26T10:00:00Z");

    private readonly FakeBookingRepository _bookings = new();
    private readonly FakeBookingStatsRepository _stats = new();
    private readonly IOperatorServiceClient _operatorClient = Substitute.For<IOperatorServiceClient>();

    [Fact]
    public async Task Confirmed_IncrementsBookingRevenueSeatsAndSnapshotsOperatorName()
    {
        var booking = CreateBooking();
        booking.AddPassenger("A01");
        booking.AddPassenger("A02");
        booking.Confirm(Now);
        _bookings.Save(booking);
        _operatorClient.GetOperatorAsync(OperatorId, Arg.Any<CancellationToken>())
            .Returns(new OperatorLookup(
                OperatorId,
                "VietRide Express",
                "APPROVED",
                true,
                "ops@example.com",
                "+84901234567",
                "0312345678",
                "0312345678",
                CancellationPolicy: null));
        var handler = BuildHandler();

        var updated = await handler.Handle(
            new UpdateBookingStatsCommand(
                "booking.booking.confirmed",
                booking.Id,
                BookingStatsTransition.Confirmed,
                Amount: 200_000),
            CancellationToken.None);

        updated.Should().BeTrue();
        var row = _stats.Single();
        row.OperatorName.Should().Be("VietRide Express");
        row.StatDate.Should().Be(DateOnly.FromDateTime(Now.UtcDateTime));
        row.TotalBookings.Should().Be(1);
        row.TotalConfirmed.Should().Be(1);
        row.TotalRevenue.Amount.Should().Be(200_000);
        row.TotalSeatsBooked.Should().Be(2);
    }

    [Fact]
    public async Task BookingStatsMonth_ConfirmedAtUtcMonthBoundary_StoresVietnamDate()
    {
        var confirmedAt = DateTimeOffset.Parse("2026-01-31T18:00:00Z");
        var booking = CreateBooking();
        booking.Confirm(confirmedAt);
        _bookings.Save(booking);
        var handler = BuildHandler();

        await handler.Handle(
            new UpdateBookingStatsCommand(
                "booking.booking.confirmed",
                booking.Id,
                BookingStatsTransition.Confirmed,
                Amount: 200_000),
            CancellationToken.None);

        _stats.Single().StatDate.Should().Be(new DateOnly(2026, 2, 1));
    }

    [Fact]
    public async Task Cancelled_IncrementsOnlyCancelledCounter()
    {
        var booking = CreateBooking();
        booking.Confirm(Now.AddHours(-1));
        booking.Cancel(BookingCancellationReason.USER_INITIATED, Now);
        _bookings.Save(booking);
        var handler = BuildHandler();

        await handler.Handle(
            new UpdateBookingStatsCommand(
                "booking.booking.cancelled",
                booking.Id,
                BookingStatsTransition.Cancelled),
            CancellationToken.None);

        var row = _stats.Single();
        row.TotalCancelled.Should().Be(1);
        row.TotalRefunded.Should().Be(Money.Zero);
    }

    [Fact]
    public async Task Refunded_UsesAmountFieldAndDoesNotIncrementCancelled()
    {
        var booking = CreateBooking();
        booking.Confirm(Now.AddHours(-2));
        booking.Cancel(BookingCancellationReason.USER_INITIATED, Now.AddHours(-1));
        booking.MarkRefunded(Now);
        _bookings.Save(booking);
        var handler = BuildHandler();

        await handler.Handle(
            new UpdateBookingStatsCommand(
                "booking.booking.refunded",
                booking.Id,
                BookingStatsTransition.Refunded,
                Amount: 180_000),
            CancellationToken.None);

        var row = _stats.Single();
        row.TotalRefunded.Amount.Should().Be(180_000);
        row.TotalCancelled.Should().Be(0);
    }

    [Fact]
    public async Task ReplaySameEvent_DoesNotDoubleCount()
    {
        var booking = CreateBooking();
        booking.AddPassenger("A01");
        booking.Confirm(Now);
        _bookings.Save(booking);
        _operatorClient.GetOperatorAsync(OperatorId, Arg.Any<CancellationToken>())
            .Returns(new OperatorLookup(
                OperatorId,
                "VietRide Express",
                "APPROVED",
                true,
                "ops@example.com",
                "+84901234567",
                "0312345678",
                "0312345678",
                CancellationPolicy: null));
        var handler = BuildHandler();
        var command = new UpdateBookingStatsCommand(
            "booking.booking.confirmed",
            booking.Id,
            BookingStatsTransition.Confirmed,
            Amount: 200_000);

        var first = await handler.Handle(command, CancellationToken.None);
        _operatorClient.ClearReceivedCalls();
        var replay = await BuildHandler().Handle(command, CancellationToken.None);

        first.Should().BeTrue();
        replay.Should().BeFalse();
        await _operatorClient.DidNotReceive()
            .GetOperatorAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        var row = _stats.Single();
        row.TotalBookings.Should().Be(1);
        row.TotalConfirmed.Should().Be(1);
        row.TotalRevenue.Amount.Should().Be(200_000);
        row.TotalSeatsBooked.Should().Be(1);
    }

    [Fact]
    public async Task DeltaUpsert_AddsToExistingCountersWithoutOverwriting()
    {
        var existing = BookingStatsEntity.Create(
            OperatorId,
            DateOnly.FromDateTime(Now.UtcDateTime),
            TripId,
            "Existing Operator");
        existing.SetCounters(
            totalBookings: 5,
            totalConfirmed: 5,
            totalCancelled: 1,
            totalNoShow: 0,
            totalCompleted: 0,
            totalRevenue: Money.FromRaw(1_000_000),
            totalRefunded: Money.FromRaw(100_000),
            totalSeatsBooked: 8);
        _stats.Seed(existing);

        var booking = CreateBooking();
        booking.AddPassenger("A01");
        booking.AddPassenger("A02");
        booking.Confirm(Now);
        _bookings.Save(booking);
        var handler = BuildHandler();

        await handler.Handle(
            new UpdateBookingStatsCommand(
                "booking.booking.confirmed",
                booking.Id,
                BookingStatsTransition.Confirmed,
                Amount: 200_000),
            CancellationToken.None);

        var row = _stats.Single();
        row.OperatorName.Should().Be("Existing Operator");
        row.TotalBookings.Should().Be(6);
        row.TotalConfirmed.Should().Be(6);
        row.TotalCancelled.Should().Be(1);
        row.TotalRefunded.Amount.Should().Be(100_000);
        row.TotalRevenue.Amount.Should().Be(1_200_000);
        row.TotalSeatsBooked.Should().Be(10);
    }

    private UpdateBookingStatsCommandHandler BuildHandler()
        => new(
            _bookings,
            _stats,
            _operatorClient,
            NullLogger<UpdateBookingStatsCommandHandler>.Instance);

    private static BookingEntity CreateBooking()
    {
        var booking = BookingEntity.CreatePendingPayment(
            bookingCode: BookingCode.Generate(Now),
            passengerUserId: PassengerUserId,
            tripId: TripId,
            operatorId: OperatorId,
            pickupStationId: StationId,
            pickupStopId: null,
            dropoffStationId: null,
            dropoffStopId: null,
            baseFare: Money.FromRaw(200_000),
            discountAmount: Money.Zero,
            totalAmount: Money.FromRaw(200_000),
            tripSnapshotOriginName: "Ha Noi",
            tripSnapshotDestName: "Da Nang",
            tripSnapshotDeparture: Now.AddHours(25),
            tripSnapshotRouteName: null,
            bookingGroupId: null,
            tripDirection: null,
            seatLockToken: Guid.NewGuid());

        return booking;
    }

    private sealed class FakeBookingStatsRepository : IBookingStatsRepository
    {
        private readonly List<BookingStatsEntity> _rows = [];
        private readonly HashSet<(string EventType, Guid BookingId)> _processedEvents = [];

        public Task<BookingStatsEntity?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(_rows.FirstOrDefault(s => s.Id == id));

        public Task<BookingStatsEntity> AddAsync(BookingStatsEntity entity, CancellationToken ct)
        {
            _rows.Add(entity);
            return Task.FromResult(entity);
        }

        public void Update(BookingStatsEntity entity)
        {
        }

        public void Remove(BookingStatsEntity entity)
            => _rows.Remove(entity);

        public IQueryable<BookingStatsEntity> Query()
            => _rows.AsQueryable();

        public IQueryable<BookingStatsEntity> QueryNoTracking()
            => _rows.AsQueryable();

        public Task<bool> TryClaimProcessedEventAsync(
            string eventType,
            Guid bookingId,
            DateTimeOffset processedAt,
            CancellationToken ct = default)
            => Task.FromResult(_processedEvents.Add((eventType, bookingId)));

        public Task UpsertDeltaAsync(BookingStatsEntity delta, CancellationToken ct = default)
        {
            var current = _rows.FirstOrDefault(s => s.OperatorId == delta.OperatorId
                && s.StatDate == delta.StatDate
                && s.TripId == delta.TripId);

            if (current is null)
            {
                _rows.Add(delta);
                return Task.CompletedTask;
            }

            if (!string.IsNullOrWhiteSpace(delta.OperatorName))
            {
                current.SetOperatorName(delta.OperatorName);
            }

            current.SetCounters(
                current.TotalBookings + delta.TotalBookings,
                current.TotalConfirmed + delta.TotalConfirmed,
                current.TotalCancelled + delta.TotalCancelled,
                current.TotalNoShow + delta.TotalNoShow,
                current.TotalCompleted + delta.TotalCompleted,
                Money.FromRaw(current.TotalRevenue.Amount + delta.TotalRevenue.Amount),
                Money.FromRaw(current.TotalRefunded.Amount + delta.TotalRefunded.Amount),
                current.TotalSeatsBooked + delta.TotalSeatsBooked);
            return Task.CompletedTask;
        }

        public void Seed(BookingStatsEntity stats)
            => _rows.Add(stats);

        public BookingStatsEntity Single()
            => _rows.Single();

        public Task<IReadOnlyList<OperatorBookingStatsReadModel>> GetOperatorStatsAsync(
            Guid operatorId,
            DateOnly? from,
            DateOnly? to,
            string groupBy,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<AdminBookingStatsAggregateReadModel>> GetAdminAggregateStatsAsync(
            DateOnly? from,
            DateOnly? to,
            string groupBy,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeBookingRepository : IBookingRepository
    {
        private readonly Dictionary<Guid, BookingEntity> _bookings = new();

        public void Save(BookingEntity booking)
            => _bookings[booking.Id] = booking;

        public Task<BookingEntity?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(_bookings.GetValueOrDefault(id));

        public Task<BookingEntity> AddAsync(BookingEntity entity, CancellationToken ct)
        {
            Save(entity);
            return Task.FromResult(entity);
        }

        public void Update(BookingEntity entity)
            => Save(entity);

        public void Remove(BookingEntity entity)
            => _bookings.Remove(entity.Id);

        public IQueryable<BookingEntity> Query()
            => _bookings.Values.AsQueryable();

        public IQueryable<BookingEntity> QueryNoTracking()
            => _bookings.Values.AsQueryable();

        public Task<BookingEntity?> FindByBookingCodeAsync(string bookingCode, CancellationToken ct = default)
            => Task.FromResult(_bookings.Values.FirstOrDefault(b => b.BookingCode.Value == bookingCode));

        public Task<BookingEntity?> FindByTicketCodeWithPassengersAsync(string ticketCode, CancellationToken ct = default)
            => Task.FromResult(_bookings.Values.FirstOrDefault(b =>
                b.Tickets.Any(t => t.TicketCode.Value == ticketCode)));

        public Task<BookingEntity?> FindByIdAsync(Guid bookingId, CancellationToken ct = default)
            => GetByIdAsync(bookingId, ct);

        public Task<BookingEntity?> FindByIdWithPassengersAsync(Guid bookingId, CancellationToken ct = default)
            => GetByIdAsync(bookingId, ct);

        public Task<bool> HasConfirmedBookingAsync(Guid passengerUserId, CancellationToken ct = default)
            => Task.FromResult(_bookings.Values.Any(b => b.PassengerUserId == passengerUserId && b.Status == BookingStatus.CONFIRMED));

        public Task<BookingPaymentTransitionSnapshot?> GetPendingPaymentTransitionSnapshotAsync(
            Guid bookingId,
            CancellationToken ct = default)
            => Task.FromResult<BookingPaymentTransitionSnapshot?>(null);

        public Task<bool> TryConfirmPendingPaymentAsync(
            Guid bookingId,
            DateTimeOffset confirmedAt,
            CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> TryExpirePendingPaymentAsync(
            Guid bookingId,
            DateTimeOffset expiredAt,
            CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> TryCancelAsync(
            Guid bookingId,
            BookingCancellationReason reason,
            DateTimeOffset cancelledAt,
            bool refundOverride,
            CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> TryMarkCancelledRefundedAsync(
            Guid bookingId,
            DateTimeOffset refundedAt,
            CancellationToken ct = default)
            => Task.FromResult(false);
    }
}
