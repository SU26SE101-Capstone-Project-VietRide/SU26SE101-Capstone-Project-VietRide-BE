using System.Text.Json;
using FluentAssertions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Stops;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Features.Stops;

public sealed class Day24StopDisableProducerTests
{
    [Fact]
    public async Task DisableStop_EmitsStableTripStopDisabledEventAndPreservesDeletedAt()
    {
        var stop = Stop.Create(Guid.NewGuid(), "A", 1, 2);
        var repo = new FakeStopRepository(stop);
        var outbox = new FakeOutbox();
        var response = await Create(repo, outbox).Handle(new DisableStopCommand(stop.OperatorId, stop.Id, null), default);

        response.Warning.Should().BeNull();
        stop.IsActive.Should().BeFalse();
        stop.DeletedAt.Should().BeNull();
        outbox.EventType.Should().Be("trip.stop.disabled");
        outbox.EventId.Should().NotBeEmpty();
        using var payload = JsonDocument.Parse(outbox.Payload!);
        var json = payload.RootElement;
        json.GetProperty("eventId").GetGuid().Should().Be(outbox.EventId);
        json.GetProperty("eventId").GetGuid().Should().NotBe(stop.Id);
        json.GetProperty("eventType").GetString().Should().Be("trip.stop.disabled");
        json.GetProperty("stopId").GetGuid().Should().Be(stop.Id);
        json.GetProperty("operatorId").GetGuid().Should().Be(stop.OperatorId);
        json.GetProperty("occurredAt").GetDateTime().Should().BeCloseTo(new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(1));
        json.GetProperty("replacedByStopId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task DisableStop_WithDifferentReplacementAfterDisable_ConflictsWithoutSecondEvent()
    {
        var replacement = Stop.Create(Guid.NewGuid(), "B", 1, 2);
        var stop = Stop.Create(replacement.OperatorId, "A", 1, 2);
        var repo = new FakeStopRepository(stop, replacement);
        var outbox = new FakeOutbox();
        var handler = Create(repo, outbox);
        await handler.Handle(new DisableStopCommand(stop.OperatorId, stop.Id, replacement.Id), default);

        var replay = await handler.Handle(new DisableStopCommand(stop.OperatorId, stop.Id, replacement.Id), default);
        replay.Warning.Should().BeNull();
        outbox.EnqueueCount.Should().Be(1);

        var act = () => handler.Handle(new DisableStopCommand(stop.OperatorId, stop.Id, null), default);
        var error = await act.Should().ThrowAsync<VietRide.Shared.Application.Exceptions.CodedConflictException>();
        error.Which.ErrorCode.Should().Be("STOP_ALREADY_DISABLED");
        outbox.EnqueueCount.Should().Be(1);
    }

    [Fact]
    public async Task DisableStop_WhenOperatorIsNotEligible_RejectsWithoutMutationOrOutbox()
    {
        var stop = Stop.Create(Guid.NewGuid(), "A", 1, 2);
        var repo = new FakeStopRepository(stop);
        var outbox = new FakeOutbox();
        var handler = new DisableStopHandler(repo, new FakeIdentity(OperatorWriteEligibilityValidation.Forbidden("inactive")), outbox, new FixedClock());

        var act = () => handler.Handle(new DisableStopCommand(stop.OperatorId, stop.Id, null), default);
        var error = await act.Should().ThrowAsync<VietRide.Shared.Application.Exceptions.ForbiddenException>();
        error.Which.ErrorCode.Should().Be("FORBIDDEN");
        stop.IsActive.Should().BeTrue();
        outbox.EnqueueCount.Should().Be(0);
    }

    [Fact]
    public async Task DisableStop_WithInactiveReplacement_RejectsAndDoesNotEmit()
    {
        var stop = Stop.Create(Guid.NewGuid(), "A", 1, 2);
        var replacement = Stop.Create(stop.OperatorId, "B", 1, 2);
        replacement.Deactivate();
        var outbox = new FakeOutbox();
        var act = () => Create(new FakeStopRepository(stop, replacement), outbox)
            .Handle(new DisableStopCommand(stop.OperatorId, stop.Id, replacement.Id), default);
        var error = await act.Should().ThrowAsync<VietRide.Shared.Application.Exceptions.CodedValidationException>();
        error.Which.ErrorCode.Should().Be("STOP_REPLACEMENT_INVALID");
        outbox.EnqueueCount.Should().Be(0);
    }

    [Fact]
    public void DisableStopHandler_HasNoSynchronousBookingImpactDependency()
    {
        typeof(DisableStopHandler).GetConstructors().Single().GetParameters()
            .Should().NotContain(parameter => parameter.ParameterType.Name.Contains("BookingImpact"));
    }

    private static DisableStopHandler Create(FakeStopRepository repo, FakeOutbox outbox)
        => new(repo, new FakeIdentity(OperatorWriteEligibilityValidation.Allowed()), outbox, new FixedClock());

    private sealed class FakeStopRepository(params Stop[] values) : IStopRepository
    {
        private readonly List<Stop> items = values.ToList();
        public Task<Stop?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(items.FirstOrDefault(x => x.Id == id));
        public Task<Stop> AddAsync(Stop entity, CancellationToken ct) { items.Add(entity); return Task.FromResult(entity); }
        public void Update(Stop entity) { }
        public void Remove(Stop entity) => items.Remove(entity);
        public IQueryable<Stop> Query() => items.AsQueryable();
        public IQueryable<Stop> QueryNoTracking() => items.AsQueryable();
    }

    private sealed class FakeIdentity : IIdentityInternalClient
    {
        private readonly OperatorWriteEligibilityValidation eligibility;
        public FakeIdentity(OperatorWriteEligibilityValidation eligibility) => this.eligibility = eligibility;
        public Task<OperatorWriteEligibilityValidation> ValidateOperatorCanWriteAsync(Guid operatorId, CancellationToken cancellationToken = default) => Task.FromResult(eligibility);
        public Task<IdentityUserLookupResult> GetUserAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(IdentityUserLookupResult.ValidationFailure("unused"));
    }

    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow => new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero); }

    private sealed class FakeOutbox : IIntegrationEventOutbox
    {
        public Guid EventId { get; private set; }
        public string? EventType { get; private set; }
        public string? Payload { get; private set; }
        public int EnqueueCount { get; private set; }
        public Task EnqueueAsync(Guid eventId, string eventType, string payloadJson, CancellationToken ct = default)
        { EventId = eventId; EventType = eventType; Payload = payloadJson; EnqueueCount++; return Task.CompletedTask; }
        public Task EnqueueAsync(string eventType, string payloadJson, CancellationToken ct = default) => EnqueueAsync(Guid.NewGuid(), eventType, payloadJson, ct);
    }
}
