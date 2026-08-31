using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Npgsql.NameTranslation;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Application.Features.Parcels.Unload;
using VietRide.Parcel.Application.Features.Reliability.ReportIncident;
using VietRide.Parcel.Application.Services;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Parcel.Infrastructure;
using VietRide.Shared.Application.Behaviors;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Shared.Persistence.UnitOfWork;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.IntegrationTests.Persistence;

public sealed class Day39ParcelDeliveryTransitionPersistenceTests
{
    [Fact]
    public async Task PassengerIncident_TerminalRejectsCleanlyThenUnloadedCommitsAllRecords()
    {
        var databaseName = $"vietride_parcel_incident_atomic_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            var terminalParcel = CreateParcel("VRP-INCIDENT-TERMINAL");
            var reportableParcel = CreateParcel("VRP-INCIDENT-UNLOADED");

            await using (var seedContext = CreateDbContext(dataSource))
            {
                await seedContext.Database.MigrateAsync();
                seedContext.Parcels.AddRange(terminalParcel, reportableParcel);
                await seedContext.SaveChangesAsync();
                await SetStatusAsync(seedContext, terminalParcel.Id, ParcelStatus.DELIVERY_CONFIRMED);
                await SetStatusAsync(seedContext, reportableParcel.Id, ParcelStatus.UNLOADED);
            }

            await using (var rejectedContext = CreateDbContext(dataSource))
            {
                var terminalCommand = new ReportParcelIncidentCommand(
                    terminalParcel.Id,
                    terminalParcel.SenderUserId,
                    null,
                    ParcelIncidentType.DELIVERY_NOT_RECEIVED.ToString(),
                    "Passenger did not receive the parcel.",
                    []);
                var terminalAction = async () => await ExecuteIncidentReportAsync(
                    rejectedContext,
                    terminalCommand);
                var rejected = await terminalAction.Should().ThrowAsync<CodedConflictException>();
                rejected.Which.ErrorCode.Should().Be("PARCEL_INCIDENT_STATUS_NOT_REPORTABLE");
            }

            await using (var rejectedAssertionContext = CreateDbContext(dataSource))
            {
                var persistedTerminal = await rejectedAssertionContext.Parcels
                    .AsNoTracking()
                    .SingleAsync(item => item.Id == terminalParcel.Id);
                persistedTerminal.Status.Should().Be(ParcelStatus.DELIVERY_CONFIRMED);
                (await rejectedAssertionContext.ParcelIncidents.CountAsync(item =>
                    item.ParcelId == terminalParcel.Id)).Should().Be(0);
                (await rejectedAssertionContext.ParcelSearchTasks.CountAsync(item =>
                    item.ParcelId == terminalParcel.Id)).Should().Be(0);
                (await rejectedAssertionContext.ParcelCustodyEvents.CountAsync(item =>
                    item.ParcelId == terminalParcel.Id)).Should().Be(0);
                (await rejectedAssertionContext.OutboxEvents.CountAsync()).Should().Be(0);
            }

            ReportParcelIncidentResponse accepted;
            await using (var acceptedContext = CreateDbContext(dataSource))
            {
                accepted = await ExecuteIncidentReportAsync(
                    acceptedContext,
                    new ReportParcelIncidentCommand(
                        reportableParcel.Id,
                        reportableParcel.SenderUserId,
                        null,
                        ParcelIncidentType.DAMAGED.ToString(),
                        "Parcel was damaged at delivery.",
                        ["https://example.test/damage-evidence"]));
            }

            await using var assertionContext = CreateDbContext(dataSource);
            var persistedReportable = await assertionContext.Parcels
                .AsNoTracking()
                .SingleAsync(item => item.Id == reportableParcel.Id);
            persistedReportable.Status.Should().Be(ParcelStatus.PENDING_OPERATOR_ACTION);
            persistedReportable.PendingActionType.Should().Be(PendingActionType.CUSTODY_EXCEPTION);
            persistedReportable.PendingActionResumeStatus.Should().Be(ParcelStatus.UNLOADED);
            (await assertionContext.ParcelIncidents.CountAsync(item =>
                item.Id == accepted.IncidentId && item.ParcelId == reportableParcel.Id)).Should().Be(1);
            (await assertionContext.ParcelSearchTasks.CountAsync(item =>
                item.ParcelId == reportableParcel.Id)).Should().Be(3);
            (await assertionContext.ParcelCustodyEvents.CountAsync(item =>
                item.ParcelId == reportableParcel.Id
                && item.EventType == ParcelCustodyEventType.EXCEPTION_REPORTED)).Should().Be(1);
            (await assertionContext.OutboxEvents.CountAsync(item =>
                item.EventType == ParcelOutboxEvents.CustodyEventRecorded)).Should().Be(1);
            (await assertionContext.OutboxEvents.CountAsync(item =>
                item.EventType == ParcelOutboxEvents.IncidentOpened)).Should().Be(1);
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    [Fact]
    public async Task WrongStopThenTargetStop_RejectsWithoutWritesThenUnloadsAtomically()
    {
        var databaseName = $"vietride_parcel_wrong_then_target_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            var operatorId = Guid.NewGuid();
            var tripId = Guid.NewGuid();
            var assistantId = Guid.NewGuid();
            var vehicleId = Guid.NewGuid();
            var intendedDropoffStopId = Guid.NewGuid();
            var wrongStopId = Guid.NewGuid();
            var parcel = CreateParcel(
                "VRP-WRONG-THEN-TARGET",
                operatorId,
                tripId,
                intendedDropoffStopId);
            var forwardingIncident = ParcelIncident.Open(
                parcel.Id,
                operatorId,
                ParcelIncidentType.WRONG_STOP,
                DateTimeOffset.UtcNow.AddHours(6),
                tripId,
                null,
                assistantId,
                "ASSISTANT",
                $"STOP:{intendedDropoffStopId:D}",
                "Forwarding vehicle",
                "Forwarding to the intended drop-off stop.",
                null,
                operatorProcessBreach: false);
            forwardingIncident.StartSearch();
            forwardingIncident.MarkFound("Found on the forwarding vehicle.");
            forwardingIncident.StartForwarding();

            await using (var seedContext = CreateDbContext(dataSource))
            {
                await seedContext.Database.MigrateAsync();
                seedContext.Parcels.Add(parcel);
                seedContext.ParcelIncidents.Add(forwardingIncident);
                await seedContext.SaveChangesAsync();
                await SetStatusAsync(seedContext, parcel.Id, ParcelStatus.IN_TRANSIT);
            }

            var tripSnapshot = new TripParcelSnapshot(
                tripId,
                operatorId,
                Guid.NewGuid(),
                vehicleId,
                "IN_PROGRESS",
                DateTimeOffset.UtcNow.AddHours(-2),
                DateTimeOffset.UtcNow.AddHours(2),
                100_000,
                new TripStationDto(Guid.NewGuid(), "Origin"),
                new TripStationDto(Guid.NewGuid(), "Destination"),
                [
                    new TripStopDto(
                        intendedDropoffStopId,
                        1,
                        true,
                        true,
                        DateTimeOffset.UtcNow,
                        50,
                        null,
                        "ARRIVED",
                        DateTimeOffset.UtcNow,
                        null),
                ],
                new TripSeatSummaryDto(20, 10),
                null,
                null,
                Guid.NewGuid(),
                assistantId);
            var tripClient = TripServiceClientProxy.Create(
                tripSnapshot,
                new TripOperationalLocationSnapshot(
                    tripId,
                    vehicleId,
                    "IN_PROGRESS",
                    intendedDropoffStopId,
                    "ARRIVED",
                    DateTimeOffset.UtcNow,
                    null,
                    null));

            await using (var handlerContext = CreateDbContext(dataSource))
            {
                var clock = new SystemClock();
                var outbox = new IntegrationEventOutbox(new OutboxStore(handlerContext, clock));
                var reliability = CreateReliabilityRepository(handlerContext);
                var handler = new UnloadParcelCommandHandler(
                    CreateRepository(handlerContext),
                    tripClient,
                    outbox,
                    new EfUnitOfWork(handlerContext),
                    new ParcelCustodyService(reliability, outbox, clock),
                    reliability);

                var wrongStopAction = async () => await handler.Handle(
                    new UnloadParcelCommand(
                        parcel.Id,
                        assistantId,
                        operatorId,
                        Guid.NewGuid(),
                        "ROUTE_STOP",
                        wrongStopId,
                        [],
                        parcel.ParcelCode),
                    CancellationToken.None);
                var mismatch = await wrongStopAction.Should()
                    .ThrowAsync<CodedConflictException>();
                mismatch.Which.ErrorCode.Should().Be("PARCEL_CUSTODY_LOCATION_MISMATCH");

                handlerContext.ChangeTracker.Clear();
                var afterWrongStop = await handlerContext.Parcels
                    .AsNoTracking()
                    .SingleAsync(item => item.Id == parcel.Id);
                afterWrongStop.Status.Should().Be(ParcelStatus.IN_TRANSIT);
                afterWrongStop.UnloadedAt.Should().BeNull();
                (await handlerContext.OutboxEvents.CountAsync()).Should().Be(0);
                var incidentAfterWrongStop = await handlerContext.ParcelIncidents
                    .AsNoTracking()
                    .SingleAsync(item => item.Id == forwardingIncident.Id);
                incidentAfterWrongStop.Status.Should().Be(ParcelIncidentStatus.FORWARDING);

                var unloaded = await handler.Handle(
                    new UnloadParcelCommand(
                        parcel.Id,
                        assistantId,
                        operatorId,
                        Guid.NewGuid(),
                        "ROUTE_STOP",
                        intendedDropoffStopId,
                        [],
                        parcel.ParcelCode),
                    CancellationToken.None);
                unloaded.Status.Should().Be(ParcelStatus.UNLOADED.ToString());
            }

            await using var assertionContext = CreateDbContext(dataSource);
            var persisted = await assertionContext.Parcels
                .AsNoTracking()
                .SingleAsync(item => item.Id == parcel.Id);
            persisted.Status.Should().Be(ParcelStatus.UNLOADED);
            persisted.UnloadedAt.Should().NotBeNull();
            var resolvedIncident = await assertionContext.ParcelIncidents
                .AsNoTracking()
                .SingleAsync(item => item.Id == forwardingIncident.Id);
            resolvedIncident.Status.Should().Be(ParcelIncidentStatus.RESOLVED);
            resolvedIncident.ResolutionCode.Should().Be("FORWARDED_TO_EXPECTED_DROPOFF");
            (await assertionContext.ParcelCustodyEvents.CountAsync(item =>
                item.ParcelId == parcel.Id
                && item.EventType == ParcelCustodyEventType.UNLOADED)).Should().Be(1);
            (await assertionContext.OutboxEvents.CountAsync(item =>
                item.EventType == ParcelOutboxEvents.Unloaded)).Should().Be(1);
            (await assertionContext.OutboxEvents.CountAsync(item =>
                item.EventType == ParcelOutboxEvents.CustodyEventRecorded)).Should().Be(1);
            (await assertionContext.OutboxEvents.CountAsync(item =>
                item.EventType == ParcelOutboxEvents.IncidentUpdated)).Should().Be(1);
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    [Fact]
    public async Task ConcurrentUnloadAndDeliver_AllowOneCasWinner_AndPreserveConfirmationFlow()
    {
        var databaseName = $"vietride_parcel_day39_delivery_{Guid.NewGuid():N}";
        var connectionString = CreateConnectionString(databaseName);
        await CreateDatabaseAsync(connectionString, databaseName);

        try
        {
            await using var dataSource = CreateDataSource(connectionString);
            var unloadParcel = CreateParcel("VRP-DAY39-UNLOAD");
            var deliverParcel = CreateParcel("VRP-DAY39-DELIVER");

            await using (var seedContext = CreateDbContext(dataSource))
            {
                await seedContext.Database.MigrateAsync();
                seedContext.Parcels.AddRange(unloadParcel, deliverParcel);
                await seedContext.SaveChangesAsync();
                await SetStatusAsync(seedContext, unloadParcel.Id, ParcelStatus.IN_TRANSIT);
                await SetStatusAsync(seedContext, deliverParcel.Id, ParcelStatus.UNLOADED);
            }

            var unloadTimes = new[]
            {
                new DateTimeOffset(2026, 7, 15, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 15, 8, 0, 1, TimeSpan.Zero),
            };
            var unloadResults = await RunConcurrentAsync(
                dataSource,
                (repository, index) => repository.TryMarkUnloadedAsync(
                    unloadParcel.Id,
                    unloadTimes[index],
                    CancellationToken.None));

            unloadResults.Count(result => result is not null).Should().Be(1);

            await using (var readContext = CreateDbContext(dataSource))
            {
                var persisted = await readContext.Parcels
                    .AsNoTracking()
                    .SingleAsync(parcel => parcel.Id == unloadParcel.Id);
                persisted.Status.Should().Be(ParcelStatus.UNLOADED);
                persisted.UnloadedAt.Should().BeOneOf(unloadTimes);
                persisted.DeliveredPendingConfirmAt.Should().BeNull();
                (await readContext.ParcelDeliveryTokens
                    .AnyAsync(token => token.ParcelId == unloadParcel.Id))
                    .Should()
                    .BeFalse();
            }

            await using (var replayContext = CreateDbContext(dataSource))
            {
                var replay = await CreateRepository(replayContext).TryMarkUnloadedAsync(
                    unloadParcel.Id,
                    unloadTimes[0].AddMinutes(1),
                    CancellationToken.None);
                replay.Should().BeNull();
            }

            var deliveryAttempts = new[]
            {
                new DeliveryAttempt(
                    new DateTimeOffset(2026, 7, 15, 9, 0, 0, TimeSpan.Zero),
                    new[] { "https://storage.googleapis.com/test/delivery-1.webp" }),
                new DeliveryAttempt(
                    new DateTimeOffset(2026, 7, 15, 9, 0, 1, TimeSpan.Zero),
                    new[] { "https://storage.googleapis.com/test/delivery-2.webp" }),
            };
            var deliveryResults = await RunConcurrentAsync(
                dataSource,
                (repository, index) => repository.TryMarkDeliveredPendingConfirmAsync(
                    deliverParcel.Id,
                    deliveryAttempts[index].PhotoUrls,
                    deliveryAttempts[index].DeliveredAt,
                    CancellationToken.None));

            deliveryResults.Count(result => result is not null).Should().Be(1);

            var rawToken = Guid.NewGuid();
            Guid persistedTokenId;
            await using (var readContext = CreateDbContext(dataSource))
            {
                var persisted = await readContext.Parcels
                    .AsNoTracking()
                    .SingleAsync(parcel => parcel.Id == deliverParcel.Id);
                var winningAttempt = deliveryAttempts.Single(
                    attempt => attempt.DeliveredAt == persisted.DeliveredPendingConfirmAt);

                persisted.Status.Should().Be(ParcelStatus.DELIVERED_PENDING_CONFIRM);
                persisted.DeliveredPendingConfirmAt.Should().Be(winningAttempt.DeliveredAt);
                persisted.DeliveryPhotoUrls.Should().Equal(winningAttempt.PhotoUrls);

                var deliveryToken = ParcelDeliveryToken.Issue(
                    deliverParcel.Id,
                    DeliveryTokenHasher.Hash(rawToken),
                    winningAttempt.DeliveredAt.AddHours(48),
                    Guid.NewGuid(),
                    ParcelDeliveryTokenIssueReason.INITIAL_DELIVERY,
                    winningAttempt.DeliveredAt);
                readContext.ParcelDeliveryTokens.Add(deliveryToken);
                await readContext.SaveChangesAsync();
                persistedTokenId = deliveryToken.Id;
            }

            await using (var confirmationContext = CreateDbContext(dataSource))
            {
                var confirmation = await CreateRepository(confirmationContext).TryConfirmDeliveryAsync(
                    deliverParcel.Id,
                    persistedTokenId,
                    "127.0.0.1",
                    new DateTimeOffset(2026, 7, 15, 9, 30, 0, TimeSpan.Zero),
                    CancellationToken.None);
                confirmation.Should().NotBeNull();
                confirmation!.Status.Should().Be(ParcelStatus.DELIVERY_CONFIRMED);
            }
        }
        finally
        {
            await DropDatabaseAsync(connectionString, databaseName);
        }
    }

    private static async Task<ParcelPaymentTransitionSnapshot?[]> RunConcurrentAsync(
        NpgsqlDataSource dataSource,
        Func<IParcelRepository, int, Task<ParcelPaymentTransitionSnapshot?>> transition)
    {
        await using var firstContext = CreateDbContext(dataSource);
        await using var secondContext = CreateDbContext(dataSource);
        var first = transition(CreateRepository(firstContext), 0);
        var second = transition(CreateRepository(secondContext), 1);
        return await Task.WhenAll(first, second);
    }

    private static ParcelEntity CreateParcel(
        string parcelCode,
        Guid? operatorId = null,
        Guid? tripId = null,
        Guid? dropoffStopId = null)
        => ParcelEntity.CreatePendingPayment(
            parcelCode,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Recipient",
            PhoneNumber.Normalize("+84912345678"),
            "recipient@example.com",
            operatorId ?? Guid.NewGuid(),
            tripId ?? Guid.NewGuid(),
            dropoffStopId,
            null,
            "Item",
            null,
            ParcelSizeCategory.MEDIUM,
            5m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(100_000));

    private static async Task SetStatusAsync(
        ParcelDbContext dbContext,
        Guid parcelId,
        ParcelStatus status)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE vietride_parcel.parcels
            SET status = CAST({status.ToString()} AS vietride_parcel.parcel_status)
            WHERE id = {parcelId};
            """);
    }

    private static IParcelRepository CreateRepository(ParcelDbContext dbContext)
    {
        var repositoryType = typeof(ParcelDbContext).Assembly.GetType(
            "VietRide.Parcel.Infrastructure.Persistence.Repositories.ParcelRepository",
            throwOnError: true)!;

        return (IParcelRepository)Activator.CreateInstance(repositoryType, dbContext)!;
    }

    private static IParcelReliabilityRepository CreateReliabilityRepository(ParcelDbContext dbContext)
    {
        var repositoryType = typeof(ParcelDbContext).Assembly.GetType(
            "VietRide.Parcel.Infrastructure.Persistence.Repositories.ParcelReliabilityRepository",
            throwOnError: true)!;

        return (IParcelReliabilityRepository)Activator.CreateInstance(repositoryType, dbContext)!;
    }

    private static async Task<ReportParcelIncidentResponse> ExecuteIncidentReportAsync(
        ParcelDbContext dbContext,
        ReportParcelIncidentCommand command)
    {
        var clock = new SystemClock();
        var outbox = new IntegrationEventOutbox(new OutboxStore(dbContext, clock));
        var reliability = CreateReliabilityRepository(dbContext);
        var handler = new ReportParcelIncidentCommandHandler(
            CreateRepository(dbContext),
            reliability,
            new ParcelCustodyService(reliability, outbox, clock),
            outbox,
            clock);
        var behavior = new TransactionBehavior<ReportParcelIncidentCommand, ReportParcelIncidentResponse>(
            NullLogger<TransactionBehavior<ReportParcelIncidentCommand, ReportParcelIncidentResponse>>.Instance,
            new EfUnitOfWork(dbContext));
        return await behavior.Handle(
            command,
            () => handler.Handle(command, CancellationToken.None),
            CancellationToken.None);
    }

    private static NpgsqlDataSource CreateDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        ParcelDbContext.ConfigurePostgresTypes(builder);
        builder.MapEnum<OutboxEventStatus>(
            $"{ParcelDbContext.SchemaName}.outbox_event_status",
            new NpgsqlNullNameTranslator());
        return builder.Build();
    }

    private static ParcelDbContext CreateDbContext(NpgsqlDataSource dataSource)
    {
        var options = ParcelIntegrationDbContextOptions.Create(dataSource);

        return new ParcelDbContext(options, new SystemClock());
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string defaultConnectionString =
            "Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var configuredConnectionString =
            Environment.GetEnvironmentVariable("VIETRIDE_PARCEL_TEST_CONNECTION_STRING");
        var connectionString = string.IsNullOrWhiteSpace(configuredConnectionString)
            ? defaultConnectionString
            : configuredConnectionString;

        connectionString = connectionString.Replace(
            "{databaseName}",
            databaseName,
            StringComparison.OrdinalIgnoreCase);
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = databaseName,
        };

        return builder.ConnectionString;
    }

    private static async Task CreateDatabaseAsync(string connectionString, string databaseName)
    {
        var adminConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres",
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\";", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string connectionString, string databaseName)
    {
        var adminConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres",
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record DeliveryAttempt(
        DateTimeOffset DeliveredAt,
        IReadOnlyCollection<string> PhotoUrls);

    private class TripServiceClientProxy : DispatchProxy
    {
        private TripParcelSnapshot snapshot = null!;
        private TripOperationalLocationSnapshot operationalLocation = null!;

        public static ITripServiceClient Create(
            TripParcelSnapshot snapshot,
            TripOperationalLocationSnapshot operationalLocation)
        {
            var client = Create<ITripServiceClient, TripServiceClientProxy>();
            var proxy = (TripServiceClientProxy)(object)client;
            proxy.snapshot = snapshot;
            proxy.operationalLocation = operationalLocation;
            return client;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod?.Name switch
            {
                nameof(ITripServiceClient.AuthorizeAssistantForTripAsync) => Task.FromResult(
                    new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Authorized)),
                nameof(ITripServiceClient.GetTripParcelSnapshotAsync) => Task.FromResult(
                    new TripSnapshotOutcome(TripSnapshotOutcomeKind.Success, snapshot, null)),
                nameof(ITripServiceClient.GetTripOperationalLocationAsync) => Task.FromResult(
                    new TripOperationalLocationOutcome(
                        TripOperationalLocationOutcomeKind.Success,
                        operationalLocation,
                        null)),
                nameof(ITripServiceClient.ReleaseCargoAsync) => Task.FromResult(
                    new TripCargoOutcome(TripCargoOutcomeKind.Success, null)),
                _ => throw new NotSupportedException(
                    $"Unexpected Trip client call '{targetMethod?.Name}'."),
            };
    }
}
