using FluentAssertions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.DriverSchedules;

namespace VietRide.Trip.UnitTests.Features.DriverSchedules;

public sealed class CreateDriverScheduleHandlerTests
{
    [Fact]
    public async Task Handle_ValidCommand_PersistsScheduleAndReturnsDto()
    {
        var command = CreateCommand();
        var driverSchedules = StubDispatchProxy<IDriverScheduleRepository>.Create();
        var routes = StubDispatchProxy<IRouteRepository>.Create();
        var vehicles = StubDispatchProxy<IVehicleRepository>.Create();
        var identity = StubDispatchProxy<IIdentityInternalClient>.Create();
        var unitOfWork = StubDispatchProxy<IUnitOfWork>.Create();
        identity.SetResult(nameof(IIdentityInternalClient.ValidateOperatorCanWriteAsync), true);
        routes.SetResult(nameof(IRouteRepository.ExistsActiveOwnedByOperatorAsync), true);
        driverSchedules.SetResult(nameof(IDriverScheduleRepository.HasDriverConflictAsync), false);

        var handler = new CreateDriverScheduleHandler(
            driverSchedules.Object,
            identity.Object,
            routes.Object,
            vehicles.Object,
            unitOfWork.Object);

        var result = await handler.Handle(command, CancellationToken.None);

        result.OperatorId.Should().Be(command.OperatorId);
        result.RouteId.Should().Be(command.RouteId);
        result.DriverUserId.Should().Be(command.DriverUserId);
        result.DayOfWeek.Should().BeEquivalentTo(command.DayOfWeek);
        driverSchedules.CallCount(nameof(IDriverScheduleRepository.AddAsync)).Should().Be(1);
        unitOfWork.CallCount(nameof(IUnitOfWork.SaveChangesAsync)).Should().Be(1);
    }

    [Fact]
    public async Task Handle_ConflictingDriverSchedule_ThrowsTripDriverConflict()
    {
        var command = CreateCommand();
        var driverSchedules = StubDispatchProxy<IDriverScheduleRepository>.Create();
        var routes = StubDispatchProxy<IRouteRepository>.Create();
        var vehicles = StubDispatchProxy<IVehicleRepository>.Create();
        var identity = StubDispatchProxy<IIdentityInternalClient>.Create();
        var unitOfWork = StubDispatchProxy<IUnitOfWork>.Create();
        identity.SetResult(nameof(IIdentityInternalClient.ValidateOperatorCanWriteAsync), true);
        routes.SetResult(nameof(IRouteRepository.ExistsActiveOwnedByOperatorAsync), true);
        driverSchedules.SetResult(nameof(IDriverScheduleRepository.HasDriverConflictAsync), true);

        var handler = new CreateDriverScheduleHandler(
            driverSchedules.Object,
            identity.Object,
            routes.Object,
            vehicles.Object,
            unitOfWork.Object);

        var action = () => handler.Handle(command, CancellationToken.None);

        var exception = await action.Should().ThrowAsync<ConflictException>();
        exception.Which.ErrorCode.Should().Be("TRIP_DRIVER_CONFLICT");
        driverSchedules.CallCount(nameof(IDriverScheduleRepository.AddAsync)).Should().Be(0);
        unitOfWork.CallCount(nameof(IUnitOfWork.SaveChangesAsync)).Should().Be(0);
    }

    private static CreateDriverScheduleCommand CreateCommand()
    {
        return new CreateDriverScheduleCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            Guid.NewGuid(),
            [1, 3, 5],
            new TimeOnly(8, 0),
            new DateOnly(2026, 6, 15),
            new DateOnly(2026, 8, 31));
    }
}
