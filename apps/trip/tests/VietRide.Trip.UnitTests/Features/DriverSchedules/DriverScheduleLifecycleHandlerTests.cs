using System.Text.Json;
using FluentAssertions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.DriverSchedules;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Features.DriverSchedules;

public sealed class DriverScheduleLifecycleHandlerTests
{
    [Fact]
    public async Task Deactivate_IsBehaviorIdempotent()
    {
        var fixture = CreateFixture(isActive: true, hasTrip: false);
        var handler = new DeactivateDriverScheduleHandler(fixture.Schedules.Object, fixture.UnitOfWork);

        var first = await handler.Handle(
            new DeactivateDriverScheduleCommand(fixture.OperatorId, fixture.Schedule.Id),
            CancellationToken.None);
        var second = await handler.Handle(
            new DeactivateDriverScheduleCommand(fixture.OperatorId, fixture.Schedule.Id),
            CancellationToken.None);

        first.IsActive.Should().BeFalse();
        second.IsActive.Should().BeFalse();
        fixture.UnitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task Delete_SoftDeletesSchedule_WhenNoTripWasGenerated()
    {
        var fixture = CreateFixture(isActive: true, hasTrip: false);
        var deletedAt = new DateTimeOffset(2026, 8, 6, 2, 0, 0, TimeSpan.Zero);
        var handler = new DeleteDriverScheduleHandler(
            fixture.Schedules.Object,
            fixture.Trips.Object,
            fixture.UnitOfWork,
            new FixedClock(deletedAt));

        var result = await handler.Handle(
            new DeleteDriverScheduleCommand(fixture.OperatorId, fixture.Schedule.Id),
            CancellationToken.None);

        result.Should().Contain("deleted", true);
        fixture.Schedule.DeletedAt.Should().Be(deletedAt);
        fixture.Schedule.IsActive.Should().BeFalse();
        fixture.UnitOfWork.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task Delete_ReturnsScheduleHasTripsWithTripCount_WhenTripExists()
    {
        var fixture = CreateFixture(isActive: true, hasTrip: true);
        var handler = new DeleteDriverScheduleHandler(
            fixture.Schedules.Object,
            fixture.Trips.Object,
            fixture.UnitOfWork,
            new FixedClock(DateTimeOffset.UtcNow));

        var action = () => handler.Handle(
            new DeleteDriverScheduleCommand(fixture.OperatorId, fixture.Schedule.Id),
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<CodedConflictException>();
        exception.Which.ErrorCode.Should().Be("SCHEDULE_HAS_TRIPS");
        exception.Which.Errors.Should().ContainSingle(error => error.Field == "tripCount" && error.Message == "1");
        fixture.Schedule.DeletedAt.Should().BeNull();
        fixture.UnitOfWork.SaveChangesCount.Should().Be(0);
    }

    private static Fixture CreateFixture(bool isActive, bool hasTrip)
    {
        var operatorId = Guid.NewGuid();
        var schedule = DriverSchedule.Create(
            operatorId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            JsonSerializer.SerializeToElement(new[] { 1, 3, 5 }),
            new TimeOnly(8, 0),
            new DateOnly(2026, 8, 6),
            null,
            isActive);
        var schedules = StubDispatchProxy<IDriverScheduleRepository>.Create();
        schedules.SetResult(nameof(IDriverScheduleRepository.GetByIdAsync), schedule);
        var trips = StubDispatchProxy<ITripRepository>.Create();
        var tripRows = hasTrip
            ? new[]
            {
                VietRide.Trip.Domain.Entities.Trip.Create(
                    operatorId,
                    schedule.RouteId,
                    schedule.VehicleId!.Value,
                    schedule.DriverUserId,
                    null,
                    schedule.Id,
                    new DateTimeOffset(2026, 8, 7, 1, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 8, 7, 4, 0, 0, TimeSpan.Zero),
                    TripSource.AUTO_FROM_SCHEDULE,
                    Money.FromRaw(100_000),
                    null,
                    0m),
            }
            : [];
        trips.SetResult(nameof(ITripRepository.QueryNoTracking), tripRows.AsQueryable());
        return new Fixture(operatorId, schedule, schedules, trips, new TrackingUnitOfWork());
    }

    private sealed record Fixture(
        Guid OperatorId,
        DriverSchedule Schedule,
        StubDispatchProxy<IDriverScheduleRepository> Schedules,
        StubDispatchProxy<ITripRepository> Trips,
        TrackingUnitOfWork UnitOfWork);

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class TrackingUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct)
        {
            SaveChangesCount++;
            return Task.FromResult(1);
        }

        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct) => operation();
        public Task BeginTransactionAsync(CancellationToken ct) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
