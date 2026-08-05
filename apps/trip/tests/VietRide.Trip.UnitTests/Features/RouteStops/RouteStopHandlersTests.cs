using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Api.Controllers;
using VietRide.Trip.Api.Controllers.Requests;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.RouteStops;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Features.RouteStops;

public sealed class RouteStopHandlersTests
{
    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherOperatorId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task AddRouteStop_AddsRouteStop_WhenRouteAndStopBelongToOperator()
    {
        var route = CreateRoute(OperatorId);
        route.SetPathGeometry("??BB");
        var stop = CreateStop(OperatorId);
        var routeStopRepository = new FakeRouteStopRepository([]);
        var handler = CreateAddHandler(new FakeRouteRepository([route]), routeStopRepository, new FakeStopRepository([stop]));

        var result = await handler.Handle(CreateAddCommand(route.Id, stop.Id), CancellationToken.None);

        result.RouteId.Should().Be(route.Id);
        result.StopId.Should().Be(stop.Id);
        result.OrderIndex.Should().Be(1);
        result.AllowPickup.Should().BeTrue();
        result.AllowDropoff.Should().BeFalse();
        routeStopRepository.Entities.Should().ContainSingle();
        route.PathPolyline.Should().BeNull();
    }

    [Fact]
    public async Task AddRouteStop_ThrowsCodedValidation_WhenPickupAndDropoffAreBothFalse()
    {
        var route = CreateRoute(OperatorId);
        var stop = CreateStop(OperatorId);
        var handler = CreateAddHandler(new FakeRouteRepository([route]), new FakeRouteStopRepository([]), new FakeStopRepository([stop]));

        var act = () => handler.Handle(CreateAddCommand(route.Id, stop.Id, allowPickup: false, allowDropoff: false), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedValidationException>();
        exception.Which.ErrorCode.Should().Be("ROUTE_STOP_FLAGS_INVALID");
        exception.Which.Errors.Should().Contain(error => error.Field == "allowPickup");
    }

    [Fact]
    public async Task AddRouteStop_ThrowsCodedValidation_WhenOrderIndexConflictsOnSameRoute()
    {
        var route = CreateRoute(OperatorId);
        var existingStop = CreateStop(OperatorId);
        var newStop = CreateStop(OperatorId);
        var existingRouteStop = RouteStop.Create(route.Id, existingStop.Id, 2, 30, 10m, true, true);
        var handler = CreateAddHandler(
            new FakeRouteRepository([route]),
            new FakeRouteStopRepository([existingRouteStop]),
            new FakeStopRepository([existingStop, newStop]));

        var act = () => handler.Handle(CreateAddCommand(route.Id, newStop.Id, orderIndex: 2), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedValidationException>();
        exception.Which.ErrorCode.Should().Be("ROUTE_STOP_ORDER_INVALID");
        exception.Which.Errors.Should().Contain(error => error.Field == "orderIndex");
    }

    [Fact]
    public async Task AddRouteStop_ThrowsValidation_WhenStopAlreadyConfiguredOnRoute()
    {
        var route = CreateRoute(OperatorId);
        var stop = CreateStop(OperatorId);
        var existingRouteStop = RouteStop.Create(route.Id, stop.Id, 1, 20, null, true, true);
        var handler = CreateAddHandler(
            new FakeRouteRepository([route]),
            new FakeRouteStopRepository([existingRouteStop]),
            new FakeStopRepository([stop]));

        var act = () => handler.Handle(CreateAddCommand(route.Id, stop.Id), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedValidationException>();
        exception.Which.ErrorCode.Should().Be("ROUTE_STOP_DUPLICATED");
        exception.Which.Errors.Should().Contain(error => error.Field == "stopId");
    }

    [Fact]
    public async Task AddRouteStop_ThrowsStopNotFound_WhenStopBelongsToAnotherOperator()
    {
        var route = CreateRoute(OperatorId);
        var otherStop = CreateStop(OtherOperatorId);
        var handler = CreateAddHandler(new FakeRouteRepository([route]), new FakeRouteStopRepository([]), new FakeStopRepository([otherStop]));

        var act = () => handler.Handle(CreateAddCommand(route.Id, otherStop.Id), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedNotFoundException>();
        exception.Which.ErrorCode.Should().Be("STOP_NOT_FOUND");
    }

    [Fact]
    public async Task AddRouteStop_ThrowsRouteNotFound_ForCrossOperatorRoute()
    {
        var otherRoute = CreateRoute(OtherOperatorId);
        var stop = CreateStop(OperatorId);
        var handler = CreateAddHandler(new FakeRouteRepository([otherRoute]), new FakeRouteStopRepository([]), new FakeStopRepository([stop]));

        var act = () => handler.Handle(CreateAddCommand(otherRoute.Id, stop.Id), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedNotFoundException>();
        exception.Which.ErrorCode.Should().Be("ROUTE_NOT_FOUND");
    }

    [Fact]
    public async Task RemoveRouteStop_HardRemovesJunctionRow()
    {
        var route = CreateRoute(OperatorId);
        route.SetPathGeometry("??BB");
        var stop = CreateStop(OperatorId);
        var routeStop = RouteStop.Create(route.Id, stop.Id, 1, 20, null, true, true);
        var routeStopRepository = new FakeRouteStopRepository([routeStop]);
        var handler = CreateRemoveHandler(new FakeRouteRepository([route]), routeStopRepository);

        await handler.Handle(new RemoveRouteStopCommand(OperatorId, route.Id, stop.Id), CancellationToken.None);

        routeStopRepository.Entities.Should().BeEmpty();
        route.PathPolyline.Should().BeNull();
    }

    [Fact]
    public async Task OperatorRoutesController_DispatchesRouteStopCommandsAndUsesAdminRole()
    {
        var routeStop = new RouteStopDto(Guid.NewGuid(), Guid.NewGuid(), 1, 20, null, true, true, default, default);
        var mediator = new CapturingMediator(routeStop);
        var controller = CreateController(mediator);
        var request = new AddRouteStopRequest(routeStop.StopId, 1, 20, null, true, false);

        var response = await controller.AddStopAsync(routeStop.RouteId, request, CancellationToken.None);

        response.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status201Created);
        mediator.LastRequest.Should().BeOfType<AddRouteStopCommand>()
            .Which.OperatorId.Should().Be(OperatorId);
        typeof(OperatorRoutesController).GetMethod(nameof(OperatorRoutesController.AddStopAsync))!
            .GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("OPERATOR_ADMIN");
        typeof(OperatorRoutesController).GetMethod(nameof(OperatorRoutesController.RemoveStopAsync))!
            .GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("OPERATOR_ADMIN");
    }

    [Fact]
    public async Task OperatorRoutesController_RemoveStop_ReturnsOkDeletedPayload()
    {
        var mediator = new CapturingMediator(Unit.Value);
        var controller = CreateController(mediator);
        var routeId = Guid.NewGuid();
        var stopId = Guid.NewGuid();

        var response = await controller.RemoveStopAsync(routeId, stopId, CancellationToken.None);

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(new Dictionary<string, bool> { ["deleted"] = true });
        mediator.LastRequest.Should().BeOfType<RemoveRouteStopCommand>()
            .Which.Should().Match<RemoveRouteStopCommand>(command =>
                command.OperatorId == OperatorId && command.RouteId == routeId && command.StopId == stopId);
    }

    private static AddRouteStopCommand CreateAddCommand(
        Guid routeId,
        Guid stopId,
        int orderIndex = 1,
        bool allowPickup = true,
        bool allowDropoff = false)
        => new(OperatorId, routeId, stopId, orderIndex, 20, 5m, allowPickup, allowDropoff);

    private static AddRouteStopHandler CreateAddHandler(
        FakeRouteRepository routeRepository,
        FakeRouteStopRepository routeStopRepository,
        FakeStopRepository stopRepository,
        OperatorWriteEligibilityValidation? eligibility = null)
        => new(
            new FakeIdentityInternalClient(eligibility ?? OperatorWriteEligibilityValidation.Allowed()),
            routeRepository,
            routeStopRepository,
            stopRepository,
            new FakeUnitOfWork());

    private static RemoveRouteStopHandler CreateRemoveHandler(
        FakeRouteRepository routeRepository,
        FakeRouteStopRepository routeStopRepository,
        OperatorWriteEligibilityValidation? eligibility = null)
        => new(
            new FakeIdentityInternalClient(eligibility ?? OperatorWriteEligibilityValidation.Allowed()),
            routeRepository,
            routeStopRepository,
            new FakeUnitOfWork());

    private static Route CreateRoute(Guid operatorId)
        => Route.Create(operatorId, "Da Nang to Hue", Guid.NewGuid(), Guid.NewGuid(), Money.FromRaw(250000), 100m, 180);

    private static Stop CreateStop(Guid operatorId)
        => Stop.Create(operatorId, $"Stop {Guid.NewGuid():N}", 16.0678m, 108.2208m, address: "Da Nang");

    private static OperatorRoutesController CreateController(IMediator mediator)
    {
        var controller = new OperatorRoutesController(mediator);
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

    private sealed class FakeRouteRepository : IRouteRepository
    {
        private readonly List<Route> routes;

        public FakeRouteRepository(IReadOnlyCollection<Route> routes)
        {
            this.routes = routes.ToList();
        }

        public Task<Route> AddAsync(Route entity, CancellationToken ct)
        {
            routes.Add(entity);
            return Task.FromResult(entity);
        }

        public Task<Route?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(routes.FirstOrDefault(route => route.Id == id));

        public Task<Route?> GetOwnedByIdAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken)
            => Task.FromResult(routes.FirstOrDefault(route => route.Id == routeId && route.OperatorId == operatorId && route.DeletedAt == null));

        public Task<Route?> GetOwnedActiveByIdAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken)
            => Task.FromResult(routes.FirstOrDefault(route => route.Id == routeId && route.OperatorId == operatorId && route.IsActive && route.DeletedAt == null));

        public Task<IReadOnlyList<Route>> ListByOperatorAsync(Guid operatorId, string? search, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Route>>(routes.Where(route => route.OperatorId == operatorId).ToList());

        public Task<bool> ExistsActiveOwnedByOperatorAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken)
            => Task.FromResult(routes.Any(route => route.Id == routeId && route.OperatorId == operatorId && route.IsActive && route.DeletedAt == null));

        public IQueryable<Route> Query() => routes.AsQueryable();

        public IQueryable<Route> QueryNoTracking() => routes.AsQueryable();

        public void Remove(Route entity) => routes.Remove(entity);

        public void Update(Route entity) { }
    }

    private sealed class FakeRouteStopRepository : IRouteStopRepository
    {
        public FakeRouteStopRepository(IReadOnlyCollection<RouteStop> routeStops)
        {
            Entities = routeStops.ToList();
        }

        public List<RouteStop> Entities { get; }

        public Task<RouteStop> AddAsync(RouteStop entity, CancellationToken ct)
        {
            Entities.Add(entity);
            return Task.FromResult(entity);
        }

        public Task<RouteStop?> GetByIdAsync((Guid RouteId, Guid StopId) id, CancellationToken ct)
            => GetByRouteAndStopAsync(id.RouteId, id.StopId, ct);

        public Task<RouteStop?> GetByRouteAndStopAsync(Guid routeId, Guid stopId, CancellationToken cancellationToken)
            => Task.FromResult(Entities.FirstOrDefault(routeStop => routeStop.RouteId == routeId && routeStop.StopId == stopId));

        public Task<bool> ExistsByRouteAndOrderIndexAsync(Guid routeId, int orderIndex, CancellationToken cancellationToken)
            => Task.FromResult(Entities.Any(routeStop => routeStop.RouteId == routeId && routeStop.OrderIndex == orderIndex));

        public IQueryable<RouteStop> Query() => Entities.AsQueryable();

        public IQueryable<RouteStop> QueryNoTracking() => Entities.AsQueryable();

        public void Remove(RouteStop entity) => Entities.Remove(entity);

        public void Update(RouteStop entity) { }
    }

    private sealed class FakeStopRepository : IStopRepository
    {
        private readonly List<Stop> stops;

        public FakeStopRepository(IReadOnlyCollection<Stop> stops)
        {
            this.stops = stops.ToList();
        }

        public Task<Stop> AddAsync(Stop entity, CancellationToken ct)
        {
            stops.Add(entity);
            return Task.FromResult(entity);
        }

        public Task<Stop?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(stops.FirstOrDefault(stop => stop.Id == id));

        public IQueryable<Stop> Query() => stops.AsQueryable();

        public IQueryable<Stop> QueryNoTracking() => stops.AsQueryable();

        public void Remove(Stop entity) => stops.Remove(entity);

        public void Update(Stop entity) { }
    }

    private sealed class FakeIdentityInternalClient : IIdentityInternalClient
    {
        private readonly OperatorWriteEligibilityValidation eligibility;

        public FakeIdentityInternalClient(OperatorWriteEligibilityValidation eligibility)
        {
            this.eligibility = eligibility;
        }

        public Task<OperatorWriteEligibilityValidation> ValidateOperatorCanWriteAsync(Guid operatorId, CancellationToken cancellationToken = default)
            => Task.FromResult(eligibility);

        public Task<IdentityUserLookupResult> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(IdentityUserLookupResult.ValidationFailure("Identity user lookup is not configured for this test."));
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task BeginTransactionAsync(CancellationToken ct) => Task.CompletedTask;

        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;

        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct) => operation();

        public Task RollbackAsync(CancellationToken ct) => Task.CompletedTask;

        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(1);
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
