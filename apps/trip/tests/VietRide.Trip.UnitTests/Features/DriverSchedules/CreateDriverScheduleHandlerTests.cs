using System.Security.Claims;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Api.Controllers;
using VietRide.Trip.Api.Controllers.Requests;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Jobs;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.DriverSchedules;
using VietRide.Trip.Domain.Entities;
using Route = VietRide.Trip.Domain.Entities.Route;

namespace VietRide.Trip.UnitTests.Features.DriverSchedules;

public sealed class CreateDriverScheduleHandlerTests
{
    [Fact]
    public async Task Handle_ValidCommand_PersistsScheduleAndReturnsDto()
    {
        var command = CreateCommand(baseFare: 400_000);
        var driverSchedules = StubDispatchProxy<IDriverScheduleRepository>.Create();
        var routes = StubDispatchProxy<IRouteRepository>.Create();
        var routeStops = StubDispatchProxy<IRouteStopRepository>.Create();
        var vehicles = StubDispatchProxy<IVehicleRepository>.Create();
        var identity = StubDispatchProxy<IIdentityInternalClient>.Create();
        var scheduler = StubDispatchProxy<ITripGenerationJobScheduler>.Create();
        var unitOfWork = StubDispatchProxy<IUnitOfWork>.Create();
        identity.SetResult(nameof(IIdentityInternalClient.ValidateOperatorCanWriteAsync), true);
        identity.SetResult(nameof(IIdentityInternalClient.GetUserAsync),
            IdentityUserLookupResult.Success(command.DriverUserId, "DRIVER", command.OperatorId, "ACTIVE"));
        routes.SetResult(nameof(IRouteRepository.ExistsActiveOwnedByOperatorAsync), true);
        ConfigureRouteDuration(command, routes, routeStops, routeDurationMinutes: 180, routeStopDurations: []);
        driverSchedules.SetResult(nameof(IDriverScheduleRepository.HasDriverConflictAsync), false);

        var handler = new CreateDriverScheduleHandler(
            driverSchedules.Object,
            identity.Object,
            routes.Object,
            routeStops.Object,
            vehicles.Object,
            scheduler.Object,
            unitOfWork.Object);

        var result = await handler.Handle(command, CancellationToken.None);

        result.OperatorId.Should().Be(command.OperatorId);
        result.RouteId.Should().Be(command.RouteId);
        result.DriverUserId.Should().Be(command.DriverUserId);
        result.DayOfWeek.Should().BeEquivalentTo(command.DayOfWeek);
        result.IsActive.Should().BeTrue();
        result.BaseFare.Should().Be(400_000);
        driverSchedules.CallCount(nameof(IDriverScheduleRepository.HasDriverConflictAsync)).Should().Be(1);
        driverSchedules.LastArguments(nameof(IDriverScheduleRepository.HasDriverConflictAsync))![5].Should().BeNull();
        driverSchedules.CallCount(nameof(IDriverScheduleRepository.AddAsync)).Should().Be(1);
        driverSchedules.LastArguments(nameof(IDriverScheduleRepository.AddAsync))![0]
            .Should().BeOfType<DriverSchedule>()
            .Which.BaseFare.Should().Be(Money.FromRaw(400_000));
        unitOfWork.CallCount(nameof(IUnitOfWork.SaveChangesAsync)).Should().Be(1);
        scheduler.CallCount(nameof(ITripGenerationJobScheduler.EnqueueScheduleGeneration)).Should().Be(1);
    }

    [Fact]
    public async Task Handle_ConflictingDriverSchedule_ThrowsTripDriverConflict()
    {
        var command = CreateCommand();
        var driverSchedules = StubDispatchProxy<IDriverScheduleRepository>.Create();
        var routes = StubDispatchProxy<IRouteRepository>.Create();
        var routeStops = StubDispatchProxy<IRouteStopRepository>.Create();
        var vehicles = StubDispatchProxy<IVehicleRepository>.Create();
        var identity = StubDispatchProxy<IIdentityInternalClient>.Create();
        var scheduler = StubDispatchProxy<ITripGenerationJobScheduler>.Create();
        var unitOfWork = StubDispatchProxy<IUnitOfWork>.Create();
        identity.SetResult(nameof(IIdentityInternalClient.ValidateOperatorCanWriteAsync), true);
        identity.SetResult(nameof(IIdentityInternalClient.GetUserAsync),
            IdentityUserLookupResult.Success(command.DriverUserId, "DRIVER", command.OperatorId, "ACTIVE"));
        routes.SetResult(nameof(IRouteRepository.ExistsActiveOwnedByOperatorAsync), true);
        driverSchedules.SetResult(nameof(IDriverScheduleRepository.HasDriverConflictAsync), true);

        var handler = new CreateDriverScheduleHandler(
            driverSchedules.Object,
            identity.Object,
            routes.Object,
            routeStops.Object,
            vehicles.Object,
            scheduler.Object,
            unitOfWork.Object);

        var action = () => handler.Handle(command, CancellationToken.None);

        var exception = await action.Should().ThrowAsync<ConflictException>();
        exception.Which.ErrorCode.Should().Be("TRIP_DRIVER_CONFLICT");
        driverSchedules.CallCount(nameof(IDriverScheduleRepository.AddAsync)).Should().Be(0);
        unitOfWork.CallCount(nameof(IUnitOfWork.SaveChangesAsync)).Should().Be(0);
        scheduler.CallCount(nameof(ITripGenerationJobScheduler.EnqueueScheduleGeneration)).Should().Be(0);
    }

