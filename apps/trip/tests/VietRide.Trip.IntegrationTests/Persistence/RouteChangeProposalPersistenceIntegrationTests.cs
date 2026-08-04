using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Shared.Persistence.UnitOfWork;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Trips;
using VietRide.Trip.Application.Services;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;
using Day29Fixture = VietRide.Trip.IntegrationTests.Internal.Trips.Day29CargoNearFullOutboxIntegrationTests;

namespace VietRide.Trip.IntegrationTests.Persistence;

public sealed class RouteChangeProposalPersistenceIntegrationTests
{
    [Fact]
    public void Model_NormalizesProposalStopsAndKeepsOutboxInSameContext()
    {
        using var db = CreateDbContext();
        var model = db.GetService<IDesignTimeModel>().Model;
        var proposal = model.FindEntityType(typeof(RouteChangeProposal))!;
        var proposalStop = model.FindEntityType(typeof(RouteChangeProposalStop))!;

        proposal.GetTableName().Should().Be("route_change_proposals");
        proposal.FindProperty("StopsJson").Should().BeNull();
        proposal.FindProperty(nameof(RouteChangeProposal.ApprovedAlternativeRouteId)).Should().NotBeNull();
        proposal.FindProperty(nameof(RouteChangeProposal.ResolutionCode)).Should().NotBeNull();
        proposal.FindProperty(nameof(RouteChangeProposal.Status))!.GetDefaultValueSql()
            .Should().Be("'PENDING'::vietride_trip.route_change_proposal_status");
        proposal.GetIndexes().Should().Contain(index =>
            index.Properties.Select(property => property.Name).SequenceEqual(new[]
            {
                nameof(RouteChangeProposal.OperatorId),
                nameof(RouteChangeProposal.Status),
                nameof(RouteChangeProposal.CreatedAt),
            }) && index.IsDescending!.SequenceEqual(new[] { false, false, true }));
        proposal.GetIndexes().Should().Contain(index =>
            index.Properties.Select(property => property.Name).SequenceEqual(new[]
            {
                nameof(RouteChangeProposal.ProposedByUserId),
                nameof(RouteChangeProposal.CreatedAt),
            }) && index.IsDescending!.SequenceEqual(new[] { false, true }));
        proposalStop.GetTableName().Should().Be("route_change_proposal_stops");
        proposalStop.FindPrimaryKey()!.Properties.Select(property => property.Name)
            .Should().Equal(nameof(RouteChangeProposalStop.ProposalId), nameof(RouteChangeProposalStop.StopId));
        proposalStop.GetIndexes().Should().Contain(index => index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(new[]
            {
                nameof(RouteChangeProposalStop.ProposalId),
                nameof(RouteChangeProposalStop.OrderIndex),
            }));
        db.Model.GetEntityTypes().Should().Contain(entity => entity.GetTableName() == "outbox_events");
    }

    [Fact]
    public async Task ApprovalLockRepositories_RequireAmbientTransaction()
    {
        await using var db = CreateDbContext();
        var stationRepository = (IStationRepository)CreateRepository(db, "StationRepository");
        var operatorStationRepository = (IOperatorStationRepository)CreateRepository(db, "OperatorStationRepository");
        var stopRepository = (IStopRepository)CreateRepository(db, "StopRepository");

        var stationAction = () => stationRepository.AcquireForRouteProposalApprovalAsync(Guid.NewGuid(), CancellationToken.None);
        var stopAction = () => stopRepository.AcquireForRouteProposalApprovalAsync([Guid.NewGuid(), Guid.NewGuid()], CancellationToken.None);
        var operatorStationAction = () => operatorStationRepository.AcquireActiveForRouteProposalApprovalAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        await stationAction.Should().ThrowAsync<InvalidOperationException>().WithMessage("A transaction is required.");
        await operatorStationAction.Should().ThrowAsync<InvalidOperationException>().WithMessage("A transaction is required.");
        await stopAction.Should().ThrowAsync<InvalidOperationException>().WithMessage("A transaction is required.");
    }

