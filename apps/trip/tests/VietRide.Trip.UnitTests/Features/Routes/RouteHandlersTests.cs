using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Application.Behaviors;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Api.Controllers;
using VietRide.Trip.Api.Controllers.Requests;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Routes;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Features.Routes;

public sealed class RouteHandlersTests
{
    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherOperatorId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task CreateRoute_CreatesOperatorScopedRoute_WhenStationsAreLinkedAndOperatorIsEligible()
    {
        var origin = CreateStation("Origin", "origin");
        var destination = CreateStation("Destination", "destination");
        var returnRoute = Route.Create(OperatorId, "Return", destination.Id, origin.Id, VietRide.Shared.Kernel.ValueObjects.Money.FromRaw(100000), 10m, 30);
        var routeRepository = new FakeRouteRepository([returnRoute]);
        var handler = CreateHandler(
            routeRepository,
            new FakeStationRepository([origin, destination]),
            new FakeOperatorStationRepository([
                OperatorStation.Create(OperatorId, origin.Id),
                OperatorStation.Create(OperatorId, destination.Id)]));

        var result = await handler.Handle(CreateCommand(origin.Id, destination.Id, returnRoute.Id), CancellationToken.None);

        result.OperatorId.Should().Be(OperatorId);
        result.OriginStationId.Should().Be(origin.Id);
        result.DestinationStationId.Should().Be(destination.Id);
        result.ReturnRouteId.Should().Be(returnRoute.Id);
        result.BaseFare.Should().Be(250000);
        routeRepository.Entities.Should().Contain(route => route.Id == result.Id);
        returnRoute.ReturnRouteId.Should().BeNull("returnRouteId is one-way and must not mutate the target route");
    }