    [Fact]
    public async Task Handle_DriverWrongRole_ThrowsValidationErrorAndDoesNotPersist()
    {
        var command = CreateCommand();
        var driverSchedules = StubDispatchProxy<IDriverScheduleRepository>.Create();
        var routes = StubDispatchProxy<IRouteRepository>.Create();
        var routeStops = StubDispatchProxy<IRouteStopRepository>.Create();
        var vehicles = StubDispatchProxy<IVehicleRepository>.Create();
        var identity = StubDispatchProxy<IIdentityInternalClient>.Create();
        var scheduler = StubDispatchProxy<ITripGenerationJobScheduler>.Create();
        var unitOfWork = StubDispatchProxy<IUnitOfWork>.Create();
        identity.SetResult(nameof(IIdentityInternalClient.ValidateOperatorCanWriteAsync), true);
        identity.SetResult(nameof(IIdentityInternalClient.GetUserAsync),
            IdentityUserLookupResult.Success(command.DriverUserId, "ASSISTANT", command.OperatorId, "ACTIVE"));
        routes.SetResult(nameof(IRouteRepository.ExistsActiveOwnedByOperatorAsync), true);

        var handler = new CreateDriverScheduleHandler(
            driverSchedules.Object,
            identity.Object,
            routes.Object,
            routeStops.Object,
            vehicles.Object,
            scheduler.Object,
            unitOfWork.Object);

        var action = () => handler.Handle(command, CancellationToken.None);

        var exception = await action.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().ContainSingle(error => error.Field == "driverUserId");
        driverSchedules.CallCount(nameof(IDriverScheduleRepository.AddAsync)).Should().Be(0);
        unitOfWork.CallCount(nameof(IUnitOfWork.SaveChangesAsync)).Should().Be(0);
        scheduler.CallCount(nameof(ITripGenerationJobScheduler.EnqueueScheduleGeneration)).Should().Be(0);
    }

    [Fact]
    public async Task Handle_DriverNonActiveStatus_PersistsScheduleBecauseStatusIsNotValidated()
    {
        var command = CreateCommand();
        var driverSchedules = StubDispatchProxy<IDriverScheduleRepository>.Create();
        var routes = StubDispatchProxy<IRouteRepository>.Create();
        var routeStops = StubDispatchProxy<IRouteStopRepository>.Create();
        var vehicles = StubDispatchProxy<IVehicleRepository>.Create();
        var identity = StubDispatchProxy<IIdentityInternalClient>.Create();
        var scheduler = StubDispatchProxy<ITripGenerationJobScheduler>.Create();
        var unitOfWork = StubDispatchProxy<IUnitOfWork>.Create();
        identity.SetResult(nameof(IIdentityInternalClient.ValidateOperatorCanWriteAsync), true);
        identity.SetResult(nameof(IIdentityInternalClient.GetUserAsync),
            IdentityUserLookupResult.Success(command.DriverUserId, "DRIVER", command.OperatorId, "LOCKED"));
        routes.SetResult(nameof(IRouteRepository.ExistsActiveOwnedByOperatorAsync), true);
        ConfigureRouteDuration(command, routes, routeStops, routeDurationMinutes: 180, routeStopDurations: []);
        driverSchedules.SetResult(nameof(IDriverScheduleRepository.HasDriverConflictAsync), false);

        var handler = new CreateDriverScheduleHandler(
            driverSchedules.Object,
            identity.Object,
            routes.Object,
            routeStops.Object,
            vehicles.Object,
            scheduler.Object,
            unitOfWork.Object);

        var result = await handler.Handle(command, CancellationToken.None);

        result.DriverUserId.Should().Be(command.DriverUserId);
        driverSchedules.CallCount(nameof(IDriverScheduleRepository.AddAsync)).Should().Be(1);
        unitOfWork.CallCount(nameof(IUnitOfWork.SaveChangesAsync)).Should().Be(1);
        scheduler.CallCount(nameof(ITripGenerationJobScheduler.EnqueueScheduleGeneration)).Should().Be(1);
    }

