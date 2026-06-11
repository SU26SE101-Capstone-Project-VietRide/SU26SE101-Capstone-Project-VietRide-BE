using System.Security.Claims;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Api.Controllers;
using VietRide.Trip.Api.Controllers.Requests;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.DriverSchedules;
using VietRide.Trip.Domain.Entities;

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
        result.IsActive.Should().BeTrue();
        driverSchedules.CallCount(nameof(IDriverScheduleRepository.HasDriverConflictAsync)).Should().Be(1);
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

    [Fact]
    public async Task Handle_InactiveCommand_PersistsInactiveAndSkipsConflictCheck()
    {
        var command = CreateCommand(isActive: false);
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

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsActive.Should().BeFalse();
        driverSchedules.CallCount(nameof(IDriverScheduleRepository.HasDriverConflictAsync)).Should().Be(0);
        driverSchedules.CallCount(nameof(IDriverScheduleRepository.AddAsync)).Should().Be(1);
        var addedSchedule = driverSchedules.LastArguments(nameof(IDriverScheduleRepository.AddAsync))![0]
            .Should().BeOfType<DriverSchedule>().Subject;
        addedSchedule.IsActive.Should().BeFalse();
        unitOfWork.CallCount(nameof(IUnitOfWork.SaveChangesAsync)).Should().Be(1);
    }

    [Fact]
    public async Task OperatorDriverSchedulesController_MapsIsActiveIntoCreateCommand()
    {
        var response = new DriverScheduleDto(
            Guid.NewGuid(),
            OperatorId,
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            null,
            [1, 3, 5],
            new TimeOnly(8, 0),
            new DateOnly(2026, 6, 15),
            null,
            false,
            default,
            default);
        var mediator = new CapturingMediator(response);
        var controller = CreateController(mediator);
        var request = new CreateDriverScheduleRequest(
            response.RouteId,
            response.VehicleId,
            response.DriverUserId,
            response.AssistantUserId,
            response.DayOfWeek,
            response.DepartureTime,
            response.ValidFrom,
            response.ValidUntil,
            IsActive: false);

        var result = await controller.Create(request, CancellationToken.None);

        result.Result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status201Created);
        mediator.LastRequest.Should().BeOfType<CreateDriverScheduleCommand>()
            .Which.IsActive.Should().BeFalse();
    }

    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static OperatorDriverSchedulesController CreateController(IMediator mediator)
    {
        var controller = new OperatorDriverSchedulesController(mediator);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim("operatorId", OperatorId.ToString()),
                ], "TestAuth")),
            },
        };

        return controller;
    }

    private static CreateDriverScheduleCommand CreateCommand(bool isActive = true)
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
            new DateOnly(2026, 8, 31),
            isActive);
    }

    private sealed class CapturingMediator : IMediator
    {
        private readonly object response;

        public CapturingMediator(object response)
        {
            this.response = response;
        }

        public object? LastRequest { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult((TResponse)response);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult<object?>(response);
        }

        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => EmptyStream<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => EmptyStream<object?>();

        private static async IAsyncEnumerable<T> EmptyStream<T>()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
