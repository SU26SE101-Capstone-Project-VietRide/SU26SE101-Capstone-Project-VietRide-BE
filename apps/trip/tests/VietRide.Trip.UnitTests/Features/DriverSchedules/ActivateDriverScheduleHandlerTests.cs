using System.Text.Json;
using FluentAssertions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Jobs;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.DriverSchedules;
using VietRide.Trip.Domain.Entities;
using Route = VietRide.Trip.Domain.Entities.Route;

namespace VietRide.Trip.UnitTests.Features.DriverSchedules;

public sealed class ActivateDriverScheduleHandlerTests
{
    [Fact]
    public async Task Handle_InactiveSchedule_ActivatesAndEnqueuesAfterSaveChanges()
    {
        var fixture = ActivateFixture.Create(isActive: false);

        var result = await fixture.Handler.Handle(
            new ActivateDriverScheduleCommand(fixture.OperatorId, fixture.Schedule.Id),
            CancellationToken.None);

        result.IsActive.Should().BeTrue();
        fixture.Schedule.IsActive.Should().BeTrue();
        fixture.Schedules.CallCount(nameof(IDriverScheduleRepository.HasDriverConflictAsync)).Should().Be(1);
        fixture.Schedules.LastArguments(nameof(IDriverScheduleRepository.HasDriverConflictAsync))![5].Should().Be(fixture.Schedule.Id);
        fixture.UnitOfWork.SaveChangesCount.Should().Be(1);
        fixture.Scheduler.EnqueueCount.Should().Be(1);
        fixture.Scheduler.EnqueueObservedSaveChanges.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_AlreadyActiveSchedule_ReturnsDtoWithoutEnqueue()
    {
        var fixture = ActivateFixture.Create(isActive: true);

        var result = await fixture.Handler.Handle(
            new ActivateDriverScheduleCommand(fixture.OperatorId, fixture.Schedule.Id),
            CancellationToken.None);

        result.IsActive.Should().BeTrue();
        fixture.UnitOfWork.SaveChangesCount.Should().Be(0);
        fixture.Scheduler.EnqueueCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_ConflictingActiveSchedule_ThrowsTripDriverConflictAndDoesNotEnqueue()
    {
        var fixture = ActivateFixture.Create(isActive: false, hasConflict: true);

        var action = () => fixture.Handler.Handle(
            new ActivateDriverScheduleCommand(fixture.OperatorId, fixture.Schedule.Id),
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<ConflictException>();
        exception.Which.ErrorCode.Should().Be("TRIP_DRIVER_CONFLICT");
        fixture.Schedule.IsActive.Should().BeFalse();
        fixture.UnitOfWork.SaveChangesCount.Should().Be(0);
        fixture.Scheduler.EnqueueCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_MissingRouteDurationAndZeroRouteStopDurations_ThrowsValidationException()
    {
        var fixture = ActivateFixture.Create(isActive: false, routeDurationMinutes: null, routeStopDurations: [0, 0]);

        var action = () => fixture.Handler.Handle(
            new ActivateDriverScheduleCommand(fixture.OperatorId, fixture.Schedule.Id),
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<CodedValidationException>();
        exception.Which.ErrorCode.Should().Be("VALIDATION_ERROR");
        exception.Which.Errors.Should().ContainSingle(error => error.Field == "estimatedArrivalTime");
        fixture.Schedule.IsActive.Should().BeFalse();
        fixture.UnitOfWork.SaveChangesCount.Should().Be(0);
        fixture.Scheduler.EnqueueCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_AssistantWrongRole_ThrowsValidationErrorAndDoesNotActivateOrEnqueue()
    {
        var assistantUserId = Guid.NewGuid();
        var fixture = ActivateFixture.Create(
            isActive: false,
            assistantUserId: assistantUserId,
            assistantLookup: IdentityUserLookupResult.Success(assistantUserId, "DRIVER", Guid.NewGuid(), "ACTIVE"));

        var action = () => fixture.Handler.Handle(
            new ActivateDriverScheduleCommand(fixture.OperatorId, fixture.Schedule.Id),
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().ContainSingle(error => error.Field == "assistantUserId");
        fixture.Schedule.IsActive.Should().BeFalse();
        fixture.UnitOfWork.SaveChangesCount.Should().Be(0);
        fixture.Scheduler.EnqueueCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_AssistantNonActiveStatus_ActivatesBecauseStatusIsNotValidated()
    {
        var assistantUserId = Guid.NewGuid();
        var fixture = ActivateFixture.Create(
            isActive: false,
            assistantUserId: assistantUserId,
            assistantStatus: "LOCKED");

        var result = await fixture.Handler.Handle(
            new ActivateDriverScheduleCommand(fixture.OperatorId, fixture.Schedule.Id),
            CancellationToken.None);

        result.IsActive.Should().BeTrue();
        fixture.Schedule.IsActive.Should().BeTrue();
        fixture.UnitOfWork.SaveChangesCount.Should().Be(1);
        fixture.Scheduler.EnqueueCount.Should().Be(1);
    }

    private sealed class ActivateFixture
    {
        private ActivateFixture(
            Guid operatorId,
            DriverSchedule schedule,
            ActivateDriverScheduleHandler handler,
            StubDispatchProxy<IDriverScheduleRepository> schedules,
            TrackingTripGenerationJobScheduler scheduler,
            TrackingUnitOfWork unitOfWork)
        {
            OperatorId = operatorId;
            Schedule = schedule;
            Handler = handler;
            Schedules = schedules;
            Scheduler = scheduler;
            UnitOfWork = unitOfWork;
        }

        public Guid OperatorId { get; }

        public DriverSchedule Schedule { get; }

        public ActivateDriverScheduleHandler Handler { get; }

        public StubDispatchProxy<IDriverScheduleRepository> Schedules { get; }

        public TrackingTripGenerationJobScheduler Scheduler { get; }

        public TrackingUnitOfWork UnitOfWork { get; }

        public static ActivateFixture Create(
            bool isActive,
            bool hasConflict = false,
            int? routeDurationMinutes = 180,
            IReadOnlyCollection<int>? routeStopDurations = null,
            Guid? assistantUserId = null,
            IdentityUserLookupResult? driverLookup = null,
            IdentityUserLookupResult? assistantLookup = null,
            string? assistantStatus = null)
        {
            var operatorId = Guid.NewGuid();
            var route = Route.Create(
                operatorId,
                "Saigon to Can Tho",
                Guid.NewGuid(),
                Guid.NewGuid(),
                Money.FromRaw(250000),
                120m,
                routeDurationMinutes);
            var schedule = DriverSchedule.Create(
                operatorId,
                route.Id,
                Guid.NewGuid(),
                Guid.NewGuid(),
                assistantUserId,
                JsonSerializer.SerializeToElement(new[] { 2, 4 }),
                new TimeOnly(8, 0),
                new DateOnly(2026, 6, 15),
                new DateOnly(2026, 7, 31),
                isActive);

            var schedules = StubDispatchProxy<IDriverScheduleRepository>.Create();
            schedules.SetResult(nameof(IDriverScheduleRepository.GetByIdAsync), schedule);
            schedules.SetResult(nameof(IDriverScheduleRepository.HasDriverConflictAsync), hasConflict);
            var identity = StubDispatchProxy<IIdentityInternalClient>.Create();
            identity.SetResult(nameof(IIdentityInternalClient.ValidateOperatorCanWriteAsync), OperatorWriteEligibilityValidation.Allowed());
            identity.SetResult(nameof(IIdentityInternalClient.GetUserAsync), (Func<object?[]?, object?>)(args =>
            {
                var userId = (Guid)args![0]!;
                return userId == schedule.DriverUserId
                    ? driverLookup ?? IdentityUserLookupResult.Success(schedule.DriverUserId, "DRIVER", operatorId, "ACTIVE")
                    : assistantLookup ?? IdentityUserLookupResult.Success(userId, "ASSISTANT", operatorId, assistantStatus ?? "ACTIVE");
            }));
            var routes = StubDispatchProxy<IRouteRepository>.Create();
            routes.SetResult(nameof(IRouteRepository.QueryNoTracking), new[] { route }.AsQueryable());
            var routeStops = StubDispatchProxy<IRouteStopRepository>.Create();
            routeStops.SetResult(
                nameof(IRouteStopRepository.QueryNoTracking),
                (routeStopDurations ?? []).Select((duration, index) => RouteStop.Create(route.Id, Guid.NewGuid(), index + 1, duration, null)).AsQueryable());
            var unitOfWork = new TrackingUnitOfWork();
            var scheduler = new TrackingTripGenerationJobScheduler(unitOfWork);

            var handler = new ActivateDriverScheduleHandler(
                schedules.Object,
                identity.Object,
                routes.Object,
                routeStops.Object,
                scheduler,
                unitOfWork);

            return new ActivateFixture(operatorId, schedule, handler, schedules, scheduler, unitOfWork);
        }
    }

    private sealed class TrackingUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct)
        {
            SaveChangesCount++;
            return Task.FromResult(1);
        }

        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct)
        {
            return operation();
        }

        public Task BeginTransactionAsync(CancellationToken ct) => Task.CompletedTask;

        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;

        public Task RollbackAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class TrackingTripGenerationJobScheduler : ITripGenerationJobScheduler
    {
        private readonly TrackingUnitOfWork unitOfWork;

        public TrackingTripGenerationJobScheduler(TrackingUnitOfWork unitOfWork)
        {
            this.unitOfWork = unitOfWork;
        }

        public int EnqueueCount { get; private set; }

        public bool EnqueueObservedSaveChanges { get; private set; }

        public string EnqueueScheduleGeneration(Guid driverScheduleId)
        {
            EnqueueCount++;
            EnqueueObservedSaveChanges = unitOfWork.SaveChangesCount > 0;
            return "job-1";
        }
    }
}
