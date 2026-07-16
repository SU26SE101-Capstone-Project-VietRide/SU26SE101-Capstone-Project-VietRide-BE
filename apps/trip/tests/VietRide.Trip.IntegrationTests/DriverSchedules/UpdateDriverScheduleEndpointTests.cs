using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Shared.Persistence.UnitOfWork;
using VietRide.Shared.Web.Idempotency;
using VietRide.Trip.Api.Controllers;
using VietRide.Trip.Api.Controllers.Requests;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Jobs;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Features.DriverSchedules;
using VietRide.Trip.Domain.Entities;
using VietRide.Trip.Infrastructure;

namespace VietRide.Trip.IntegrationTests.DriverSchedules;

public sealed class UpdateDriverScheduleEndpointTests
{
    [Fact]
    public void CanonicalAndCrewEndpoints_ArePatchIdempotentAndDistinctRoutes()
    {
        var methods = typeof(OperatorDriverSchedulesController).GetMethods(BindingFlags.Public | BindingFlags.Instance);
        var canonical = methods.Single(method => method.Name == nameof(OperatorDriverSchedulesController.Update));
        var crew = methods.Single(method => method.Name == nameof(OperatorDriverSchedulesController.UpdateCrew));

        canonical.GetCustomAttribute<HttpPatchAttribute>()!.Template.Should().Be("{id:guid}");
        crew.GetCustomAttribute<HttpPatchAttribute>()!.Template.Should().Be("{id:guid}/crew");
        canonical.GetCustomAttribute<RequireIdempotencyAttribute>().Should().NotBeNull();
        crew.GetCustomAttribute<RequireIdempotencyAttribute>().Should().NotBeNull();
    }

    [Fact]
    public void RequestModel_DistinguishesOmittedFromExplicitNullAndRejectsUnknownFields()
    {
        var omitted = JsonSerializer.Deserialize<UpdateDriverScheduleRequest>("{\"isActive\":true}", WebOptions())!;
        var explicitNull = JsonSerializer.Deserialize<UpdateDriverScheduleRequest>("{\"vehicleId\":null}", WebOptions())!;

        omitted.IsActiveSpecified.Should().BeTrue();
        omitted.VehicleIdSpecified.Should().BeFalse();
        explicitNull.VehicleIdSpecified.Should().BeTrue();
        explicitNull.VehicleId.Should().BeNull();
        Action unknown = () => JsonSerializer.Deserialize<UpdateDriverScheduleRequest>("{\"routeId\":null}", WebOptions());
        unknown.Should().Throw<JsonException>();
    }

