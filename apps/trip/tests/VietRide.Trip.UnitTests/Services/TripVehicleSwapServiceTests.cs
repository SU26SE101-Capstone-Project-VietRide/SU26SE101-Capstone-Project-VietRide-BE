using System.Text.Json;
using FluentAssertions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Events;
using VietRide.Trip.Application.Services;
using VietRide.Trip.Domain.Constants;
using VietRide.Trip.Domain.Entities;
using TripEntity = VietRide.Trip.Domain.Entities.Trip;

namespace VietRide.Trip.UnitTests.Services;

public sealed class TripVehicleSwapServiceTests
{
    public static IEnumerable<object[]> PassengerCompatibilityMatrix()
    {
        var types = new[]
        {
            TripSeatType.STANDARD,
            TripSeatType.SLEEPER_UPPER,
            TripSeatType.SLEEPER_LOWER,
            TripSeatType.VIP,
        };
        var statuses = new[]
        {
            TripSeatStatus.AVAILABLE,
            TripSeatStatus.HELD,
            TripSeatStatus.BOOKED,
        };

        foreach (var status in statuses)
            foreach (var oldType in types)
                foreach (var newType in types)
                {
                    yield return [status, oldType, newType];
                }
    }

    [Theory]
    [MemberData(nameof(PassengerCompatibilityMatrix))]
    public async Task StageSwapAsync_AppliesExactPassengerRankMatrix(
        TripSeatStatus status,
        TripSeatType oldType,
        TripSeatType newType)
    {
        var fixture = Fixture.Create(oldType, status, NewSeat("A01", newType));
        var downgraded = Rank(newType) < Rank(oldType);
        var impacts = status == TripSeatStatus.BOOKED && downgraded
            ? new[]
            {
                new VehicleSwapBookingSeatImpact(
                    Guid.NewGuid(),
                    ["A01"],
                    VehicleSwapBookingSeatImpact.SeatTypeDowngraded),
            }
            : [];

        var changed = await fixture.Service.StageSwapAsync(
            fixture.Trip,
            fixture.OldVehicle,
            fixture.NewVehicle,
            [fixture.Seat],
            impacts,
            fixture.ActorUserId,
            TripAuditAction.TripVehicleSwapped,
            "request-1",
            fixture.OccurredAt,
            CancellationToken.None);

        changed.Should().BeTrue();
        fixture.Trip.VehicleId.Should().Be(fixture.NewVehicle.Id);
        fixture.Seat.Status.Should().Be(status);
        if (status == TripSeatStatus.AVAILABLE)
        {
            fixture.Seat.SeatType.Should().Be(newType);
        }
        else
        {
            fixture.Seat.SeatType.Should().Be(oldType);
        }

        fixture.Seats.Removed.Should().BeEmpty();
        fixture.Outbox.Items.Should().ContainSingle();
        using var payload = JsonDocument.Parse(fixture.Outbox.Items[0].Payload);
        payload.RootElement.GetProperty("seatImpacts").GetArrayLength()
            .Should().Be(status == TripSeatStatus.BOOKED && downgraded ? 1 : 0);
    }