    [Fact]
    public async Task CreateRoute_ThrowsStationNotFound_BeforeOriginDestinationEqualityCheck()
    {
        var stationId = Guid.NewGuid();
        var handler = CreateHandler(
            new FakeRouteRepository([]),
            new FakeStationRepository([]),
            new FakeOperatorStationRepository([]));

        var act = () => handler.Handle(CreateCommand(stationId, stationId), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedNotFoundException>();
        exception.Which.ErrorCode.Should().Be("STATION_NOT_FOUND");
    }

    [Fact]
    public async Task CreateRoute_ThrowsValidationErrorWithStationFields_WhenOperatorStationLinksAreMissing()
    {
        var origin = CreateStation("Origin", "origin");
        var destination = CreateStation("Destination", "destination");
        var handler = CreateHandler(
            new FakeRouteRepository([]),
            new FakeStationRepository([origin, destination]),
            new FakeOperatorStationRepository([]));

        var act = () => handler.Handle(CreateCommand(origin.Id, destination.Id), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Select(error => error.Field).Should().BeEquivalentTo([
            nameof(CreateRouteCommand.OriginStationId),
            nameof(CreateRouteCommand.DestinationStationId)]);
    }

    [Fact]
    public async Task CreateRoute_ThrowsValidationError_WhenOriginEqualsDestinationAfterStationAndLinkChecks()
    {
        var station = CreateStation("Origin", "origin");
        var handler = CreateHandler(
            new FakeRouteRepository([]),
            new FakeStationRepository([station]),
            new FakeOperatorStationRepository([OperatorStation.Create(OperatorId, station.Id)]));

        var act = () => handler.Handle(CreateCommand(station.Id, station.Id), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().Contain(error => error.Field == nameof(CreateRouteCommand.DestinationStationId));
    }

    [Fact]
    public async Task CreateRoute_ThrowsRouteNotFound_WhenReturnRouteBelongsToAnotherOperator()
    {
        var origin = CreateStation("Origin", "origin");
        var destination = CreateStation("Destination", "destination");
        var otherReturnRoute = Route.Create(OtherOperatorId, "Other Return", destination.Id, origin.Id, VietRide.Shared.Kernel.ValueObjects.Money.FromRaw(100000), null, null);
        var handler = CreateHandler(
            new FakeRouteRepository([otherReturnRoute]),
            new FakeStationRepository([origin, destination]),
            new FakeOperatorStationRepository([
                OperatorStation.Create(OperatorId, origin.Id),
                OperatorStation.Create(OperatorId, destination.Id)]));

        var act = () => handler.Handle(CreateCommand(origin.Id, destination.Id, otherReturnRoute.Id), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedNotFoundException>();
        exception.Which.ErrorCode.Should().Be("ROUTE_NOT_FOUND");
    }

    [Fact]
    public async Task CreateRoute_ThrowsRouteNotFound_WhenReturnRouteDoesNotExist()
    {
        var origin = CreateStation("Origin", "origin");
        var destination = CreateStation("Destination", "destination");
        var handler = CreateHandler(
            new FakeRouteRepository([]),
            new FakeStationRepository([origin, destination]),
            new FakeOperatorStationRepository([
                OperatorStation.Create(OperatorId, origin.Id),
                OperatorStation.Create(OperatorId, destination.Id)]));

        var act = () => handler.Handle(CreateCommand(origin.Id, destination.Id, Guid.NewGuid()), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedNotFoundException>();
        exception.Which.ErrorCode.Should().Be("ROUTE_NOT_FOUND");
    }

    [Fact]
    public async Task CreateRoute_ThrowsForbiddenAndDoesNotCreate_WhenOperatorIsNotApproved()
    {
        var origin = CreateStation("Origin", "origin");
        var destination = CreateStation("Destination", "destination");
        var routeRepository = new FakeRouteRepository([]);
        var handler = CreateHandler(
            routeRepository,
            new FakeStationRepository([origin, destination]),
            new FakeOperatorStationRepository([
                OperatorStation.Create(OperatorId, origin.Id),
                OperatorStation.Create(OperatorId, destination.Id)]),
            OperatorWriteEligibilityValidation.Forbidden("Operator is not approved."));

        var act = () => handler.Handle(CreateCommand(origin.Id, destination.Id), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ForbiddenException>();
        exception.Which.ErrorCode.Should().Be("FORBIDDEN");
        routeRepository.Entities.Should().BeEmpty();
    }

    [Fact]
    public async Task ListRoutes_ReturnsOnlyCallerRoutesWithTenantScopedTotalAndSearch()
    {
        var route = CreateRoute(OperatorId, "Da Nang to Hue");
        var other = CreateRoute(OtherOperatorId, "Da Nang to Hue");
        var handler = new ListRoutesHandler(new FakeRouteRepository([route, other]));

        var result = await handler.Handle(new ListRoutesQuery(OperatorId, 1, 20, "Hue"), CancellationToken.None);

        result.Items.Should().ContainSingle(item => item.Id == route.Id);
        result.TotalItems.Should().Be(1);
    }

    [Fact]
    public async Task ListRoutesValidator_RejectsInvalidPagination()
    {
        var behavior = new ValidationBehavior<ListRoutesQuery, PagedResult<RouteDto>>([new ListRoutesValidator()]);
        var query = new ListRoutesQuery(OperatorId, 0, 0, null);

        var act = () => behavior.Handle(
            query,
            () => Task.FromResult(PagedResult<RouteDto>.Create([], 1, 20, 0)),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Select(error => error.Field).Should().Contain([
            nameof(ListRoutesQuery.Page),
            nameof(ListRoutesQuery.PageSize)]);
    }

    [Fact]
    public async Task GetRoute_ReturnsCallerRoute_WhenRouteExists()
    {
        var route = CreateRoute(OperatorId, "Da Nang to Hue", isActive: false);
        var handler = new GetRouteHandler(new FakeRouteRepository([route]));

        var result = await handler.Handle(new GetRouteQuery(OperatorId, route.Id), CancellationToken.None);

        result.Id.Should().Be(route.Id);
        result.OperatorId.Should().Be(OperatorId);
        result.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task GetRoute_ThrowsRouteNotFound_ForCrossOperatorRoute()
    {
        var route = CreateRoute(OtherOperatorId, "Da Nang to Hue");
        var handler = new GetRouteHandler(new FakeRouteRepository([route]));

        var act = () => handler.Handle(new GetRouteQuery(OperatorId, route.Id), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedNotFoundException>();
        exception.Which.ErrorCode.Should().Be("ROUTE_NOT_FOUND");
    }

    [Fact]
    public async Task UpdateRoute_UpdatesMutableFields_WhenCallerOwnsRouteAndOperatorIsEligible()
    {
        var route = CreateRoute(OperatorId, "Da Nang to Hue");
        var returnRoute = CreateRoute(OperatorId, "Hue to Da Nang");
        var handler = CreateUpdateHandler(new FakeRouteRepository([route, returnRoute]));

        var result = await handler.Handle(new UpdateRouteCommand(
            OperatorId,
            route.Id,
            "Da Nang to Hue Express",
            returnRoute.Id,
            260500,
            105.5m,
            180,
            false), CancellationToken.None);

        result.Name.Should().Be("Da Nang to Hue Express");
        result.ReturnRouteId.Should().Be(returnRoute.Id);
        result.BaseFare.Should().Be(260000);
        result.TotalDistanceKm.Should().Be(105.5m);
        result.EstimatedDurationMinutes.Should().Be(180);
        result.IsActive.Should().BeFalse();
        returnRoute.ReturnRouteId.Should().BeNull("returnRouteId is one-way and must not mutate the target route");
    }

    [Fact]
    public async Task UpdateRoute_ThrowsRouteNotFound_ForCrossOperatorRoute()
    {
        var route = CreateRoute(OtherOperatorId, "Da Nang to Hue");
        var handler = CreateUpdateHandler(new FakeRouteRepository([route]));

        var act = () => handler.Handle(new UpdateRouteCommand(OperatorId, route.Id, "New", null, null, null, null, null), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedNotFoundException>();
        exception.Which.ErrorCode.Should().Be("ROUTE_NOT_FOUND");
    }

    [Fact]
    public async Task UpdateRoute_ThrowsRouteNotFound_WhenReturnRouteDoesNotExist()
    {
        var route = CreateRoute(OperatorId, "Da Nang to Hue");
        var handler = CreateUpdateHandler(new FakeRouteRepository([route]));

        var act = () => handler.Handle(new UpdateRouteCommand(OperatorId, route.Id, null, Guid.NewGuid(), null, null, null, null), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedNotFoundException>();
        exception.Which.ErrorCode.Should().Be("ROUTE_NOT_FOUND");
    }

    [Fact]
    public async Task UpdateRoute_UpdatesInactiveOwnedRoute_WhenCallerOwnsRoute()
    {
        var route = CreateRoute(OperatorId, "Da Nang to Hue", isActive: false);
        var handler = CreateUpdateHandler(new FakeRouteRepository([route]));

        var result = await handler.Handle(new UpdateRouteCommand(
            OperatorId,
            route.Id,
            "Da Nang to Hue Express",
            null,
            null,
            null,
            null,
            true), CancellationToken.None);

        result.Name.Should().Be("Da Nang to Hue Express");
        result.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task OperatorRoutesController_UsesExpectedRolesAndDispatchesThroughMediator()
    {
        var routeId = Guid.NewGuid();
        var route = new RouteDto(routeId, OperatorId, "Da Nang to Hue", Guid.NewGuid(), Guid.NewGuid(), null, 250000, 100m, 180, true, default, default);
        var mediator = new CapturingMediator(route);
        var controller = CreateController(mediator);

        var response = await controller.GetByIdAsync(routeId, CancellationToken.None);

        response.Result.Should().BeOfType<OkObjectResult>();
        mediator.LastRequest.Should().BeOfType<GetRouteQuery>()
            .Which.OperatorId.Should().Be(OperatorId);
        typeof(OperatorRoutesController).GetMethod(nameof(OperatorRoutesController.GetByIdAsync))!
            .GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("OPERATOR_STAFF,OPERATOR_ADMIN");
        typeof(OperatorRoutesController).GetMethod(nameof(OperatorRoutesController.PostAsync))!
            .GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("OPERATOR_ADMIN");
    }

    private static CreateRouteHandler CreateHandler(
        FakeRouteRepository routeRepository,
        FakeStationRepository stationRepository,
        FakeOperatorStationRepository operatorStationRepository,
        OperatorWriteEligibilityValidation? eligibility = null)
        => new(
            new FakeIdentityInternalClient(eligibility ?? OperatorWriteEligibilityValidation.Allowed()),
            operatorStationRepository,
            routeRepository,
            stationRepository,
            new FakeUnitOfWork());

    private static UpdateRouteHandler CreateUpdateHandler(
        FakeRouteRepository routeRepository,
        OperatorWriteEligibilityValidation? eligibility = null)
        => new(
            new FakeIdentityInternalClient(eligibility ?? OperatorWriteEligibilityValidation.Allowed()),
            routeRepository,
            new FakeUnitOfWork());

    private static CreateRouteCommand CreateCommand(Guid originStationId, Guid destinationStationId, Guid? returnRouteId = null)
        => new(OperatorId, "Da Nang to Hue", originStationId, destinationStationId, returnRouteId, 250500, 100m, 180, true);

    private static Route CreateRoute(Guid operatorId, string name, bool isActive = true)
    {
        var route = Route.Create(operatorId, name, Guid.NewGuid(), Guid.NewGuid(), VietRide.Shared.Kernel.ValueObjects.Money.FromRaw(250000), 100m, 180);

        if (!isActive)
        {
            route.Deactivate();
        }

        return route;
    }

    private static Station CreateStation(string name, string slug)
        => Station.Create(name, slug, "Da Nang", "Da Nang");

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
        public FakeRouteRepository(IReadOnlyCollection<Route> routes)
        {
            Entities = routes.ToList();
        }

        public List<Route> Entities { get; }

        public Task<Route> AddAsync(Route entity, CancellationToken ct)
        {
            Entities.Add(entity);
            return Task.FromResult(entity);
        }

        public Task<Route?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(Entities.FirstOrDefault(route => route.Id == id));

        public Task<Route?> GetOwnedByIdAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken)
            => Task.FromResult(Entities.FirstOrDefault(route =>
                route.Id == routeId
                && route.OperatorId == operatorId
                && route.DeletedAt == null));

        public Task<Route?> GetOwnedActiveByIdAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken)
            => Task.FromResult(Entities.FirstOrDefault(route =>
                route.Id == routeId
                && route.OperatorId == operatorId
                && route.IsActive
                && route.DeletedAt == null));

        public Task<IReadOnlyList<Route>> ListByOperatorAsync(Guid operatorId, string? search, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Route>>(Entities.Where(route => route.OperatorId == operatorId).ToList());

        public Task<bool> ExistsActiveOwnedByOperatorAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken)
            => Task.FromResult(Entities.Any(route =>
                route.Id == routeId
                && route.OperatorId == operatorId
                && route.IsActive
                && route.DeletedAt == null));

        public IQueryable<Route> Query() => Entities.AsQueryable();

        public IQueryable<Route> QueryNoTracking() => Entities.AsQueryable();

        public void Remove(Route entity) => Entities.Remove(entity);

        public void Update(Route entity) { }
    }

    private sealed class FakeStationRepository : IStationRepository
    {
        private readonly List<Station> stations;

        public FakeStationRepository(IReadOnlyCollection<Station> stations)
        {
            this.stations = stations.ToList();
        }

        public Task<Station> AddAsync(Station entity, CancellationToken ct)
        {
            stations.Add(entity);
            return Task.FromResult(entity);
        }

        public Task<Station?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(stations.FirstOrDefault(station => station.Id == id));

        public IQueryable<Station> Query() => stations.AsQueryable();

        public IQueryable<Station> QueryNoTracking() => stations.AsQueryable();

        public void Remove(Station entity) => stations.Remove(entity);

        public Task<IReadOnlyList<Station>> SearchActiveByNameAsync(string q, string? city, string? province, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Station>>(stations);

        public void Update(Station entity) { }
    }

    private sealed class FakeOperatorStationRepository : IOperatorStationRepository
    {
        private readonly List<OperatorStation> operatorStations;

        public FakeOperatorStationRepository(IReadOnlyCollection<OperatorStation> operatorStations)
        {
            this.operatorStations = operatorStations.ToList();
        }

        public Task<OperatorStation> AddAsync(OperatorStation entity, CancellationToken ct)
        {
            operatorStations.Add(entity);
            return Task.FromResult(entity);
        }

        public Task<OperatorStation?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(operatorStations.FirstOrDefault(operatorStation => operatorStation.Id == id));

        public IQueryable<OperatorStation> Query() => operatorStations.AsQueryable();

        public IQueryable<OperatorStation> QueryNoTracking() => operatorStations.AsQueryable();

        public void Remove(OperatorStation entity) => operatorStations.Remove(entity);

        public void Update(OperatorStation entity) { }
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
