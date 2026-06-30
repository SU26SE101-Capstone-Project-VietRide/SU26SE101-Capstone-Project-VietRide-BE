using FluentAssertions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.DriverSchedules.GetMyDriverSchedule;
using VietRide.Trip.Domain.Entities;
using TripEntity = VietRide.Trip.Domain.Entities.Trip;

namespace VietRide.Trip.UnitTests.Features.DriverSchedules;

public sealed class GetMyDriverScheduleHandlerTests
{
    private static readonly DateTimeOffset FixedUtcNow = new(2026, 6, 30, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_DefaultWindow_ReturnsCallerAssignmentsWithinInclusiveIctDates()
    {
        var callerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var driverTrip = CreateTrip(
            callerId,
            null,
            new DateTimeOffset(2026, 6, 29, 17, 0, 0, TimeSpan.Zero));
        var assistantTrip = CreateTrip(
            otherUserId,
            callerId,
            new DateTimeOffset(2026, 7, 14, 16, 59, 59, TimeSpan.Zero));
        var outsideWindowTrip = CreateTrip(
            callerId,
            null,
            new DateTimeOffset(2026, 7, 14, 17, 0, 0, TimeSpan.Zero));
        var repository = StubDispatchProxy<ITripRepository>.Create();
        repository.SetResult(
            nameof(ITripRepository.QueryNoTracking),
            new[] { outsideWindowTrip, assistantTrip, driverTrip }.AsQueryable());
        var clock = StubDispatchProxy<IClock>.Create();
        clock.SetResult("get_UtcNow", FixedUtcNow);
        var handler = new GetMyDriverScheduleHandler(clock.Object, repository.Object);

        var result = await handler.Handle(
            new GetMyDriverScheduleQuery(callerId, null, null),
            CancellationToken.None);

        result.From.Should().Be(new DateOnly(2026, 6, 30));
        result.To.Should().Be(new DateOnly(2026, 7, 14));
        result.Trips.Select(trip => trip.TripId)
            .Should().Equal(driverTrip.Id, assistantTrip.Id);
        result.Trips.Select(trip => trip.AssignmentRole)
            .Should().Equal("DRIVER", "ASSISTANT");
    }

    [Fact]
    public async Task Handle_DifferentCaller_ReturnsOnlyThatCallersAssignments()
    {
        var callerId = Guid.NewGuid();
        var differentUserId = Guid.NewGuid();
        var callersTrip = CreateTrip(
            callerId,
            null,
            new DateTimeOffset(2026, 7, 1, 1, 0, 0, TimeSpan.Zero));
        var differentUsersTrip = CreateTrip(
            differentUserId,
            null,
            new DateTimeOffset(2026, 7, 1, 2, 0, 0, TimeSpan.Zero));
        var repository = StubDispatchProxy<ITripRepository>.Create();
        repository.SetResult(
            nameof(ITripRepository.QueryNoTracking),
            new[] { callersTrip, differentUsersTrip }.AsQueryable());
        var clock = StubDispatchProxy<IClock>.Create();
        clock.SetResult("get_UtcNow", FixedUtcNow);
        var handler = new GetMyDriverScheduleHandler(clock.Object, repository.Object);

        var result = await handler.Handle(
            new GetMyDriverScheduleQuery(
                callerId,
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 1)),
            CancellationToken.None);

        result.Trips.Should().ContainSingle()
            .Which.TripId.Should().Be(callersTrip.Id);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Validate_ExactlyOneDateBound_ReturnsValidationError(bool hasFrom, bool hasTo)
    {
        var date = new DateOnly(2026, 6, 30);
        var query = new GetMyDriverScheduleQuery(
            Guid.NewGuid(),
            hasFrom ? date : null,
            hasTo ? date : null);

        var result = await new GetMyDriverScheduleValidator().ValidateAsync(query);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == "from");
    }

    private static TripEntity CreateTrip(
        Guid driverUserId,
        Guid? assistantUserId,
        DateTimeOffset departureDateTime)
    {
        return TripEntity.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            driverUserId,
            assistantUserId,
            null,
            departureDateTime,
            departureDateTime.AddHours(3),
            TripSource.MANUAL,
            Money.FromRaw(250000),
            500m,
            0m);
    }
}