    [Theory]
    [InlineData(TripSeatStatus.AVAILABLE, "ABSENT", true)]
    [InlineData(TripSeatStatus.AVAILABLE, "DISABLED", true)]
    [InlineData(TripSeatStatus.AVAILABLE, "DRIVER_AREA", true)]
    [InlineData(TripSeatStatus.HELD, "ABSENT", false)]
    [InlineData(TripSeatStatus.HELD, "DISABLED", false)]
    [InlineData(TripSeatStatus.HELD, "DRIVER_AREA", false)]
    [InlineData(TripSeatStatus.BOOKED, "ABSENT", false)]
    [InlineData(TripSeatStatus.BOOKED, "DISABLED", false)]
    [InlineData(TripSeatStatus.BOOKED, "DRIVER_AREA", false)]
    public async Task StageSwapAsync_CoversAbsentDisabledAndDriverArea(
        TripSeatStatus status,
        string scenario,
        bool removesAvailableSeat)
    {
        var layoutSeats = scenario switch
        {
            "ABSENT" => Array.Empty<object>(),
            "DISABLED" => [NewSeat("A01", TripSeatType.STANDARD, disabled: true)],
            "DRIVER_AREA" => [NewSeat("A01", TripSeatType.DRIVER_AREA)],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        var fixture = Fixture.Create(TripSeatType.VIP, status, layoutSeats);
        var reason = scenario switch
        {
            "ABSENT" => VehicleSwapBookingSeatImpact.SeatRemoved,
            _ => VehicleSwapBookingSeatImpact.SeatDisabled,
        };
        var impacts = status == TripSeatStatus.BOOKED
            ? new[] { new VehicleSwapBookingSeatImpact(Guid.NewGuid(), ["A01"], reason) }
            : [];

        await fixture.Service.StageSwapAsync(
            fixture.Trip,
            fixture.OldVehicle,
            fixture.NewVehicle,
            [fixture.Seat],
            impacts,
            fixture.ActorUserId,
            TripAuditAction.TripVehicleSwapped,
            "request-1",
            fixture.OccurredAt,
            CancellationToken.None);

        fixture.Seats.Removed.Contains(fixture.Seat).Should().Be(removesAvailableSeat);
        if (!removesAvailableSeat)
        {
            fixture.Seat.Status.Should().Be(status);
            fixture.Seat.SeatType.Should().Be(TripSeatType.VIP);
        }

        fixture.Seats.Added.Should().NotContain(seat => seat.SeatType == TripSeatType.DRIVER_AREA);
    }

    [Fact]
    public async Task StageSwapAsync_ReconcilesAvailableSeatsInDeterministicOrder()
    {
        var fixture = Fixture.Create(
            TripSeatType.STANDARD,
            TripSeatStatus.AVAILABLE,
            NewSeat("C03", TripSeatType.VIP),
            NewSeat("B02", TripSeatType.SLEEPER_UPPER));

        await fixture.Service.StageSwapAsync(
            fixture.Trip,
            fixture.OldVehicle,
            fixture.NewVehicle,
            [fixture.Seat],
            [],
            fixture.ActorUserId,
            TripAuditAction.TripVehicleSwapped,
            "request-1",
            fixture.OccurredAt,
            CancellationToken.None);

        fixture.Seats.Removed.Should().ContainSingle().Which.Should().BeSameAs(fixture.Seat);
        fixture.Seats.Added.Select(seat => seat.SeatNumber).Should().Equal("B02", "C03");
    }

    [Theory]
    [InlineData(TripAuditAction.TripVehicleSwapped)]
    [InlineData(TripAuditAction.DriverScheduleCascadeApplied)]
    public async Task StageSwapAsync_StagesExactEventAndAuditWithoutSaving(string auditAction)
    {
        var fixture = Fixture.Create(TripSeatType.VIP, TripSeatStatus.BOOKED);
        var bookingId = Guid.NewGuid();

        await fixture.Service.StageSwapAsync(
            fixture.Trip,
            fixture.OldVehicle,
            fixture.NewVehicle,
            [fixture.Seat],
            [new VehicleSwapBookingSeatImpact(bookingId, ["A01"], VehicleSwapBookingSeatImpact.SeatRemoved)],
            fixture.ActorUserId,
            auditAction,
            "  request-1  ",
            fixture.OccurredAt,
            CancellationToken.None);

        fixture.Audits.Items.Should().ContainSingle();
        var audit = fixture.Audits.Items.Single();
        audit.Action.Should().Be(auditAction);
        audit.Metadata.Should().NotBeNull();
        var metadata = audit.Metadata!.Value;
        metadata.EnumerateObject().Select(property => property.Name)
            .Should().Equal("changedFields", "before", "after", "requestId");
        metadata.GetProperty("changedFields").EnumerateArray().Select(item => item.GetString())
            .Should().Equal("vehicleId");
        var before = metadata.GetProperty("before");
        before.EnumerateObject().Select(property => property.Name).Should().Equal("vehicleId");
        before.GetProperty("vehicleId").GetGuid().Should().Be(fixture.OldVehicle.Id);
        var after = metadata.GetProperty("after");
        after.EnumerateObject().Select(property => property.Name).Should().Equal("vehicleId");
        after.GetProperty("vehicleId").GetGuid().Should().Be(fixture.NewVehicle.Id);
        metadata.GetProperty("requestId").GetString().Should().Be("request-1");
        fixture.Outbox.Items.Should().ContainSingle(item => item.Type == TripVehicleSwappedIntegrationEvent.EventTypeValue);
        using var payload = JsonDocument.Parse(fixture.Outbox.Items[0].Payload);
        var root = payload.RootElement;
        root.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            "eventId",
            "occurredAt",
            "tripId",
            "operatorId",
            "oldVehicleId",
            "newVehicleId",
            "oldVehiclePlateNumber",
            "newVehiclePlateNumber",
            "departureDateTime",
            "driverUserId",
            "assistantUserId",
            "seatImpacts");
        root.TryGetProperty("eventType", out _).Should().BeFalse();
        root.GetProperty("assistantUserId").ValueKind.Should().Be(JsonValueKind.Null);
        var impact = root.GetProperty("seatImpacts").EnumerateArray().Single();
        impact.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo("bookingId", "seatNumbers", "reason");
        impact.GetProperty("bookingId").GetGuid().Should().Be(bookingId);
    }

