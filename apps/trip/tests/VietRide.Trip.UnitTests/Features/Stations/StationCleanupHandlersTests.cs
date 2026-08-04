using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Internal.Stations;
using VietRide.Trip.Application.Features.Stations;
using VietRide.Trip.Application.Features.Stations.MergeStations;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Features.Stations;

public sealed class StationCleanupHandlersTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-16T08:00:00Z");

    [Fact]
    public async Task Normalize_PreservesSlugPolicyAndEnqueuesPiiSafeSnapshot()
    {
        var station = Station.Create(
            "Old Name",
            "old-name-city-province",
            "City",
            "Province",
            contactPhone: "0900000000",
            contactEmail: "secret@example.com");
        var stations = new FakeStationRepository([station]);
        var outbox = new CapturingOutbox();
        var handler = new UpdateAdminStationHandler(stations, outbox, new FakeUnitOfWork(), new FrozenClock(Now));
        var actorId = Guid.NewGuid();

        var result = await handler.Handle(new UpdateAdminStationCommand(
            station.Id,
            "Bến Xe Mới",
            null,
            null,
            "Thủ Đức",
            "Hồ Chí Minh",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            true,
            actorId,
            "127.0.0.1",
            "unit-test"), CancellationToken.None);

        result.Slug.Should().Be("ben-xe-moi-thu-đuc-ho-chi-minh");
        outbox.Events.Should().ContainSingle();
        outbox.Events[0].EventType.Should().Be("trip.station.normalized");
        outbox.Events[0].Payload.Should().NotContain("contactPhone");
        outbox.Events[0].Payload.Should().NotContain("contactEmail");
        using var payload = JsonDocument.Parse(outbox.Events[0].Payload);
        payload.RootElement.GetProperty("actorUserId").GetGuid().Should().Be(actorId);
        payload.RootElement.GetProperty("before").GetProperty("name").GetString().Should().Be("Old Name");
        payload.RootElement.GetProperty("after").GetProperty("name").GetString().Should().Be("Bến Xe Mới");
        payload.RootElement.GetProperty("before").EnumerateObject().Should().HaveCount(9);
        payload.RootElement.GetProperty("before").TryGetProperty("addressStreet", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Normalize_SlugCollisionUsesDeterministicStationHashSuffix()
    {
        var station = Station.Create("Old", "old", "City", "Province");
        var collision = Station.Create("Collision", "collision-city-province", "Other", "Other");
        var handler = new UpdateAdminStationHandler(
            new FakeStationRepository([station, collision]),
            new CapturingOutbox(),
            new FakeUnitOfWork(),
            new FrozenClock(Now));

        var result = await handler.Handle(new UpdateAdminStationCommand(
            station.Id,
            "Collision",
            null,
            null,
            "City",
            "Province",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            Guid.NewGuid(),
            null,
            null), CancellationToken.None);

        var suffix = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(station.Id.ToString("D"))))[..6]
            .ToLowerInvariant();
        result.Slug.Should().Be($"collision-city-province-{suffix}");
    }

    [Fact]
    public async Task NormalizeValidator_RejectsEmptyPatchAndUnpairedCoordinates()
    {
        var validator = new UpdateAdminStationCommandValidator();
        var empty = await validator.ValidateAsync(new UpdateAdminStationCommand(
            Guid.NewGuid(),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            Guid.NewGuid(),
            null,
            null));
        var unpaired = await validator.ValidateAsync(new UpdateAdminStationCommand(
            Guid.NewGuid(),
            null,
            null,
            null,
            null,
            null,
            10.7m,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            Guid.NewGuid(),
            null,
            null));

        empty.IsValid.Should().BeFalse();
        empty.Errors.Should().Contain(error => error.PropertyName == "request");
        unpaired.IsValid.Should().BeFalse();
        unpaired.Errors.Should().Contain(error => error.PropertyName == "coordinates");
    }

    [Fact]
    public async Task MergeValidator_RejectsSelfMerge()
    {
        var stationId = Guid.NewGuid();
        var result = await new MergeStationsCommandValidator().ValidateAsync(new MergeStationsCommand(
            stationId,
            stationId,
            Guid.NewGuid(),
            null,
            null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(MergeStationsCommand.DuplicateStationId));
    }

    [Fact]
    public async Task Normalize_RejectsMergedStationWithoutOutbox()
    {
        var station = Station.Create("Duplicate", "duplicate", "City", "Province");
        station.MarkMergedInto(Guid.NewGuid(), Now);
        var outbox = new CapturingOutbox();
        var handler = new UpdateAdminStationHandler(
            new FakeStationRepository([station]),
            outbox,
            new FakeUnitOfWork(),
            new FrozenClock(Now));

        var action = () => handler.Handle(new UpdateAdminStationCommand(
            station.Id,
            "Changed",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            Guid.NewGuid(),
            null,
            null), CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedConflictException>()).Which;
        exception.ErrorCode.Should().Be("STATION_MERGE_CONFLICT");
        outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Merge_AppliesProfilePolicyReturnsCountsAndEnqueuesPiiSafeEvent()
    {
        var primary = Station.Create(
            "Primary",
            "primary",
            "Primary City",
            "Primary Province",
            contactPhone: "0900000000");
        var duplicate = Station.Create(
            "Duplicate",
            "duplicate",
            "Duplicate City",
            "Duplicate Province",
            addressStreet: "Duplicate Address",
            latitude: 10.7m,
            longitude: 106.7m,
            contactEmail: "duplicate@example.com",
            supportsShuttle: true);
        var stations = new FakeStationRepository([primary, duplicate])
        {
            ShuttleCount = 4,
            RedirectCount = 5,
        };
        var operatorStations = new FakeOperatorStationRepository((2, 1));
        var routes = new FakeRouteRepository(false, (3, 2));
        var alternativeRoutes = new FakeAlternativeRouteRepository(6);
        var outbox = new CapturingOutbox();
        var handler = new MergeStationsCommandHandler(
            stations,
            operatorStations,
            routes,
            alternativeRoutes,
            outbox,
            new FakeUnitOfWork(),
            new FrozenClock(Now));

        var result = await handler.Handle(new MergeStationsCommand(
            primary.Id,
            duplicate.Id,
            Guid.NewGuid(),
            "127.0.0.1",
            "unit-test"), CancellationToken.None);

        result.PrimaryStation.Name.Should().Be("Primary");
        result.PrimaryStation.AddressStreet.Should().Be("Duplicate Address");
        result.PrimaryStation.ContactPhone.Should().Be("0900000000");
        result.PrimaryStation.ContactEmail.Should().Be("duplicate@example.com");
        result.PrimaryStation.SupportsShuttle.Should().BeTrue();
        result.RelinkedCounts.Should().Be(new StationRelinkedCounts(2, 1, 3, 2, 6, 4, 5));
        duplicate.MergedIntoStationId.Should().Be(primary.Id);
        duplicate.DeletedAt.Should().Be(Now);
        outbox.Events.Should().ContainSingle();
        outbox.Events[0].EventType.Should().Be("trip.station.merged");
        outbox.Events[0].Payload.Should().NotContain("contactPhone");
        outbox.Events[0].Payload.Should().NotContain("contactEmail");
    }

    [Fact]
    public async Task Merge_RouteConflictLeavesAggregateAndOutboxUntouched()
    {
        var primary = Station.Create("Primary", "primary", "City", "Province");
        var duplicate = Station.Create(
            "Duplicate",
            "duplicate",
            "City",
            "Province",
            addressStreet: "Duplicate Address");
        var stations = new FakeStationRepository([primary, duplicate]);
        var routes = new FakeRouteRepository(true, default);
        var outbox = new CapturingOutbox();
        var handler = new MergeStationsCommandHandler(
            stations,
            new FakeOperatorStationRepository(default),
            routes,
            new FakeAlternativeRouteRepository(0),
            outbox,
            new FakeUnitOfWork(),
            new FrozenClock(Now));

        var action = () => handler.Handle(new MergeStationsCommand(
            primary.Id,
            duplicate.Id,
            Guid.NewGuid(),
            null,
            null), CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedConflictException>()).Which;
        exception.ErrorCode.Should().Be("STATION_MERGE_CONFLICT");
        primary.AddressStreet.Should().BeNull();
        duplicate.DeletedAt.Should().BeNull();
        outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task InternalResolution_ReturnsMergedOriginalAndCanonicalTarget()
    {
        var canonicalId = Guid.NewGuid();
        var duplicate = Station.Create("Duplicate", "duplicate", "City", "Province", supportsShuttle: true);
        duplicate.MarkMergedInto(canonicalId, Now);
        var handler = new GetStationByIdHandler(new FakeStationRepository([duplicate]));

        var result = await handler.Handle(new GetStationByIdQuery(duplicate.Id), CancellationToken.None);

        result.Id.Should().Be(duplicate.Id);
        result.Name.Should().Be("Duplicate");
        result.IsMerged.Should().BeTrue();
        result.CanonicalStationId.Should().Be(canonicalId);
        result.SupportsShuttle.Should().BeTrue();
    }

    [Fact]
    public async Task InternalResolution_RejectsOrdinarySoftDelete()
    {
        var station = Station.Create("Deleted", "deleted", "City", "Province");
        station.SoftDelete(Now);
        var handler = new GetStationByIdHandler(new FakeStationRepository([station]));

        var action = () => handler.Handle(new GetStationByIdQuery(station.Id), CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<CodedNotFoundException>()).Which;
        exception.ErrorCode.Should().Be("STATION_NOT_FOUND");
    }

    private sealed class FakeStationRepository : IStationRepository
    {
        private readonly List<Station> _stations;

        public FakeStationRepository(IEnumerable<Station> stations) => _stations = stations.ToList();

        public int ShuttleCount { get; init; }
        public int RedirectCount { get; init; }

        public Task<Station?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(_stations.SingleOrDefault(station => station.Id == id && !station.DeletedAt.HasValue));

        public Task<Station?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_stations.SingleOrDefault(station => station.Id == id));

        public Task<Station?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
            => GetByIdIncludingDeletedAsync(id, cancellationToken);

        public Task<bool> SlugExistsAsync(
            string slug,
            Guid excludedStationId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_stations.Any(station =>
                station.Id != excludedStationId
                && station.Slug == slug
                && !station.DeletedAt.HasValue));

        public Task<IReadOnlyList<Station>> GetForMergeAsync(
            Guid primaryStationId,
            Guid duplicateStationId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Station>>(_stations
                .Where(station => station.Id == primaryStationId || station.Id == duplicateStationId)
                .ToArray());

        public Task<int> FlattenMergeRedirectsAsync(
            Guid duplicateStationId,
            Guid primaryStationId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(RedirectCount);

        public Task<int> RelinkShuttleTripsAsync(
            Guid duplicateStationId,
            Guid primaryStationId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ShuttleCount);

        public Task<IReadOnlyList<Station>> SearchActiveByNameAsync(
            string? q,
            string? city,
            string? province,
            Guid? locationId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Station>>([]);

        public Task<Station> AddAsync(Station entity, CancellationToken ct)
        {
            _stations.Add(entity);
            return Task.FromResult(entity);
        }

        public void Update(Station entity) { }
        public void Remove(Station entity) => _stations.Remove(entity);
        public IQueryable<Station> Query() => _stations.AsQueryable();
        public IQueryable<Station> QueryNoTracking() => _stations.AsQueryable();
    }

    private sealed class CapturingOutbox : IIntegrationEventOutbox
    {
        public List<(string EventType, string Payload)> Events { get; } = [];

        public Task EnqueueAsync(string eventType, string payloadJson, CancellationToken ct = default)
        {
            Events.Add((eventType, payloadJson));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOperatorStationRepository : IOperatorStationRepository
    {
        private readonly (int RelinkedCount, int CollapsedCount) _counts;

        public FakeOperatorStationRepository((int RelinkedCount, int CollapsedCount) counts) => _counts = counts;

        public Task<(int RelinkedCount, int CollapsedCount)> RelinkForStationMergeAsync(
            Guid duplicateStationId,
            Guid primaryStationId,
            CancellationToken cancellationToken = default) => Task.FromResult(_counts);

        public Task<OperatorStation?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<OperatorStation?>(null);
        public Task<OperatorStation> AddAsync(OperatorStation entity, CancellationToken ct) => Task.FromResult(entity);
        public void Update(OperatorStation entity) { }
        public void Remove(OperatorStation entity) { }
        public IQueryable<OperatorStation> Query() => Array.Empty<OperatorStation>().AsQueryable();
        public IQueryable<OperatorStation> QueryNoTracking() => Query();
    }

    private sealed class FakeRouteRepository : IRouteRepository
    {
        private readonly bool _hasConflict;
        private readonly (int OriginCount, int DestinationCount) _counts;

        public FakeRouteRepository(bool hasConflict, (int OriginCount, int DestinationCount) counts)
        {
            _hasConflict = hasConflict;
            _counts = counts;
        }

        public Task<bool> HasStationMergeConflictAsync(
            Guid duplicateStationId,
            Guid primaryStationId,
            CancellationToken cancellationToken = default) => Task.FromResult(_hasConflict);

        public Task<(int OriginCount, int DestinationCount)> RelinkForStationMergeAsync(
            Guid duplicateStationId,
            Guid primaryStationId,
            CancellationToken cancellationToken = default) => Task.FromResult(_counts);

        public Task<Route?> GetOwnedByIdAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken)
            => Task.FromResult<Route?>(null);
        public Task<Route?> GetOwnedActiveByIdAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken)
            => Task.FromResult<Route?>(null);
        public Task<IReadOnlyList<Route>> ListByOperatorAsync(Guid operatorId, string? search, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Route>>([]);
        public Task<bool> ExistsActiveOwnedByOperatorAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken)
            => Task.FromResult(false);
        public Task<Route?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<Route?>(null);
        public Task<Route> AddAsync(Route entity, CancellationToken ct) => Task.FromResult(entity);
        public void Update(Route entity) { }
        public void Remove(Route entity) { }
        public IQueryable<Route> Query() => Array.Empty<Route>().AsQueryable();
        public IQueryable<Route> QueryNoTracking() => Query();
    }

    private sealed class FakeAlternativeRouteRepository : IAlternativeRouteRepository
    {
        private readonly int _count;

        public FakeAlternativeRouteRepository(int count) => _count = count;

        public Task<int> RelinkDestinationForStationMergeAsync(
            Guid duplicateStationId,
            Guid primaryStationId,
            CancellationToken cancellationToken = default) => Task.FromResult(_count);

        public Task<AlternativeRoute?> GetOwnedByIdAsync(
            Guid operatorId,
            Guid alternativeRouteId,
            CancellationToken cancellationToken) => Task.FromResult<AlternativeRoute?>(null);
        public Task<bool> ExistsStopAsync(Guid alternativeRouteId, Guid stopId, CancellationToken cancellationToken)
            => Task.FromResult(false);
        public Task<bool> ExistsStopOrderIndexAsync(Guid alternativeRouteId, int orderIndex, CancellationToken cancellationToken)
            => Task.FromResult(false);
        public Task<IReadOnlyList<AlternativeRouteStop>> ListStopsAsync(
            Guid alternativeRouteId,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AlternativeRouteStop>>([]);
        public Task ReplaceStopsAsync(
            Guid alternativeRouteId,
            IReadOnlyCollection<AlternativeRouteStop> stops,
            CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<AlternativeRoute?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult<AlternativeRoute?>(null);
        public Task<AlternativeRoute> AddAsync(AlternativeRoute entity, CancellationToken ct) => Task.FromResult(entity);
        public void Update(AlternativeRoute entity) { }
        public void Remove(AlternativeRoute entity) { }
        public IQueryable<AlternativeRoute> Query() => Array.Empty<AlternativeRoute>().AsQueryable();
        public IQueryable<AlternativeRoute> QueryNoTracking() => Query();
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(1);
        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct) => operation();
        public Task BeginTransactionAsync(CancellationToken ct) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FrozenClock : IClock
    {
        public FrozenClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; }
    }
}
