using System.Text.Json;
using FluentAssertions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Incidents.ReportIncident;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Features.Incidents;

public sealed class ReportIncidentTests
{
    private static readonly Guid OperatorId = Guid.Parse("39000000-0000-4000-8000-000000000001");
    private static readonly Guid DriverId = Guid.Parse("39000000-0000-4000-8000-000000000002");
    private static readonly Guid AssistantId = Guid.Parse("39000000-0000-4000-8000-000000000003");

    [Fact]
    public async Task Handle_AssignedAssistantOnInProgressTrip_CreatesIncidentAndCanonicalOutbox()
    {
        var trip = CreateTrip(inProgress: true);
        var now = new DateTimeOffset(2026, 7, 16, 3, 0, 0, TimeSpan.Zero);
        var fixture = new HandlerFixture(trip, now);
        var originalStatus = trip.Status;

        var response = await fixture.Handler.Handle(
            new ReportIncidentCommand(
                trip.Id,
                AssistantId,
                "TRAFFIC_JAM",
                "  Kẹt xe tại nút giao  ",
                [" https://storage.example/incident.jpg "],
                10.7731m,
                106.7032m),
            CancellationToken.None);

        fixture.Incidents.Entities.Should().ContainSingle();
        var persisted = fixture.Incidents.Entities.Single();
        persisted.TripId.Should().Be(trip.Id);
        persisted.ReportedByUserId.Should().Be(AssistantId);
        persisted.Description.Should().Be("Kẹt xe tại nút giao");
        persisted.PhotoUrls.Should().Equal("https://storage.example/incident.jpg");
        persisted.ReportedAt.Should().Be(now);
        trip.Status.Should().Be(originalStatus);
        response.IncidentId.Should().Be(persisted.Id);
        fixture.Trips.GetForUpdateCount.Should().Be(1);
        fixture.Outbox.Events.Should().ContainSingle();
        fixture.Outbox.Events[0].EventType.Should().Be("trip.incident.reported");

        using var document = JsonDocument.Parse(fixture.Outbox.Events[0].Payload);
        var root = document.RootElement;
        root.GetProperty("eventId").GetGuid().Should().NotBe(Guid.Empty);
        root.GetProperty("occurredAt").GetDateTime().Should().Be(now.UtcDateTime);
        root.GetProperty("incidentId").GetGuid().Should().Be(persisted.Id);
        root.GetProperty("tripId").GetGuid().Should().Be(trip.Id);
        root.GetProperty("operatorId").GetGuid().Should().Be(OperatorId);
        root.GetProperty("reporterUserId").GetGuid().Should().Be(AssistantId);
        root.GetProperty("category").GetString().Should().Be("TRAFFIC_JAM");
    }