    [Fact]
    public async Task StageSwapAsync_SameVehicleIsNoOp()
    {
        var fixture = Fixture.Create(TripSeatType.STANDARD, TripSeatStatus.AVAILABLE);

        var changed = await fixture.Service.StageSwapAsync(
            fixture.Trip,
            fixture.OldVehicle,
            fixture.OldVehicle,
            [fixture.Seat],
            [],
            fixture.ActorUserId,
            TripAuditAction.TripVehicleSwapped,
            "request-1",
            fixture.OccurredAt,
            CancellationToken.None);

        changed.Should().BeFalse();
        fixture.Seats.Added.Should().BeEmpty();
        fixture.Seats.Removed.Should().BeEmpty();
        fixture.Audits.Items.Should().BeEmpty();
        fixture.Outbox.Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData(TripAuditAction.TripEdited, "request-1")]
    [InlineData(TripAuditAction.TripVehicleSwapped, " ")]
    public async Task StageSwapAsync_ValidatesAuditInputsBeforeSameVehicleNoOp(
        string auditAction,
        string requestId)
    {
        var fixture = Fixture.Create(TripSeatType.STANDARD, TripSeatStatus.AVAILABLE);

        var action = () => fixture.Service.StageSwapAsync(
            fixture.Trip,
            fixture.OldVehicle,
            fixture.OldVehicle,
            [fixture.Seat],
            [],
            fixture.ActorUserId,
            auditAction,
            requestId,
            fixture.OccurredAt,
            CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>();
        fixture.Trip.VehicleId.Should().Be(fixture.OldVehicle.Id);
        fixture.Seats.Added.Should().BeEmpty();
        fixture.Seats.Removed.Should().BeEmpty();
        fixture.Audits.Items.Should().BeEmpty();
        fixture.Outbox.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task StageSwapAsync_RejectsUnknownSeatTypeBeforeMutation()
    {
        var fixture = Fixture.Create(
            TripSeatType.STANDARD,
            TripSeatStatus.AVAILABLE,
            new { seatNumber = "A01", row = 1, col = 1, deck = 1, type = "ECONOMY", isWindow = false, isAisle = false, disabled = false });

        var action = () => fixture.Service.StageSwapAsync(
            fixture.Trip,
            fixture.OldVehicle,
            fixture.NewVehicle,
            [fixture.Seat],
            [],
            fixture.ActorUserId,
            TripAuditAction.TripVehicleSwapped,
            "request-1",
            fixture.OccurredAt,
            CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>().WithMessage("*Unknown seat type*");
        fixture.Audits.Items.Should().BeEmpty();
        fixture.Outbox.Items.Should().BeEmpty();
    }

    [Fact]
    public void ContractAndConstructor_ExposeNoHiddenHttpOrCommitDependency()
    {
        typeof(TripVehicleSwapService).GetConstructors().Should().ContainSingle()
            .Which.GetParameters().Select(parameter => parameter.ParameterType)
            .Should().BeEquivalentTo(new[]
            {
                typeof(ITripSeatRepository),
                typeof(ITripAuditLogRepository),
                typeof(IIntegrationEventOutbox),
            });
        typeof(ITripVehicleSwapService).GetMethod(nameof(ITripVehicleSwapService.StageSwapAsync))!
            .GetParameters().Select(parameter => parameter.ParameterType)
            .Should().Contain(typeof(IReadOnlyCollection<VehicleSwapBookingSeatImpact>));
    }

    [Fact]
    public void BookingImpact_RejectsUnknownReasonAndDefensivelyCopiesSeats()
    {
        var source = new List<string> { " a01 " };
        var impact = new VehicleSwapBookingSeatImpact(
            Guid.NewGuid(),
            source,
            VehicleSwapBookingSeatImpact.SeatRemoved);
        source[0] = "B02";

        impact.SeatNumbers.Should().Equal("A01");
        var action = () => new VehicleSwapBookingSeatImpact(Guid.NewGuid(), ["A01"], "VEHICLE_CHANGED");
        action.Should().Throw<ArgumentException>();
    }

    private static object NewSeat(string seatNumber, TripSeatType type, bool disabled = false) => new
    {
        seatNumber,
        row = 1,
        col = 1,
        deck = 1,
        type = type.ToString(),
        isWindow = false,
        isAisle = false,
        disabled,
    };

    private static int Rank(TripSeatType type) => type switch
    {
        TripSeatType.STANDARD => 0,
        TripSeatType.SLEEPER_UPPER => 1,
        TripSeatType.SLEEPER_LOWER => 2,
        TripSeatType.VIP => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private sealed class Fixture
    {
        private Fixture(
            TripEntity trip,
            Vehicle oldVehicle,
            Vehicle newVehicle,
            TripSeat seat,
            Guid actorUserId,
            DateTimeOffset occurredAt,
            RecordingTripSeatRepository seats,
            RecordingAuditRepository audits,
            RecordingOutbox outbox)
        {
            Trip = trip;
            OldVehicle = oldVehicle;
            NewVehicle = newVehicle;
            Seat = seat;
            ActorUserId = actorUserId;
            OccurredAt = occurredAt;
            Seats = seats;
            Audits = audits;
            Outbox = outbox;
            Service = new TripVehicleSwapService(seats, audits, outbox);
        }

        public TripEntity Trip { get; }
        public Vehicle OldVehicle { get; }
        public Vehicle NewVehicle { get; }
        public TripSeat Seat { get; }
        public Guid ActorUserId { get; }
        public DateTimeOffset OccurredAt { get; }
        public RecordingTripSeatRepository Seats { get; }
        public RecordingAuditRepository Audits { get; }
        public RecordingOutbox Outbox { get; }
        public TripVehicleSwapService Service { get; }

        public static Fixture Create(
            TripSeatType oldType,
            TripSeatStatus status,
            params object[] newLayoutSeats)
        {
            var operatorId = Guid.NewGuid();
            var oldVehicle = CreateVehicle(operatorId, "51A-111.11", [NewSeat("A01", oldType)]);
            var newVehicle = CreateVehicle(operatorId, "51A-222.22", newLayoutSeats);
            var departure = new DateTimeOffset(2026, 7, 20, 1, 0, 0, TimeSpan.Zero);
            var trip = TripEntity.Create(
                operatorId,
                Guid.NewGuid(),
                oldVehicle.Id,
                Guid.NewGuid(),
                null,
                null,
                departure,
                departure.AddHours(4),
                TripSource.MANUAL,
                Money.FromRaw(100_000),
                null,
                0m);
            var seat = TripSeat.Create(trip.Id, "A01", oldType);
            if (status is TripSeatStatus.HELD or TripSeatStatus.BOOKED)
            {
                seat.MarkHeld();
            }

            if (status == TripSeatStatus.BOOKED)
            {
                seat.MarkBooked();
            }

            return new Fixture(
                trip,
                oldVehicle,
                newVehicle,
                seat,
                Guid.NewGuid(),
                new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero),
                new RecordingTripSeatRepository(),
                new RecordingAuditRepository(),
                new RecordingOutbox());
        }

        private static Vehicle CreateVehicle(Guid operatorId, string plate, IReadOnlyCollection<object> seats)
        {
            var layout = JsonSerializer.SerializeToElement(new
            {
                version = 1,
                vehicleTypeCode = "TEST",
                totalSeats = Math.Max(1, seats.Count),
                rows = 1,
                cols = Math.Max(1, seats.Count),
                decks = 1,
                aisles = Array.Empty<object>(),
                seats,
            });
            return Vehicle.Create(operatorId, Guid.NewGuid(), plate, layout, Math.Max(1, seats.Count), null, null);
        }
    }

    private sealed class RecordingTripSeatRepository : ITripSeatRepository
    {
        public List<TripSeat> Added { get; } = [];
        public List<TripSeat> Removed { get; } = [];

        public Task<TripSeat?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<TripSeat?>(null);
        public Task<TripSeat> AddAsync(TripSeat entity, CancellationToken ct = default)
        {
            Added.Add(entity);
            return Task.FromResult(entity);
        }

        public void Update(TripSeat entity) { }
        public void Remove(TripSeat entity) => Removed.Add(entity);
        public IQueryable<TripSeat> Query() => Added.AsQueryable();
        public IQueryable<TripSeat> QueryNoTracking() => Query();
    }

    private sealed class RecordingAuditRepository : ITripAuditLogRepository
    {
        public List<TripAuditLog> Items { get; } = [];
        public Task AddAsync(TripAuditLog auditLog, CancellationToken cancellationToken = default)
        {
            Items.Add(auditLog);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TripAuditLog>> ListByTripIdAsync(
            Guid tripId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TripAuditLog>>(Items.Where(item => item.TripId == tripId).ToArray());
    }

    private sealed class RecordingOutbox : IIntegrationEventOutbox
    {
        public List<(string Type, string Payload)> Items { get; } = [];
        public Task EnqueueAsync(string eventType, string payloadJson, CancellationToken ct = default)
        {
            Items.Add((eventType, payloadJson));
            return Task.CompletedTask;
        }
    }
}
