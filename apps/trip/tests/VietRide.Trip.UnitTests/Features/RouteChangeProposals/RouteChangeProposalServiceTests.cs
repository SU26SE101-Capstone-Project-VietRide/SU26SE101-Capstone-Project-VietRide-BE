using FluentAssertions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Events;
using VietRide.Trip.Application.Features.RouteChangeProposals;
using VietRide.Trip.Application.Services;
using VietRide.Trip.Domain.Constants;
using VietRide.Trip.Domain.Entities;
using DomainTrip = VietRide.Trip.Domain.Entities.Trip;

namespace VietRide.Trip.UnitTests.Features.RouteChangeProposals;

public sealed class RouteChangeProposalServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateExisting_CopiesNormalizedStopSnapshotAndEmitsCreatedEvent()
    {
        var fixture = CreateFixture();
        fixture.AlternativeRouteStops.Add(AlternativeRouteStop.Create(fixture.SourceRoute.Id, Guid.NewGuid(), 2, 20, 8m));
        fixture.AlternativeRouteStops.Add(AlternativeRouteStop.Create(fixture.SourceRoute.Id, Guid.NewGuid(), 1, 10, 3m));

        var result = await fixture.Service.CreateAsync(
            fixture.Trip.Id,
            fixture.Trip.DriverUserId,
            "EXISTING",
            fixture.SourceRoute.Id,
            null,
            null,
            "  flooding ahead  ",
            CancellationToken.None);

        result.Reason.Should().Be("flooding ahead");
        result.Snapshot.Stops.Select(stop => stop.OrderIndex).Should().Equal(1, 2);
        fixture.Proposals.Items.Should().ContainSingle();
        fixture.Outbox.Events.Should().ContainSingle(item => item.EventType == RouteChangeProposalIntegrationEvent.Created);
        fixture.UnitOfWork.SaveCalls.Should().Be(1);
    }

    [Fact]
    public async Task CreateCustom_WithValidTenantSnapshotAndGeometry_FreezesProposal()
    {
        var fixture = CreateFixture();
        var snapshot = CreateCustomSnapshot(fixture);

        var result = await fixture.Service.CreateAsync(
            fixture.Trip.Id,
            fixture.Trip.DriverUserId,
            "CUSTOM",
            null,
            snapshot,
            null,
            "Use the safe bypass",
            CancellationToken.None);

        result.Type.Should().Be("CUSTOM");
        result.Snapshot.PathPolyline.Should().Be(snapshot.PathPolyline);
        result.Snapshot.Stops.Should().ContainSingle().Which.StopId.Should().Be(fixture.Stop.Id);
    }

    [Fact]
    public async Task CreateCustom_WithMalformedGeometry_ReturnsCanonicalGeometryValidation()
    {
        var fixture = CreateFixture();
        var snapshot = CreateCustomSnapshot(fixture) with { PathPolyline = "malformed" };

        var action = () => fixture.Service.CreateAsync(
            fixture.Trip.Id,
            fixture.Trip.DriverUserId,
            "CUSTOM",
            null,
            snapshot,
            null,
            "Use the safe bypass",
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<CodedValidationException>();
        exception.Which.ErrorCode.Should().Be("ROUTE_GEOMETRY_INVALID");
        fixture.Proposals.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateCustom_WithoutActiveTenantStation_MasksStationAsNotFound()
    {
        var fixture = CreateFixture();
        fixture.OperatorStation.Deactivate();

        var action = () => fixture.Service.CreateAsync(
            fixture.Trip.Id,
            fixture.Trip.DriverUserId,
            "CUSTOM",
            null,
            CreateCustomSnapshot(fixture),
            null,
            "Use the safe bypass",
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<CodedNotFoundException>();
        exception.Which.ErrorCode.Should().Be("STATION_NOT_FOUND");
    }

    [Fact]
    public async Task ApproveExisting_ChangesTripAndSupersedesOtherPendingProposal()
    {
        var fixture = CreateFixture();
        var approved = CreateExistingProposal(fixture, "approve this");
        var other = CreateCustomProposal(fixture, "other proposal");
        fixture.Proposals.Items.AddRange([approved, other]);

        var result = await fixture.Service.ApproveAsync(
            fixture.Trip.OperatorId,
            Guid.NewGuid(),
            approved.Id,
            CancellationToken.None);

        result.Proposal.Status.Should().Be("APPROVED");
        result.Proposal.ApprovedAlternativeRouteId.Should().Be(fixture.SourceRoute.Id);
        result.RouteChange.AlternativeRouteId.Should().Be(fixture.SourceRoute.Id);
        fixture.Trip.AlternativeRouteId.Should().Be(fixture.SourceRoute.Id);
        other.Status.Should().Be(RouteChangeProposalStatus.SUPERSEDED);
        other.ResolutionCode.Should().Be(RouteChangeProposalResolutionCode.AnotherProposalApproved);
        other.SupersededByProposalId.Should().Be(approved.Id);
        fixture.Outbox.Events.Select(item => item.EventType).Should().Contain([
            RouteChangeProposalIntegrationEvent.Approved,
            RouteChangeProposalIntegrationEvent.Superseded,
            TripRouteChangedIntegrationEvent.EventTypeValue,
        ]);
    }

    [Fact]
    public async Task ApproveCustom_RevalidatesSnapshotAndPromotesOfficialAlternativeRoute()
    {
        var fixture = CreateFixture();
        var proposal = CreateCustomProposal(fixture, "custom bypass");
        proposal.AddStop(RouteChangeProposalStop.Create(proposal.Id, fixture.Stop.Id, 1, 10, 3m));
        fixture.Proposals.Items.Add(proposal);

        var result = await fixture.Service.ApproveAsync(
            fixture.Trip.OperatorId,
            Guid.NewGuid(),
            proposal.Id,
            CancellationToken.None);

        result.Proposal.Status.Should().Be("APPROVED");
        result.Proposal.ApprovedAlternativeRouteId.Should().NotBeNull();
        result.Proposal.ApprovedAlternativeRouteId.Should().NotBe(fixture.SourceRoute.Id);
        fixture.AlternativeRoutes.AddedRoutes.Should().ContainSingle(route => route.Id == result.Proposal.ApprovedAlternativeRouteId);
        fixture.AlternativeRouteStops.Should().ContainSingle(item => item.StopId == fixture.Stop.Id);
        fixture.LockCalls.Should().StartWith("trip", "proposals", "station", "operator-station", "stops");
    }

    [Fact]
    public async Task ApproveCustom_WhenLockedStopIsInactive_CommitsExpiryThenReturnsStaleConflict()
    {
        var fixture = CreateFixture();
        var proposal = CreateCustomProposal(fixture, "custom bypass");
        proposal.AddStop(RouteChangeProposalStop.Create(proposal.Id, fixture.Stop.Id, 1, 10, 3m));
        fixture.Proposals.Items.Add(proposal);
        fixture.Stop.Deactivate();

        var action = () => fixture.Service.ApproveAsync(
            fixture.Trip.OperatorId,
            Guid.NewGuid(),
            proposal.Id,
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<CodedConflictException>();
        exception.Which.ErrorCode.Should().Be("ROUTE_CHANGE_PROPOSAL_STALE");
        proposal.Status.Should().Be(RouteChangeProposalStatus.EXPIRED);
        proposal.ResolutionCode.Should().Be(RouteChangeProposalResolutionCode.SourceRouteChanged);
    }

    [Fact]
    public async Task ApproveStaleSource_CommitsExpiryThenReturnsCanonicalConflict()
    {
        var fixture = CreateFixture();
        var proposal = CreateExistingProposal(fixture, "stale proposal");
        fixture.Proposals.Items.Add(proposal);
        fixture.SourceRoute.UpdatedAt = fixture.SourceRoute.UpdatedAt.AddMinutes(1);

        var action = () => fixture.Service.ApproveAsync(
            fixture.Trip.OperatorId,
            Guid.NewGuid(),
            proposal.Id,
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<CodedConflictException>();
        exception.Which.ErrorCode.Should().Be("ROUTE_CHANGE_PROPOSAL_STALE");
        proposal.Status.Should().Be(RouteChangeProposalStatus.EXPIRED);
        proposal.ResolutionCode.Should().Be(RouteChangeProposalResolutionCode.SourceRouteChanged);
        fixture.UnitOfWork.SaveCalls.Should().Be(1);
        fixture.Outbox.Events.Should().ContainSingle(item => item.EventType == RouteChangeProposalIntegrationEvent.Expired);
    }

    [Fact]
    public async Task Reject_PersistsReasonAndEmitsRejectedEvent()
    {
        var fixture = CreateFixture();
        var proposal = CreateCustomProposal(fixture, "unsafe route");
        fixture.Proposals.Items.Add(proposal);

        var result = await fixture.Service.RejectAsync(
            fixture.Trip.OperatorId,
            Guid.NewGuid(),
            proposal.Id,
            "  insufficient clearance  ",
            CancellationToken.None);

        result.Status.Should().Be("REJECTED");
        result.RejectionReason.Should().Be("insufficient clearance");
        fixture.Outbox.Events.Should().ContainSingle(item => item.EventType == RouteChangeProposalIntegrationEvent.Rejected);
    }

    [Fact]
    public async Task Approve_WithDifferentOperator_DoesNotRevealProposal()
    {
        var fixture = CreateFixture();
        fixture.Proposals.Items.Add(CreateExistingProposal(fixture, "tenant-owned"));

        var action = () => fixture.Service.ApproveAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            fixture.Proposals.Items[0].Id,
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<CodedNotFoundException>();
        exception.Which.ErrorCode.Should().Be("ROUTE_CHANGE_PROPOSAL_NOT_FOUND");
    }

    private static Fixture CreateFixture()
    {
        var operatorId = Guid.NewGuid();
        var originStation = Station.Create("Origin", "origin", "HCMC", "HCMC", latitude: 38.5m, longitude: -120.2m);
        var destinationStation = Station.Create("Destination", "destination", "HCMC", "HCMC", latitude: 43.252m, longitude: -126.453m);
        var route = Route.Create(
            operatorId,
            "Main route",
            originStation.Id,
            destinationStation.Id,
            Money.FromRaw(100_000),
            15m,
            30);
        var trip = DomainTrip.Create(
            operatorId,
            route.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            Now.AddHours(2),
            Now.AddHours(5),
            TripSource.MANUAL,
            Money.FromRaw(100_000),
            null,
            0m);
        var sourceRoute = AlternativeRoute.Create(route.Id, "Flood bypass", destinationStation.Id, 15m, 30);
        sourceRoute.UpdatedAt = Now;
        var stop = Stop.Create(operatorId, "Checkpoint", 40.7m, -120.95m);
        var operatorStation = OperatorStation.Create(operatorId, destinationStation.Id);
        var lockCalls = new List<string>();
        var proposals = new ProposalRepositoryStub(lockCalls);
        var trips = new TripRepositoryStub(trip, lockCalls);
        var alternativeRoutes = new AlternativeRouteRepositoryStub(sourceRoute);
        var auditLogs = new AuditLogRepositoryStub();
        var outbox = new OutboxStub();
        var unitOfWork = new UnitOfWorkStub();
        var service = new RouteChangeProposalService(
            proposals,
            trips,
            alternativeRoutes,
            new RouteRepositoryStub(route),
            new StationRepositoryStub([originStation, destinationStation], lockCalls),
            new OperatorStationRepositoryStub(operatorStation, lockCalls),
            new StopRepositoryStub(stop, lockCalls),
            new IncidentRepositoryStub(),
            auditLogs,
            new BookingImpactClientStub(trip.Id),
            new TripRouteChangeService(alternativeRoutes, outbox),
            outbox,
            unitOfWork,
            new ClockStub(Now));
        return new Fixture(service, trip, sourceRoute, destinationStation, operatorStation, stop, alternativeRoutes, alternativeRoutes.Stops, proposals, outbox, unitOfWork, lockCalls);
    }

    private static RouteChangeProposal CreateExistingProposal(Fixture fixture, string reason)
        => RouteChangeProposal.Create(
            fixture.Trip.Id,
            fixture.Trip.OperatorId,
            fixture.Trip.DriverUserId,
            RouteChangeProposalType.EXISTING,
            fixture.SourceRoute.Id,
            fixture.SourceRoute.UpdatedAt,
            null,
            reason,
            fixture.SourceRoute.Name,
            fixture.SourceRoute.Description,
            fixture.SourceRoute.DestinationStationId,
            fixture.SourceRoute.TotalDistanceKm,
            fixture.SourceRoute.EstimatedDurationMinutes,
            fixture.SourceRoute.PathPolyline);

    private static RouteChangeProposal CreateCustomProposal(Fixture fixture, string reason)
        => RouteChangeProposal.Create(
            fixture.Trip.Id,
            fixture.Trip.OperatorId,
            fixture.Trip.DriverUserId,
            RouteChangeProposalType.CUSTOM,
            null,
            null,
            null,
            reason,
            "Custom bypass",
            null,
            fixture.DestinationStation.Id,
            10m,
            20,
            "_p~iF~ps|U_ulLnnqC_mqNvxq`@");

    private static RouteChangeProposalSnapshotInput CreateCustomSnapshot(Fixture fixture)
        => new(
            "Custom bypass",
            "Safe detour",
            fixture.DestinationStation.Id,
            10m,
            20,
            "_p~iF~ps|U_ulLnnqC_mqNvxq`@",
            [new RouteChangeProposalStopSnapshot(fixture.Stop.Id, 1, 10, 3m)]);

    private sealed record Fixture(
        RouteChangeProposalService Service,
        DomainTrip Trip,
        AlternativeRoute SourceRoute,
        Station DestinationStation,
        OperatorStation OperatorStation,
        Stop Stop,
        AlternativeRouteRepositoryStub AlternativeRoutes,
        List<AlternativeRouteStop> AlternativeRouteStops,
        ProposalRepositoryStub Proposals,
        OutboxStub Outbox,
        UnitOfWorkStub UnitOfWork,
        List<string> LockCalls);

    private abstract class RepositoryStub<TEntity>
        where TEntity : class
    {
        protected readonly List<TEntity> Entities = [];
        public virtual Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<TEntity?>(null);
        public virtual Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken) { Entities.Add(entity); return Task.FromResult(entity); }
        public void Update(TEntity entity) { }
        public void Remove(TEntity entity) => Entities.Remove(entity);
        public IQueryable<TEntity> Query() => Entities.AsQueryable();
        public IQueryable<TEntity> QueryNoTracking() => Entities.AsQueryable();
    }

    private sealed class ProposalRepositoryStub(List<string> lockCalls) : RepositoryStub<RouteChangeProposal>, IRouteChangeProposalRepository
    {
        public List<RouteChangeProposal> Items => Entities;
        public Task AcquireSourceCoordinationLockAsync(Guid sourceAlternativeRouteId, CancellationToken cancellationToken)
        {
            lockCalls.Add("source-coordination");
            return Task.CompletedTask;
        }
        public Task<RouteChangeProposal?> GetOwnedByIdAsync(Guid operatorId, Guid proposalId, CancellationToken cancellationToken)
            => Task.FromResult(Entities.SingleOrDefault(item => item.Id == proposalId && item.OperatorId == operatorId));
        public IQueryable<RouteChangeProposal> QueryWithStopsNoTracking() => Entities.AsQueryable();
        public Task<IReadOnlyList<RouteChangeProposal>> AcquirePendingByTripAsync(Guid tripId, CancellationToken cancellationToken)
        {
            lockCalls.Add("proposals");
            return Task.FromResult<IReadOnlyList<RouteChangeProposal>>(Entities.Where(item => item.TripId == tripId && item.Status == RouteChangeProposalStatus.PENDING).OrderBy(item => item.Id).ToArray());
        }
        public Task<IReadOnlyList<RouteChangeProposal>> AcquirePendingBySourceAsync(Guid sourceAlternativeRouteId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<RouteChangeProposal>>(Entities.Where(item => item.SourceAlternativeRouteId == sourceAlternativeRouteId && item.Status == RouteChangeProposalStatus.PENDING).ToArray());
        public Task LoadStopsAsync(RouteChangeProposal proposal, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TripRepositoryStub(DomainTrip trip, List<string> lockCalls) : RepositoryStub<DomainTrip>, ITripRepository
    {
        public override Task<DomainTrip?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(id == trip.Id ? trip : null);
        public Task<DomainTrip?> GetWithSeatsAsync(Guid tripId, CancellationToken cancellationToken) => GetByIdAsync(tripId, cancellationToken);
        public Task<DomainTrip?> GetRouteChangePreflightAsync(Guid tripId, CancellationToken cancellationToken) => GetByIdAsync(tripId, cancellationToken);
        public Task<DomainTrip?> AcquireForRouteChangeAsync(Guid tripId, CancellationToken cancellationToken)
        {
            lockCalls.Add("trip");
            return GetByIdAsync(tripId, cancellationToken);
        }
    }

    private sealed class AlternativeRouteRepositoryStub(AlternativeRoute route) : RepositoryStub<AlternativeRoute>, IAlternativeRouteRepository
    {
        public List<AlternativeRouteStop> Stops { get; } = [];
        public IReadOnlyList<AlternativeRoute> AddedRoutes => Entities;
        public Task<AlternativeRoute?> GetOwnedByIdAsync(Guid operatorId, Guid alternativeRouteId, CancellationToken cancellationToken)
            => Task.FromResult(alternativeRouteId == route.Id ? route : null);
        public Task<AlternativeRoute?> AcquireOwnedByIdAsync(Guid operatorId, Guid alternativeRouteId, CancellationToken cancellationToken)
            => GetOwnedByIdAsync(operatorId, alternativeRouteId, cancellationToken);
        public Task<bool> ExistsStopAsync(Guid alternativeRouteId, Guid stopId, CancellationToken cancellationToken) => Task.FromResult(Stops.Any(item => item.StopId == stopId));
        public Task<bool> ExistsStopOrderIndexAsync(Guid alternativeRouteId, int orderIndex, CancellationToken cancellationToken) => Task.FromResult(Stops.Any(item => item.OrderIndex == orderIndex));
        public Task<IReadOnlyList<AlternativeRouteStop>> ListStopsAsync(Guid alternativeRouteId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AlternativeRouteStop>>(Stops.ToArray());
        public Task<IReadOnlyList<TripRouteChangedCandidateStop>> ListCandidateStopsAsync(Guid alternativeRouteId, DateTimeOffset estimatedArrivalBase, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TripRouteChangedCandidateStop>>([]);
        public Task ReplaceStopsAsync(Guid alternativeRouteId, IReadOnlyCollection<AlternativeRouteStop> stops, CancellationToken cancellationToken) { Stops.Clear(); Stops.AddRange(stops); return Task.CompletedTask; }
    }

    private sealed class RouteRepositoryStub(Route route) : RepositoryStub<Route>, IRouteRepository
    {
        public override Task<Route?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(id == route.Id ? route : null);
        public Task<Route?> GetOwnedByIdAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken)
            => Task.FromResult(routeId == route.Id && operatorId == route.OperatorId ? route : null);
        public Task<Route?> GetOwnedActiveByIdAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken)
            => Task.FromResult(routeId == route.Id && operatorId == route.OperatorId && route.IsActive ? route : null);
        public Task<IReadOnlyList<Route>> ListByOperatorAsync(Guid operatorId, string? search, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Route>>(operatorId == route.OperatorId ? [route] : []);
        public Task<bool> ExistsActiveOwnedByOperatorAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken)
            => Task.FromResult(routeId == route.Id && operatorId == route.OperatorId && route.IsActive);
    }

    private sealed class StationRepositoryStub(IReadOnlyCollection<Station> stations, List<string> lockCalls) : RepositoryStub<Station>, IStationRepository
    {
        public override Task<Station?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(stations.SingleOrDefault(station => station.Id == id));
        public Task<Station?> AcquireForRouteProposalApprovalAsync(Guid id, CancellationToken cancellationToken)
        {
            lockCalls.Add("station");
            return GetByIdAsync(id, cancellationToken);
        }
        public Task<IReadOnlyList<Station>> SearchActiveByNameAsync(string? q, string? city, string? province, Guid? locationId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Station>>([]);
    }

    private sealed class OperatorStationRepositoryStub : RepositoryStub<OperatorStation>, IOperatorStationRepository
    {
        private readonly OperatorStation operatorStation;
        private readonly List<string> lockCalls;

        public OperatorStationRepositoryStub(OperatorStation operatorStation, List<string> lockCalls)
        {
            this.operatorStation = operatorStation;
            this.lockCalls = lockCalls;
            Entities.Add(operatorStation);
        }

        public Task<OperatorStation?> AcquireActiveForRouteProposalApprovalAsync(Guid operatorId, Guid stationId, CancellationToken cancellationToken)
        {
            lockCalls.Add("operator-station");
            return Task.FromResult(operatorStation.OperatorId == operatorId
                && operatorStation.StationId == stationId
                && operatorStation.IsActive
                    ? operatorStation
                    : null);
        }

        public Task<bool> ExistsActiveAsync(Guid operatorId, Guid stationId, CancellationToken cancellationToken)
            => Task.FromResult(operatorStation.OperatorId == operatorId
                && operatorStation.StationId == stationId
                && operatorStation.IsActive);
    }

    private sealed class StopRepositoryStub(Stop stop, List<string> lockCalls) : RepositoryStub<Stop>, IStopRepository
    {
        public override Task<Stop?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(id == stop.Id ? stop : null);
        public Task<IReadOnlyList<Stop>> AcquireForRouteProposalApprovalAsync(IReadOnlyCollection<Guid> stopIds, CancellationToken cancellationToken)
        {
            lockCalls.Add("stops");
            return Task.FromResult<IReadOnlyList<Stop>>(stopIds.Contains(stop.Id) ? [stop] : []);
        }
    }
    private sealed class IncidentRepositoryStub : RepositoryStub<Incident>, IIncidentRepository;

    private sealed class AuditLogRepositoryStub : ITripAuditLogRepository
    {
        public List<TripAuditLog> Items { get; } = [];
        public Task AddAsync(TripAuditLog auditLog, CancellationToken cancellationToken = default) { Items.Add(auditLog); return Task.CompletedTask; }
        public Task<IReadOnlyList<TripAuditLog>> ListByTripIdAsync(Guid tripId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TripAuditLog>>(Items.Where(item => item.TripId == tripId).ToArray());
    }

    private sealed class BookingImpactClientStub(Guid tripId) : IBookingImpactClient
    {
        public Task<TripBookingImpactProjection> GetTripEditImpactAsync(Guid requestedTripId, Guid operatorId, CancellationToken cancellationToken)
            => Task.FromResult(new TripBookingImpactProjection(tripId, 0, []));
    }

    private sealed class OutboxStub : IIntegrationEventOutbox
    {
        public List<(Guid EventId, string EventType, string Payload)> Events { get; } = [];
        public Task EnqueueAsync(Guid eventId, string eventType, string payloadJson, CancellationToken cancellationToken = default) { Events.Add((eventId, eventType, payloadJson)); return Task.CompletedTask; }
        public Task EnqueueAsync(string eventType, string payloadJson, CancellationToken cancellationToken = default) { Events.Add((Guid.Empty, eventType, payloadJson)); return Task.CompletedTask; }
    }

    private sealed class UnitOfWorkStub : IUnitOfWork
    {
        public int SaveCalls { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) { SaveCalls++; return Task.FromResult(1); }
        public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken) { var result = await operation(); await SaveChangesAsync(cancellationToken); return result; }
        public Task BeginTransactionAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ClockStub(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }
}