    [Fact]
    public async Task ApprovalLocks_EmitForUpdateAndDeterministicStopOrdering()
    {
        var interceptor = new CommandCaptureInterceptor();
        await using var db = CreateDbContext($"route_proposal_locks_{Guid.NewGuid():N}", interceptor);
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        try
        {
            await db.Database.MigrateAsync();
            var tripSeed = await global::VietRide.Trip.IntegrationTests.Internal.Trips.Day29CargoNearFullOutboxIntegrationTests.SeedTripAsync(db);
            var operatorId = tripSeed.OperatorId;
            var station = Station.Create("Locked destination", $"locked-{Guid.NewGuid():N}", "HCMC", "HCMC");
            var operatorStation = OperatorStation.Create(operatorId, station.Id);
            var firstStop = Stop.Create(operatorId, "First locked stop", 10m, 106m);
            var secondStop = Stop.Create(operatorId, "Second locked stop", 11m, 107m);
            var proposal = RouteChangeProposal.Create(
                tripSeed.TripId,
                operatorId,
                Guid.NewGuid(),
                RouteChangeProposalType.CUSTOM,
                null,
                null,
                null,
                "Lock race scaffold",
                "Locked bypass",
                null,
                station.Id,
                10m,
                20,
                "_p~iF~ps|U_ulLnnqC_mqNvxq`@");
            proposal.AddStop(RouteChangeProposalStop.Create(proposal.Id, firstStop.Id, 1, 10, 3m));
            proposal.AddStop(RouteChangeProposalStop.Create(proposal.Id, secondStop.Id, 2, 20, 6m));
            db.AddRange(station, operatorStation, firstStop, secondStop, proposal);
            await db.SaveChangesAsync();

            transaction = await db.Database.BeginTransactionAsync();
            var stationRepository = (IStationRepository)CreateRepository(db, "StationRepository");
            var operatorStationRepository = (IOperatorStationRepository)CreateRepository(db, "OperatorStationRepository");
            var stopRepository = (IStopRepository)CreateRepository(db, "StopRepository");

            await stationRepository.AcquireForRouteProposalApprovalAsync(station.Id, CancellationToken.None);
            await operatorStationRepository.AcquireActiveForRouteProposalApprovalAsync(operatorId, station.Id, CancellationToken.None);
            await stopRepository.AcquireForRouteProposalApprovalAsync(
                [secondStop.Id, firstStop.Id],
                CancellationToken.None);

            interceptor.Commands.Should().Contain(command => command.Contains("stations", StringComparison.OrdinalIgnoreCase)
                && command.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase));
            interceptor.Commands.Should().Contain(command => command.Contains("operator_stations", StringComparison.OrdinalIgnoreCase)
                && command.Contains("ORDER BY id FOR UPDATE", StringComparison.OrdinalIgnoreCase));
            interceptor.Commands.Should().Contain(command => command.Contains("stops", StringComparison.OrdinalIgnoreCase)
                && command.Contains("ORDER BY id FOR UPDATE", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync();
                await transaction.DisposeAsync();
            }
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task CreationSourceCoordination_SerializesSourceExpiryAfterNewProposalCommit()
    {
        var databaseName = $"{Day29Fixture.ScratchDatabasePrefix}{Guid.NewGuid():N}";
        await using var setup = CreateDbContext(databaseName);
        try
        {
            await setup.Database.MigrateAsync();
            var seed = await Day29Fixture.SeedTripAsync(setup);
            var trip = await setup.Trips.SingleAsync(item => item.Id == seed.TripId);
            var route = await setup.Routes.SingleAsync(item => item.Id == trip.RouteId);
            var source = AlternativeRoute.Create(route.Id, "Concurrent source", route.DestinationStationId, null, null);
            setup.AlternativeRoutes.Add(source);
            await setup.SaveChangesAsync();
            setup.ChangeTracker.Clear();

            await using var creator = CreateDbContext(databaseName);
            await creator.Database.BeginTransactionAsync();
            var creatorProposals = (IRouteChangeProposalRepository)CreateRepository(creator, "RouteChangeProposalRepository");
            var creatorTrips = (ITripRepository)CreateRepository(creator, "TripRepository");
            var creatorRoutes = (IAlternativeRouteRepository)CreateRepository(creator, "AlternativeRouteRepository");
            await creatorProposals.AcquireSourceCoordinationLockAsync(source.Id, CancellationToken.None);
            var lockedTrip = (await creatorTrips.AcquireForRouteChangeAsync(seed.TripId, CancellationToken.None))!;
            var lockedSource = (await creatorRoutes.AcquireOwnedByIdAsync(seed.OperatorId, source.Id, CancellationToken.None))!;
            var proposal = CreateExistingProposal(lockedTrip, lockedSource);
            await creatorProposals.AddAsync(proposal, CancellationToken.None);
            await creator.SaveChangesAsync();

            var contenderStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var contender = Task.Run(async () =>
            {
                await using var db = CreateDbContext(databaseName);
                await db.Database.BeginTransactionAsync();
                var repository = (IRouteChangeProposalRepository)CreateRepository(db, "RouteChangeProposalRepository");
                contenderStarted.SetResult();
                await repository.AcquireSourceCoordinationLockAsync(source.Id, CancellationToken.None);
                var pending = await repository.AcquirePendingBySourceAsync(source.Id, CancellationToken.None);
                pending.Should().ContainSingle().Which.Expire("SOURCE_ROUTE_CHANGED", DateTimeOffset.UtcNow);
                await db.SaveChangesAsync();
                await db.Database.CommitTransactionAsync();
            });

            await contenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(150);
            contender.IsCompleted.Should().BeFalse("the source mutation must wait for proposal creation");
            await creator.Database.CommitTransactionAsync();
            await contender.WaitAsync(TimeSpan.FromSeconds(10));

            await using var assertion = CreateDbContext(databaseName);
            (await assertion.RouteChangeProposals.SingleAsync(item => item.Id == proposal.Id))
                .Status.Should().Be(RouteChangeProposalStatus.EXPIRED);
        }
        finally
        {
            await Day29Fixture.DeleteScratchDatabaseAsync(setup, databaseName);
        }
    }

    [Fact]
    public async Task CreationTripLock_SerializesTerminalTransitionAndExpiresNewProposal()
    {
        var databaseName = $"{Day29Fixture.ScratchDatabasePrefix}{Guid.NewGuid():N}";
        await using var setup = CreateDbContext(databaseName);
        try
        {
            await setup.Database.MigrateAsync();
            var seed = await Day29Fixture.SeedTripAsync(setup);
            var trip = await setup.Trips.SingleAsync(item => item.Id == seed.TripId);
            var route = await setup.Routes.SingleAsync(item => item.Id == trip.RouteId);
            var source = AlternativeRoute.Create(route.Id, "Terminal race source", route.DestinationStationId, null, null);
            setup.AlternativeRoutes.Add(source);
            await setup.SaveChangesAsync();
            setup.ChangeTracker.Clear();

            await using var creator = CreateDbContext(databaseName);
            await creator.Database.BeginTransactionAsync();
            var creatorProposals = (IRouteChangeProposalRepository)CreateRepository(creator, "RouteChangeProposalRepository");
            var creatorTrips = (ITripRepository)CreateRepository(creator, "TripRepository");
            var creatorRoutes = (IAlternativeRouteRepository)CreateRepository(creator, "AlternativeRouteRepository");
            await creatorProposals.AcquireSourceCoordinationLockAsync(source.Id, CancellationToken.None);
            var lockedTrip = (await creatorTrips.AcquireForRouteChangeAsync(seed.TripId, CancellationToken.None))!;
            var lockedSource = (await creatorRoutes.AcquireOwnedByIdAsync(seed.OperatorId, source.Id, CancellationToken.None))!;
            var proposal = CreateExistingProposal(lockedTrip, lockedSource);
            await creatorProposals.AddAsync(proposal, CancellationToken.None);
            await creator.SaveChangesAsync();

            var contenderStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var contender = Task.Run(async () =>
            {
                await using var db = CreateDbContext(databaseName);
                await db.Database.BeginTransactionAsync();
                var tripRepository = (ITripRepository)CreateRepository(db, "TripRepository");
                var proposalRepository = (IRouteChangeProposalRepository)CreateRepository(db, "RouteChangeProposalRepository");
                contenderStarted.SetResult();
                var terminalTrip = (await tripRepository.AcquireForRouteChangeAsync(seed.TripId, CancellationToken.None))!;
                var pending = await proposalRepository.AcquirePendingByTripAsync(seed.TripId, CancellationToken.None);
                pending.Should().ContainSingle().Which.Expire("TRIP_NO_LONGER_EDITABLE", DateTimeOffset.UtcNow);
                terminalTrip.Cancel(DateTimeOffset.UtcNow, Guid.NewGuid(), "Concurrent cancellation");
                await db.SaveChangesAsync();
                await db.Database.CommitTransactionAsync();
            });

            await contenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(150);
            contender.IsCompleted.Should().BeFalse("the terminal transition must wait for proposal creation");
            await creator.Database.CommitTransactionAsync();
            await contender.WaitAsync(TimeSpan.FromSeconds(10));

            await using var assertion = CreateDbContext(databaseName);
            (await assertion.RouteChangeProposals.SingleAsync(item => item.Id == proposal.Id))
                .Status.Should().Be(RouteChangeProposalStatus.EXPIRED);
            (await assertion.Trips.SingleAsync(item => item.Id == seed.TripId))
                .Status.Should().Be(TripStatus.CANCELLED);
        }
        finally
        {
            await Day29Fixture.DeleteScratchDatabaseAsync(setup, databaseName);
        }
    }

    [Fact]
    public async Task CreateService_WaitsForSourceWriterAndRejectsDeactivatedRoute()
    {
        var databaseName = $"{Day29Fixture.ScratchDatabasePrefix}{Guid.NewGuid():N}";
        await using var setup = CreateDbContext(databaseName);
        try
        {
            await setup.Database.MigrateAsync();
            var seed = await Day29Fixture.SeedTripAsync(setup);
            var trip = await setup.Trips.SingleAsync(item => item.Id == seed.TripId);
            var route = await setup.Routes.SingleAsync(item => item.Id == trip.RouteId);
            var source = AlternativeRoute.Create(route.Id, "Writer-locked source", route.DestinationStationId, null, null);
            setup.AlternativeRoutes.Add(source);
            await setup.SaveChangesAsync();
            setup.ChangeTracker.Clear();

            await using var writer = CreateDbContext(databaseName);
            await writer.Database.BeginTransactionAsync();
            var writerProposals = (IRouteChangeProposalRepository)CreateRepository(writer, "RouteChangeProposalRepository");
            var writerRoutes = (IAlternativeRouteRepository)CreateRepository(writer, "AlternativeRouteRepository");
            await writerProposals.AcquireSourceCoordinationLockAsync(source.Id, CancellationToken.None);
            var lockedSource = (await writerRoutes.AcquireOwnedByIdAsync(seed.OperatorId, source.Id, CancellationToken.None))!;
            lockedSource.Deactivate();
            await writer.SaveChangesAsync();

            await using var creator = CreateDbContext(databaseName);
            var service = CreateProposalService(creator);
            var createTask = service.CreateAsync(
                seed.TripId,
                trip.DriverUserId,
                "EXISTING",
                source.Id,
                null,
                null,
                "Writer race proposal",
                CancellationToken.None);
            await Task.Delay(150);
            createTask.IsCompleted.Should().BeFalse("creation must wait for the source writer transaction");

            await writer.Database.CommitTransactionAsync();
            Func<Task> action = async () => await createTask;
            var error = await action.Should().ThrowAsync<CodedNotFoundException>();
            error.Which.ErrorCode.Should().Be("ROUTE_NOT_FOUND");

            await using var assertion = CreateDbContext(databaseName);
            (await assertion.RouteChangeProposals.CountAsync()).Should().Be(0);
        }
        finally
        {
            await Day29Fixture.DeleteScratchDatabaseAsync(setup, databaseName);
        }
    }

    [Fact]
    public async Task CreateService_WaitsForTerminalWriterAndRejectsCancelledTrip()
    {
        var databaseName = $"{Day29Fixture.ScratchDatabasePrefix}{Guid.NewGuid():N}";
        await using var setup = CreateDbContext(databaseName);
        try
        {
            await setup.Database.MigrateAsync();
            var seed = await Day29Fixture.SeedTripAsync(setup);
            var seededTrip = await setup.Trips.SingleAsync(item => item.Id == seed.TripId);
            setup.ChangeTracker.Clear();

            await using var writer = CreateDbContext(databaseName);
            await writer.Database.BeginTransactionAsync();
            var writerTrips = (ITripRepository)CreateRepository(writer, "TripRepository");
            var lockedTrip = (await writerTrips.AcquireForRouteChangeAsync(seed.TripId, CancellationToken.None))!;
            lockedTrip.Cancel(DateTimeOffset.UtcNow, Guid.NewGuid(), "Terminal writer race");
            await writer.SaveChangesAsync();

            await using var creator = CreateDbContext(databaseName);
            var service = CreateProposalService(creator);
            var createTask = service.CreateAsync(
                seed.TripId,
                seededTrip.DriverUserId,
                "CUSTOM",
                null,
                null,
                null,
                "Terminal race proposal",
                CancellationToken.None);
            await Task.Delay(150);
            createTask.IsCompleted.Should().BeFalse("creation must wait for the terminal Trip writer transaction");

            await writer.Database.CommitTransactionAsync();
            Func<Task> action = async () => await createTask;
            var error = await action.Should().ThrowAsync<CodedConflictException>();
            error.Which.ErrorCode.Should().Be("TRIP_NOT_EDITABLE");

            await using var assertion = CreateDbContext(databaseName);
            (await assertion.RouteChangeProposals.CountAsync()).Should().Be(0);
        }
        finally
        {
            await Day29Fixture.DeleteScratchDatabaseAsync(setup, databaseName);
        }
    }

    [Fact]
    public async Task ApproveCustom_WhenOutboxFails_RollsBackPromotionTripProposalAuditAndOutbox()
    {
        var databaseName = $"{Day29Fixture.ScratchDatabasePrefix}{Guid.NewGuid():N}";
        await using var setup = CreateDbContext(databaseName);
        try
        {
            await setup.Database.MigrateAsync();
            var seed = await Day29Fixture.SeedTripAsync(setup);
            var trip = await setup.Trips.SingleAsync(item => item.Id == seed.TripId);
            var route = await setup.Routes.SingleAsync(item => item.Id == trip.RouteId);
            var operatorStation = OperatorStation.Create(seed.OperatorId, route.DestinationStationId);
            var proposal = RouteChangeProposal.Create(
                trip.Id,
                seed.OperatorId,
                trip.DriverUserId,
                RouteChangeProposalType.CUSTOM,
                null,
                null,
                null,
                "Atomic rollback proposal",
                "Rollback bypass",
                null,
                route.DestinationStationId,
                10m,
                20,
                "_p~iF~ps|U_ulLnnqC_mqNvxq`@");
            setup.AddRange(operatorStation, proposal);
            await setup.SaveChangesAsync();
            setup.ChangeTracker.Clear();

            var durableOutbox = new IntegrationEventOutbox(new OutboxStore(setup, new SystemClock()));
            var service = CreateProposalService(setup, new ThrowAfterStagingOutbox(durableOutbox));
            var action = () => service.ApproveAsync(seed.OperatorId, Guid.NewGuid(), proposal.Id, CancellationToken.None);

            await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("staged proposal outbox failure");

            await using var assertion = CreateDbContext(databaseName);
            var persistedProposal = await assertion.RouteChangeProposals.AsNoTracking().SingleAsync(item => item.Id == proposal.Id);
            persistedProposal.Status.Should().Be(RouteChangeProposalStatus.PENDING);
            persistedProposal.ApprovedAlternativeRouteId.Should().BeNull();
            (await assertion.Trips.AsNoTracking().SingleAsync(item => item.Id == trip.Id))
                .AlternativeRouteId.Should().BeNull();
            (await assertion.AlternativeRoutes.AsNoTracking().CountAsync()).Should().Be(0);
            (await assertion.AlternativeRouteStops.AsNoTracking().CountAsync()).Should().Be(0);
            (await assertion.TripAuditLogs.AsNoTracking().CountAsync(item => item.TripId == trip.Id)).Should().Be(0);
            (await assertion.OutboxEvents.AsNoTracking().CountAsync()).Should().Be(0);
        }
        finally
        {
            await Day29Fixture.DeleteScratchDatabaseAsync(setup, databaseName);
        }
    }

    private static RouteChangeProposal CreateExistingProposal(
        VietRide.Trip.Domain.Entities.Trip trip,
        AlternativeRoute source)
        => RouteChangeProposal.Create(
            trip.Id,
            trip.OperatorId,
            trip.DriverUserId,
            RouteChangeProposalType.EXISTING,
            source.Id,
            source.UpdatedAt,
            null,
            "Concurrent route proposal",
            source.Name,
            source.Description,
            source.DestinationStationId,
            source.TotalDistanceKm,
            source.EstimatedDurationMinutes,
            source.PathPolyline);

    private static object CreateRepository(TripDbContext db, string typeName)
    {
        var type = typeof(TripDbContext).Assembly.GetType(
            $"VietRide.Trip.Infrastructure.Persistence.Repositories.{typeName}",
            throwOnError: true)!;
        return Activator.CreateInstance(type, db)!;
    }

    private static RouteChangeProposalService CreateProposalService(
        TripDbContext db,
        IIntegrationEventOutbox? overriddenOutbox = null)
    {
        var outbox = overriddenOutbox ?? new IntegrationEventOutbox(new OutboxStore(db, new SystemClock()));
        var alternativeRoutes = (IAlternativeRouteRepository)CreateRepository(db, "AlternativeRouteRepository");
        return new RouteChangeProposalService(
            (IRouteChangeProposalRepository)CreateRepository(db, "RouteChangeProposalRepository"),
            (ITripRepository)CreateRepository(db, "TripRepository"),
            alternativeRoutes,
            (IRouteRepository)CreateRepository(db, "RouteRepository"),
            (IStationRepository)CreateRepository(db, "StationRepository"),
            (IOperatorStationRepository)CreateRepository(db, "OperatorStationRepository"),
            (IStopRepository)CreateRepository(db, "StopRepository"),
            (IIncidentRepository)CreateRepository(db, "IncidentRepository"),
            (ITripAuditLogRepository)CreateRepository(db, "TripAuditLogRepository"),
            new EmptyBookingImpactClient(),
            new TripRouteChangeService(alternativeRoutes, outbox),
            outbox,
            new EfUnitOfWork(db),
            new SystemClock());
    }

    private sealed class ThrowAfterStagingOutbox(IIntegrationEventOutbox inner) : IIntegrationEventOutbox
    {
        public Task EnqueueAsync(
            string eventType,
            string payloadJson,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task EnqueueAsync(
            Guid eventId,
            string eventType,
            string payloadJson,
            CancellationToken cancellationToken = default)
        {
            await inner.EnqueueAsync(eventId, eventType, payloadJson, cancellationToken);
            throw new InvalidOperationException("staged proposal outbox failure");
        }
    }

    private sealed class EmptyBookingImpactClient : IBookingImpactClient
    {
        public Task<TripBookingImpactProjection> GetTripEditImpactAsync(
            Guid tripId,
            Guid operatorId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new TripBookingImpactProjection(tripId, 0, []));
    }

    private static TripDbContext CreateDbContext(string databaseName = "route_proposal_model", params IInterceptor[] interceptors)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(CreateConnectionString(databaseName));
        dataSourceBuilder.MapEnum<OutboxEventStatus>(
            $"{TripDbContext.SchemaName}.outbox_event_status",
            new NpgsqlNullNameTranslator());
        TripDbContext.ConfigurePostgresEnums(dataSourceBuilder);
        var dataSource = dataSourceBuilder.Build();
        var options = new DbContextOptionsBuilder<TripDbContext>()
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .UseNpgsql(dataSource)
            .AddInterceptors(interceptors)
            .Options;
        return new TripDbContext(options, new SystemClock());
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string fallback = "Host=127.0.0.1;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var template = Environment.GetEnvironmentVariable("VIETRIDE_TRIP_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(template))
        {
            template = fallback;
        }

        var expanded = template.Contains("{databaseName}", StringComparison.OrdinalIgnoreCase)
            ? template.Replace("{databaseName}", databaseName, StringComparison.OrdinalIgnoreCase)
            : template;
        return new NpgsqlConnectionStringBuilder(expanded) { Database = databaseName }.ConnectionString;
    }

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