    [Fact]
    public async Task Handle_UnassignedCaller_ThrowsForbiddenWithoutSideEffects()
    {
        var trip = CreateTrip(inProgress: true);
        var fixture = new HandlerFixture(trip, DateTimeOffset.UtcNow);

        var action = () => fixture.Handler.Handle(
            ValidCommand(trip.Id) with { ReporterUserId = Guid.NewGuid() },
            CancellationToken.None);

        await action.Should().ThrowAsync<ForbiddenException>();
        fixture.Incidents.Entities.Should().BeEmpty();
        fixture.Outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_TripNotInProgress_ThrowsExactValidationCode()
    {
        var trip = CreateTrip(inProgress: false);
        var fixture = new HandlerFixture(trip, DateTimeOffset.UtcNow);

        var action = () => fixture.Handler.Handle(ValidCommand(trip.Id), CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedValidationException>()).Which;
        exception.ErrorCode.Should().Be("TRIP_NOT_IN_PROGRESS");
        fixture.Incidents.Entities.Should().BeEmpty();
        fixture.Outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_MissingTrip_ThrowsExactNotFoundCode()
    {
        var fixture = new HandlerFixture(null, DateTimeOffset.UtcNow);

        var action = () => fixture.Handler.Handle(ValidCommand(Guid.NewGuid()), CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedNotFoundException>()).Which;
        exception.ErrorCode.Should().Be("TRIP_NOT_FOUND");
        fixture.Incidents.Entities.Should().BeEmpty();
        fixture.Outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AbsentOptionalFields_OmitsThemFromEventPayload()
    {
        var trip = CreateTrip(inProgress: true);
        var fixture = new HandlerFixture(trip, DateTimeOffset.UtcNow);

        await fixture.Handler.Handle(ValidCommand(trip.Id), CancellationToken.None);

        using var document = JsonDocument.Parse(fixture.Outbox.Events.Single().Payload);
        var root = document.RootElement;
        root.TryGetProperty("description", out _).Should().BeFalse();
        root.TryGetProperty("photoUrls", out _).Should().BeFalse();
        root.TryGetProperty("latitude", out _).Should().BeFalse();
        root.TryGetProperty("longitude", out _).Should().BeFalse();
    }

    [Fact]
    public void Validator_LowercaseCategory_IsRejected()
    {
        var result = new ReportIncidentCommandValidator().Validate(
            ValidCommand(Guid.NewGuid()) with { Category = "traffic_jam" });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_DescriptionOverLimit_IsRejected()
    {
        var result = new ReportIncidentCommandValidator().Validate(
            ValidCommand(Guid.NewGuid()) with { Description = new string('x', 501) });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_NonHttpsPhotoUrl_IsRejected()
    {
        var result = new ReportIncidentCommandValidator().Validate(
            ValidCommand(Guid.NewGuid()) with { PhotoUrls = ["http://storage.example/photo.jpg"] });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_UnpairedCoordinates_AreRejected()
    {
        var result = new ReportIncidentCommandValidator().Validate(
            ValidCommand(Guid.NewGuid()) with { Latitude = 10.1m });

        result.IsValid.Should().BeFalse();
    }

    private static ReportIncidentCommand ValidCommand(Guid tripId)
        => new(
            tripId,
            DriverId,
            "OTHER",
            null,
            null,
            null,
            null);

    private static VietRide.Trip.Domain.Entities.Trip CreateTrip(bool inProgress)
    {
        var trip = VietRide.Trip.Domain.Entities.Trip.Create(
            OperatorId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            DriverId,
            AssistantId,
            null,
            DateTimeOffset.UtcNow.AddHours(1),
            DateTimeOffset.UtcNow.AddHours(5),
            TripSource.MANUAL,
            Money.FromRaw(100_000),
            1_000m,
            10m,
            false);
        if (inProgress)
        {
            trip.MarkBoarding(DateTimeOffset.UtcNow);
            trip.Start(DateTimeOffset.UtcNow);
        }

        return trip;
    }

    private sealed class HandlerFixture
    {
        public HandlerFixture(VietRide.Trip.Domain.Entities.Trip? trip, DateTimeOffset now)
        {
            Trips = new FakeTripRepository(trip);
            Incidents = new RecordingIncidentRepository();
            Outbox = new RecordingOutbox();
            Handler = new ReportIncidentCommandHandler(Trips, Incidents, Outbox, new FrozenClock(now));
        }

        public FakeTripRepository Trips { get; }
        public RecordingIncidentRepository Incidents { get; }
        public RecordingOutbox Outbox { get; }
        public ReportIncidentCommandHandler Handler { get; }
    }

    private sealed class FakeTripRepository : ITripRepository
    {
        private readonly VietRide.Trip.Domain.Entities.Trip? _trip;

        public FakeTripRepository(VietRide.Trip.Domain.Entities.Trip? trip)
        {
            _trip = trip;
        }

        public int GetForUpdateCount { get; private set; }

        public Task<VietRide.Trip.Domain.Entities.Trip?> GetForUpdateAsync(
            Guid tripId,
            CancellationToken cancellationToken)
        {
            GetForUpdateCount++;
            return Task.FromResult(_trip?.Id == tripId ? _trip : null);
        }

        public Task<VietRide.Trip.Domain.Entities.Trip?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
            => GetForUpdateAsync(id, cancellationToken);

        public Task<VietRide.Trip.Domain.Entities.Trip> AddAsync(
            VietRide.Trip.Domain.Entities.Trip entity,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Update(VietRide.Trip.Domain.Entities.Trip entity) => throw new NotSupportedException();
        public void Remove(VietRide.Trip.Domain.Entities.Trip entity) => throw new NotSupportedException();
        public IQueryable<VietRide.Trip.Domain.Entities.Trip> Query()
            => Array.Empty<VietRide.Trip.Domain.Entities.Trip>().AsQueryable();
        public IQueryable<VietRide.Trip.Domain.Entities.Trip> QueryNoTracking() => Query();
        public Task<VietRide.Trip.Domain.Entities.Trip?> GetWithSeatsAsync(
            Guid tripId,
            CancellationToken cancellationToken)
            => GetForUpdateAsync(tripId, cancellationToken);
    }

    private sealed class RecordingIncidentRepository : IIncidentRepository
    {
        public List<Incident> Entities { get; } = [];

        public Task<Incident?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Entities.SingleOrDefault(incident => incident.Id == id));

        public Task<Incident> AddAsync(Incident entity, CancellationToken cancellationToken = default)
        {
            Entities.Add(entity);
            return Task.FromResult(entity);
        }

        public void Update(Incident entity) { }
        public void Remove(Incident entity) => Entities.Remove(entity);
        public IQueryable<Incident> Query() => Entities.AsQueryable();
        public IQueryable<Incident> QueryNoTracking() => Query();
    }

    private sealed class RecordingOutbox : IIntegrationEventOutbox
    {
        public List<(string EventType, string Payload)> Events { get; } = [];

        public Task EnqueueAsync(
            string eventType,
            string payloadJson,
            CancellationToken cancellationToken = default)
        {
            Events.Add((eventType, payloadJson));
            return Task.CompletedTask;
        }
    }

    private sealed class FrozenClock : IClock
    {
        public FrozenClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }
}