    [Fact]
    public async Task OverlapAdvisoryLock_SerializesConcurrentSchedulesSharingEffectiveDriverResource()
    {
        var databaseName = $"vietride_driver_schedule_overlap_{Guid.NewGuid():N}";
        await using (var setup = CreateDbContext(databaseName))
        {
            await setup.Database.MigrateAsync();
        }

        try
        {
            await using var firstDb = CreateDbContext(databaseName);
            await using var secondDb = CreateDbContext(databaseName);
            var firstRepository = CreateRepository(firstDb);
            var secondRepository = CreateRepository(secondDb);
            var driverId = Guid.NewGuid();
            await using var firstTransaction = await firstDb.Database.BeginTransactionAsync();
            await firstRepository.AcquireOverlapLocksAsync(
                driverId, null, null, [1, 3], new TimeOnly(8, 0),
                new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 1));

            var contenderStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var contender = Task.Run(async () =>
            {
                await using var secondTransaction = await secondDb.Database.BeginTransactionAsync();
                contenderStarted.SetResult();
                await secondRepository.AcquireOverlapLocksAsync(
                    driverId, null, null, [3], new TimeOnly(8, 0),
                    new DateOnly(2026, 7, 15), null);
                await secondTransaction.RollbackAsync();
            });

            await contenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(150);
            contender.IsCompleted.Should().BeFalse(
                "overlapping edits sharing the driver resource must serialize before conflict revalidation");

            await firstTransaction.CommitAsync();
            await contender.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await using var cleanup = CreateDbContext(databaseName);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task UpdateHandler_WaitingOnTrackedScheduleReloadsCommittedStateAndRejectsStaleWrite()
    {
        var databaseName = $"vietride_driver_schedule_stale_{Guid.NewGuid():N}";
        ScheduleSeed seed;
        await using (var setup = CreateDbContext(databaseName))
        {
            await setup.Database.MigrateAsync();
            seed = SeedSchedule(setup);
            await setup.SaveChangesAsync();
        }

        try
        {
            await using var waiterDb = CreateDbContext(databaseName);
            await using var winnerDb = CreateDbContext(databaseName);
            await waiterDb.Database.OpenConnectionAsync();
            await winnerDb.Database.OpenConnectionAsync();
            var waiterBackendPid = await GetBackendPidAsync(waiterDb);
            var winnerBackendPid = await GetBackendPidAsync(winnerDb);
            var waiterRepository = CreateRepository(waiterDb);
            var winnerRepository = CreateRepository(winnerDb);

            var preflight = await waiterRepository.GetByIdAsync(seed.ScheduleId, CancellationToken.None);
            preflight.Should().NotBeNull();
            preflight!.DepartureTime.Should().Be(new TimeOnly(8, 0));

            await using var winnerTransaction = await winnerDb.Database.BeginTransactionAsync();
            var winner = await winnerRepository.AcquireOwnedForUpdateAsync(seed.ScheduleId, seed.OperatorId);
            winner.Should().NotBeNull();
            using (var days = JsonDocument.Parse("[1,3,5]"))
            {
                winner!.UpdateRecurrence(
                    new TimeOnly(9, 0),
                    days.RootElement,
                    winner.DriverUserId,
                    winner.AssistantUserId,
                    winner.VehicleId,
                    winner.ValidUntil,
                    winner.IsActive);
            }

            await winnerDb.SaveChangesAsync();

            var handler = new UpdateDriverScheduleHandler(
                waiterRepository,
                Unexpected<IDriverScheduleAuditLogRepository>(),
                Unexpected<ITripRepository>(),
                Unexpected<ITripSeatRepository>(),
                Unexpected<ITripStopRepository>(),
                Unexpected<ITripAuditLogRepository>(),
                Unexpected<IVehicleRepository>(),
                Unexpected<IRouteRepository>(),
                new AllowedIdentityClient(),
                Unexpected<IBookingImpactClient>(),
                Unexpected<ITripVehicleSwapService>(),
                Unexpected<VietRide.Shared.Application.Outbox.IIntegrationEventOutbox>(),
                Unexpected<ITripGenerationJobScheduler>(),
                new EfUnitOfWork(waiterDb),
                new SystemClock());
            var command = new UpdateDriverScheduleCommand(
                seed.OperatorId,
                seed.ScheduleId,
                Guid.NewGuid(),
                Guid.NewGuid().ToString("D"),
                UpdateDriverScheduleCommand.FutureOnly,
                DepartureTimeSpecified: false,
                DepartureTime: null,
                DayOfWeekSpecified: false,
                DayOfWeek: null,
                DriverUserIdSpecified: false,
                DriverUserId: null,
                AssistantUserIdSpecified: false,
                AssistantUserId: null,
                VehicleIdSpecified: false,
                VehicleId: null,
                ValidUntilSpecified: false,
                ValidUntil: null,
                IsActiveSpecified: true,
                IsActive: false);

            var waiter = handler.Handle(command, CancellationToken.None);
            await WaitUntilBlockedByAsync(databaseName, waiterBackendPid, winnerBackendPid);

            await winnerTransaction.CommitAsync();

            Func<Task> awaitHandler = async () => await waiter;
            var exception = await awaitHandler.Should().ThrowAsync<CodedConflictException>();
            exception.Which.ErrorCode.Should().Be("TRIP_NOT_EDITABLE");
            waiterDb.DriverSchedules.Local.Single(schedule => schedule.Id == seed.ScheduleId)
                .DepartureTime.Should().Be(new TimeOnly(9, 0),
                    "the locked tracked entity must be reloaded after the competing commit");

            await using var verification = CreateDbContext(databaseName);
            var persisted = await verification.DriverSchedules.AsNoTracking()
                .SingleAsync(schedule => schedule.Id == seed.ScheduleId);
            persisted.DepartureTime.Should().Be(new TimeOnly(9, 0));
            persisted.IsActive.Should().BeTrue("the stale handler transaction must roll back without overwriting the winner");
        }
        finally
        {
            await using var cleanup = CreateDbContext(databaseName);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static JsonSerializerOptions WebOptions() => new(JsonSerializerDefaults.Web);

    private static TripDbContext CreateDbContext(string databaseName)
    {
        var connectionString = CreateConnectionString(databaseName);
        var options = new DbContextOptionsBuilder<TripDbContext>()
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(warnings => warnings.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", TripDbContext.SchemaName))
            .Options;
        return new TripDbContext(options, new SystemClock());
    }

    private static string CreateConnectionString(string databaseName)
    {
        const string fallback = "Host=localhost;Port=5432;Database={databaseName};Username=vietride;Password=vietride_dev";
        var template = Environment.GetEnvironmentVariable("VIETRIDE_TRIP_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(template))
        {
            template = fallback;
        }

        return template.Contains("{databaseName}", StringComparison.OrdinalIgnoreCase)
            ? template.Replace("{databaseName}", databaseName, StringComparison.OrdinalIgnoreCase)
            : template;
    }

    private static async Task<int> GetBackendPidAsync(TripDbContext dbContext)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_backend_pid()";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task WaitUntilBlockedByAsync(
        string databaseName,
        int waiterBackendPid,
        int winnerBackendPid)
    {
        await using var observerDb = CreateDbContext(databaseName);
        await observerDb.Database.OpenConnectionAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            while (true)
            {
                await using var command = observerDb.Database.GetDbConnection().CreateCommand();
                command.CommandText = "SELECT @winner_pid = ANY(pg_blocking_pids(@waiter_pid))";

                var winnerParameter = command.CreateParameter();
                winnerParameter.ParameterName = "winner_pid";
                winnerParameter.Value = winnerBackendPid;
                command.Parameters.Add(winnerParameter);

                var waiterParameter = command.CreateParameter();
                waiterParameter.ParameterName = "waiter_pid";
                waiterParameter.Value = waiterBackendPid;
                command.Parameters.Add(waiterParameter);

                if (await command.ExecuteScalarAsync(timeout.Token) is true)
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"PostgreSQL did not report backend {winnerBackendPid} blocking backend {waiterBackendPid} within 5 seconds.");
        }
    }

    private static IDriverScheduleRepository CreateRepository(TripDbContext dbContext)
    {
        var repositoryType = typeof(TripDbContext).Assembly.GetType(
            "VietRide.Trip.Infrastructure.Persistence.Repositories.DriverScheduleRepository",
            throwOnError: true)!;
        return (IDriverScheduleRepository)Activator.CreateInstance(repositoryType, dbContext)!;
    }

    private static ScheduleSeed SeedSchedule(TripDbContext dbContext)
    {
        var operatorId = Guid.NewGuid();
        var origin = Station.Create(
            "Stale Guard Origin",
            $"stale-origin-{Guid.NewGuid():N}",
            "Ho Chi Minh City",
            "Ho Chi Minh City",
            latitude: 10.7769m,
            longitude: 106.7009m);
        var destination = Station.Create(
            "Stale Guard Destination",
            $"stale-destination-{Guid.NewGuid():N}",
            "Da Lat",
            "Lam Dong",
            latitude: 11.9404m,
            longitude: 108.4583m);
        var route = VietRide.Trip.Domain.Entities.Route.Create(
            operatorId,
            "Driver schedule stale guard route",
            origin.Id,
            destination.Id,
            Money.FromRaw(100_000),
            300m,
            360);
        using var days = JsonDocument.Parse("[1,3,5]");
        var schedule = DriverSchedule.Create(
            operatorId,
            route.Id,
            vehicleId: null,
            Guid.NewGuid(),
            assistantUserId: null,
            days.RootElement,
            new TimeOnly(8, 0),
            new DateOnly(2026, 7, 1),
            validUntil: null,
            isActive: true);

        dbContext.AddRange(origin, destination, route, schedule);
        return new ScheduleSeed(operatorId, schedule.Id);
    }

    private static T Unexpected<T>() where T : class =>
        DispatchProxy.Create<T, UnexpectedDependencyProxy>();

    private sealed class AllowedIdentityClient : IIdentityInternalClient
    {
        public Task<OperatorWriteEligibilityValidation> ValidateOperatorCanWriteAsync(
            Guid operatorId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperatorWriteEligibilityValidation.Allowed());

        public Task<IdentityUserLookupResult> GetUserAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No Identity user lookup is expected for an isActive-only stale update.");
    }

    public class UnexpectedDependencyProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"Unexpected dependency call: {targetMethod?.DeclaringType?.Name}.{targetMethod?.Name}");
    }

    private sealed record ScheduleSeed(Guid OperatorId, Guid ScheduleId);
}