    [Fact]
    public async Task Handle_AssistantWrongOperator_ThrowsValidationErrorAndDoesNotPersist()
    {
        var assistantUserId = Guid.NewGuid();
        var command = CreateCommand(assistantUserId: assistantUserId);
        var driverSchedules = StubDispatchProxy<IDriverScheduleRepository>.Create();
        var routes = StubDispatchProxy<IRouteRepository>.Create();
        var routeStops = StubDispatchProxy<IRouteStopRepository>.Create();
        var vehicles = StubDispatchProxy<IVehicleRepository>.Create();
        var identity = StubDispatchProxy<IIdentityInternalClient>.Create();
        var scheduler = StubDispatchProxy<ITripGenerationJobScheduler>.Create();
        var unitOfWork = StubDispatchProxy<IUnitOfWork>.Create();
        identity.SetResult(nameof(IIdentityInternalClient.ValidateOperatorCanWriteAsync), true);
        identity.SetResult(nameof(IIdentityInternalClient.GetUserAsync), (Func<object?[]?, object?>)(args =>
        {
            var userId = (Guid)args![0]!;
            return userId == command.DriverUserId
                ? IdentityUserLookupResult.Success(command.DriverUserId, "DRIVER", command.OperatorId, "ACTIVE")
                : IdentityUserLookupResult.Success(assistantUserId, "ASSISTANT", Guid.NewGuid(), "ACTIVE");
        }));
        routes.SetResult(nameof(IRouteRepository.ExistsActiveOwnedByOperatorAsync), true);

        var handler = new CreateDriverScheduleHandler(
            driverSchedules.Object,
            identity.Object,
            routes.Object,
            routeStops.Object,
            vehicles.Object,
            scheduler.Object,
            unitOfWork.Object);

        var action = () => handler.Handle(command, CancellationToken.None);

        var exception = await action.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().ContainSingle(error => error.Field == "assistantUserId");
        driverSchedules.CallCount(nameof(IDriverScheduleRepository.AddAsync)).Should().Be(0);
        unitOfWork.CallCount(nameof(IUnitOfWork.SaveChangesAsync)).Should().Be(0);
        scheduler.CallCount(nameof(ITripGenerationJobScheduler.EnqueueScheduleGeneration)).Should().Be(0);
    }

    [Fact]
    public async Task Handle_InactiveCommand_PersistsInactiveAndSkipsConflictCheck()
    {
        var command = CreateCommand(isActive: false);
        var driverSchedules = StubDispatchProxy<IDriverScheduleRepository>.Create();
        var routes = StubDispatchProxy<IRouteRepository>.Create();
        var routeStops = StubDispatchProxy<IRouteStopRepository>.Create();
        var vehicles = StubDispatchProxy<IVehicleRepository>.Create();
        var identity = StubDispatchProxy<IIdentityInternalClient>.Create();
        var scheduler = StubDispatchProxy<ITripGenerationJobScheduler>.Create();
        var unitOfWork = StubDispatchProxy<IUnitOfWork>.Create();
        identity.SetResult(nameof(IIdentityInternalClient.ValidateOperatorCanWriteAsync), true);
        identity.SetResult(nameof(IIdentityInternalClient.GetUserAsync),
            IdentityUserLookupResult.Success(command.DriverUserId, "DRIVER", command.OperatorId, "ACTIVE"));
        routes.SetResult(nameof(IRouteRepository.ExistsActiveOwnedByOperatorAsync), true);
        driverSchedules.SetResult(nameof(IDriverScheduleRepository.HasDriverConflictAsync), true);

        var handler = new CreateDriverScheduleHandler(
            driverSchedules.Object,
            identity.Object,
            routes.Object,
            routeStops.Object,
            vehicles.Object,
            scheduler.Object,
            unitOfWork.Object);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsActive.Should().BeFalse();
        driverSchedules.CallCount(nameof(IDriverScheduleRepository.HasDriverConflictAsync)).Should().Be(0);
        driverSchedules.CallCount(nameof(IDriverScheduleRepository.AddAsync)).Should().Be(1);
        var addedSchedule = driverSchedules.LastArguments(nameof(IDriverScheduleRepository.AddAsync))![0]
            .Should().BeOfType<DriverSchedule>().Subject;
        addedSchedule.IsActive.Should().BeFalse();
        unitOfWork.CallCount(nameof(IUnitOfWork.SaveChangesAsync)).Should().Be(1);
        scheduler.CallCount(nameof(ITripGenerationJobScheduler.EnqueueScheduleGeneration)).Should().Be(0);
    }

    [Fact]
    public async Task Handle_ActiveCommandWithMissingRouteDuration_ThrowsValidationErrorAndDoesNotPersistOrEnqueue()
    {
        var command = CreateCommand();
        var driverSchedules = StubDispatchProxy<IDriverScheduleRepository>.Create();
        var routes = StubDispatchProxy<IRouteRepository>.Create();
        var routeStops = StubDispatchProxy<IRouteStopRepository>.Create();
        var vehicles = StubDispatchProxy<IVehicleRepository>.Create();
        var identity = StubDispatchProxy<IIdentityInternalClient>.Create();
        var scheduler = StubDispatchProxy<ITripGenerationJobScheduler>.Create();
        var unitOfWork = StubDispatchProxy<IUnitOfWork>.Create();
        identity.SetResult(nameof(IIdentityInternalClient.ValidateOperatorCanWriteAsync), true);
        identity.SetResult(nameof(IIdentityInternalClient.GetUserAsync),
            IdentityUserLookupResult.Success(command.DriverUserId, "DRIVER", command.OperatorId, "ACTIVE"));
        routes.SetResult(nameof(IRouteRepository.ExistsActiveOwnedByOperatorAsync), true);
        ConfigureRouteDuration(command, routes, routeStops, routeDurationMinutes: null, routeStopDurations: [0, 0]);
        driverSchedules.SetResult(nameof(IDriverScheduleRepository.HasDriverConflictAsync), false);

        var handler = new CreateDriverScheduleHandler(
            driverSchedules.Object,
            identity.Object,
            routes.Object,
            routeStops.Object,
            vehicles.Object,
            scheduler.Object,
            unitOfWork.Object);

        var action = () => handler.Handle(command, CancellationToken.None);

        var exception = await action.Should().ThrowAsync<CodedValidationException>();
        exception.Which.ErrorCode.Should().Be("VALIDATION_ERROR");
        exception.Which.Errors.Should().ContainSingle(error => error.Field == "estimatedArrivalTime");
        driverSchedules.CallCount(nameof(IDriverScheduleRepository.AddAsync)).Should().Be(0);
        unitOfWork.CallCount(nameof(IUnitOfWork.SaveChangesAsync)).Should().Be(0);
        scheduler.CallCount(nameof(ITripGenerationJobScheduler.EnqueueScheduleGeneration)).Should().Be(0);
    }

    [Fact]
    public async Task OperatorDriverSchedulesController_MapsIsActiveAndBaseFareIntoCreateCommand()
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
            IsActive: false,
            BaseFare: 400_000);

        var result = await controller.Create(request, CancellationToken.None);

        result.Result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status201Created);
        var command = mediator.LastRequest.Should().BeOfType<CreateDriverScheduleCommand>().Subject;
        command.IsActive.Should().BeFalse();
        command.BaseFare.Should().Be(400_000);
    }

    [Fact]
    public async Task OperatorDriverSchedulesController_ActivateMapsIdIntoActivateCommand()
    {
        var driverScheduleId = Guid.NewGuid();
        var response = new DriverScheduleDto(
            driverScheduleId,
            OperatorId,
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            null,
            [1, 3, 5],
            new TimeOnly(8, 0),
            new DateOnly(2026, 6, 15),
            null,
            true,
            default,
            default);
        var mediator = new CapturingMediator(response);
        var controller = CreateController(mediator);

        var result = await controller.Activate(driverScheduleId, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().Be(response);
        mediator.LastRequest.Should().BeOfType<ActivateDriverScheduleCommand>()
            .Which.DriverScheduleId.Should().Be(driverScheduleId);
        ((ActivateDriverScheduleCommand)mediator.LastRequest!).OperatorId.Should().Be(OperatorId);
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

    private static void ConfigureRouteDuration(
        CreateDriverScheduleCommand command,
        StubDispatchProxy<IRouteRepository> routes,
        StubDispatchProxy<IRouteStopRepository> routeStops,
        int? routeDurationMinutes,
        IReadOnlyCollection<int> routeStopDurations)
    {
        var route = Route.Create(
            command.OperatorId,
            "Saigon to Can Tho",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Money.FromRaw(250000),
            120m,
            routeDurationMinutes);
        typeof(Route).GetProperty(nameof(Route.Id))!.SetValue(route, command.RouteId);
        routes.SetResult(nameof(IRouteRepository.QueryNoTracking), new[] { route }.AsQueryable());
        routeStops.SetResult(
            nameof(IRouteStopRepository.QueryNoTracking),
            routeStopDurations.Select((duration, index) => RouteStop.Create(route.Id, Guid.NewGuid(), index + 1, duration, null)).AsQueryable());
    }

    private static CreateDriverScheduleCommand CreateCommand(
        bool isActive = true,
        Guid? assistantUserId = null,
        long? baseFare = null)
    {
        return new CreateDriverScheduleCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            assistantUserId,
            [1, 3, 5],
            new TimeOnly(8, 0),
            new DateOnly(2026, 6, 15),
            new DateOnly(2026, 8, 31),
            isActive,
            baseFare);
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
