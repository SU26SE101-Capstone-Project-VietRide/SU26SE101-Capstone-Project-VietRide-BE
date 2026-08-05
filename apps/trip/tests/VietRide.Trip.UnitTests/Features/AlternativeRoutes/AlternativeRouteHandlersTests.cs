using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Api.Controllers;
using VietRide.Trip.Api.Controllers.Requests;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.AlternativeRoutes;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Features.AlternativeRoutes;

public sealed class AlternativeRouteHandlersTests
{
    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherOperatorId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task CreateAlternativeRoute_CreatesRouteWithIndependentStopSequence_WhenActiveLimitAllows()
    {
        var route = CreateRoute(OperatorId);
        var destination = CreateStation();
        var stop = CreateStop(OperatorId);
        var alternativeRouteRepository = new FakeAlternativeRouteRepository([]);
        var handler = CreateCreateHandler(
            alternativeRouteRepository,
            new FakeRouteRepository([route]),
            new FakeStationRepository([destination]),
            new FakeStopRepository([stop]));

        var result = await handler.Handle(CreateCommand(route.Id, destination.Id, stop.Id), CancellationToken.None);

        result.RouteId.Should().Be(route.Id);
        result.DestinationStationId.Should().Be(destination.Id);
        result.IsActive.Should().BeTrue();
        result.Stops.Should().ContainSingle().Which.StopId.Should().Be(stop.Id);
        alternativeRouteRepository.Entities.Should().ContainSingle();
        alternativeRouteRepository.Stops.Should().ContainSingle();
    }

    [Fact]
    public async Task CreateAlternativeRoute_AllowsMoreThanTwoActiveAlternatives()
    {
        var route = CreateRoute(OperatorId);
        var destination = CreateStation();
        var stop = CreateStop(OperatorId);
        var existingOne = AlternativeRoute.Create(route.Id, "Alt 1", destination.Id, null, null);
        var existingTwo = AlternativeRoute.Create(route.Id, "Alt 2", destination.Id, null, null);
        var repository = new FakeAlternativeRouteRepository([existingOne, existingTwo]);
        var handler = CreateCreateHandler(
            repository,
            new FakeRouteRepository([route]),
            new FakeStationRepository([destination]),
            new FakeStopRepository([stop]));

        await handler.Handle(CreateCommand(route.Id, destination.Id, stop.Id), CancellationToken.None);

        repository.Entities.Count(alternativeRoute => alternativeRoute.IsActive).Should().Be(3);
    }

    [Fact]
    public async Task CreateAlternativeRoute_AllowsNewAlternative_AfterOneExistingAlternativeIsDeactivated()
    {
        var route = CreateRoute(OperatorId);
        var destination = CreateStation();
        var stop = CreateStop(OperatorId);
        var active = AlternativeRoute.Create(route.Id, "Alt 1", destination.Id, null, null);
        var inactive = AlternativeRoute.Create(route.Id, "Alt 2", destination.Id, null, null);
        inactive.Deactivate();
        var alternativeRouteRepository = new FakeAlternativeRouteRepository([active, inactive]);
        var handler = CreateCreateHandler(
            alternativeRouteRepository,
            new FakeRouteRepository([route]),
            new FakeStationRepository([destination]),
            new FakeStopRepository([stop]));

        var result = await handler.Handle(CreateCommand(route.Id, destination.Id, stop.Id), CancellationToken.None);

        result.IsActive.Should().BeTrue();
        alternativeRouteRepository.Entities.Count(alternativeRoute => alternativeRoute.IsActive).Should().Be(2);
    }

    [Fact]
    public async Task CreateAlternativeRoute_ThrowsValidation_WhenStopOrderIndexIsDuplicatedInPayload()
    {
        var route = CreateRoute(OperatorId);
        var destination = CreateStation();
        var firstStop = CreateStop(OperatorId);
        var secondStop = CreateStop(OperatorId);
        var handler = CreateCreateHandler(
            new FakeAlternativeRouteRepository([]),
            new FakeRouteRepository([route]),
            new FakeStationRepository([destination]),
            new FakeStopRepository([firstStop, secondStop]));
        var command = new CreateAlternativeRouteCommand(
            OperatorId,
            route.Id,
            "Incident bypass",
            null,
            destination.Id,
            11m,
            20,
            [
                new AlternativeRouteStopInput(firstStop.Id, 1, 10, 3m),
                new AlternativeRouteStopInput(secondStop.Id, 1, 12, 4m),
            ]);

        var act = () => handler.Handle(command, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Should().NotBeOfType<CodedValidationException>();
        exception.Which.Errors.Should().Contain(error => error.Field == "orderIndex");
    }

    [Fact]
    public async Task UpdateAlternativeRoute_ThrowsRouteNotFound_ForCrossOperatorAlternativeRoute()
    {
        var destination = CreateStation();
        var stop = CreateStop(OperatorId);
        var otherRoute = CreateRoute(OtherOperatorId);
        var alternativeRoute = AlternativeRoute.Create(otherRoute.Id, "Other", destination.Id, null, null);
        var handler = CreateUpdateHandler(
            new FakeAlternativeRouteRepository([alternativeRoute], []),
            new FakeStationRepository([destination]),
            new FakeStopRepository([stop]));

        var act = () => handler.Handle(UpdateCommand(alternativeRoute.Id, destination.Id, stop.Id), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedNotFoundException>();
        exception.Which.ErrorCode.Should().Be("ROUTE_NOT_FOUND");
    }

    [Fact]
    public async Task UpdateAlternativeRoute_PreservesExistingValuesAndStops_WhenFieldsAreOmitted()
    {
        var route = CreateRoute(OperatorId);
        var destination = CreateStation();
        var existingStop = CreateStop(OperatorId);
        var alternativeRoute = AlternativeRoute.Create(route.Id, "Alt", destination.Id, 10m, 30, "Old description");
        var alternativeRouteRepository = new FakeAlternativeRouteRepository([alternativeRoute], [route]);
        alternativeRouteRepository.Stops.Add(AlternativeRouteStop.Create(alternativeRoute.Id, existingStop.Id, 1, 10, 3m));
        var handler = CreateUpdateHandler(
            alternativeRouteRepository,
            new FakeStationRepository([destination]),
            new FakeStopRepository([existingStop]));

        var result = await handler.Handle(new UpdateAlternativeRouteCommand(
            OperatorId,
            alternativeRoute.Id,
            null,
            false,
            "Updated description",
            true,
            null,
            false,
            null,
            false,
            null,
            false,
            false,
            false,
            null), CancellationToken.None);

        result.Name.Should().Be("Alt");
        result.Description.Should().Be("Updated description");
        result.DestinationStationId.Should().Be(destination.Id);
        result.TotalDistanceKm.Should().Be(10m);
        result.EstimatedDurationMinutes.Should().Be(30);
        result.IsActive.Should().BeFalse();
        result.Stops.Should().ContainSingle().Which.StopId.Should().Be(existingStop.Id);
    }

    [Fact]
    public void UpdateAlternativeRouteValidator_Fails_WhenStopsIsExplicitNull()
    {
        var validator = new UpdateAlternativeRouteValidator();
        var command = new UpdateAlternativeRouteCommand(
            OperatorId,
            Guid.NewGuid(),
            null,
            false,
            null,
            false,
            null,
            false,
            null,
            false,
            null,
            false,
            null,
            true,
            null);

        var result = validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(UpdateAlternativeRouteCommand.Stops));
    }

    [Fact]
    public async Task UpdateAlternativeRoute_ReplacesStopsOnlyWhenStopsAreSupplied()
    {
        var route = CreateRoute(OperatorId);
        var destination = CreateStation();
        var oldStop = CreateStop(OperatorId);
        var newStop = CreateStop(OperatorId);
        var alternativeRoute = AlternativeRoute.Create(route.Id, "Alt", destination.Id, null, null);
        alternativeRoute.SetPathGeometry("??BB");
        var alternativeRouteRepository = new FakeAlternativeRouteRepository([alternativeRoute], [route]);
        alternativeRouteRepository.Stops.Add(AlternativeRouteStop.Create(alternativeRoute.Id, oldStop.Id, 1, 10, 3m));
        var handler = CreateUpdateHandler(
            alternativeRouteRepository,
            new FakeStationRepository([destination]),
            new FakeStopRepository([oldStop, newStop]));

        var result = await handler.Handle(new UpdateAlternativeRouteCommand(
            OperatorId,
            alternativeRoute.Id,
            null,
            false,
            null,
            false,
            null,
            false,
            null,
            false,
            null,
            false,
            null,
            true,
            [new AlternativeRouteStopInput(newStop.Id, 2, 20, 6m)]), CancellationToken.None);

        result.Stops.Should().ContainSingle().Which.StopId.Should().Be(newStop.Id);
        alternativeRouteRepository.Stops.Should().ContainSingle().Which.StopId.Should().Be(newStop.Id);
        alternativeRoute.PathPolyline.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAlternativeRoute_ThrowsValidation_WhenSuppliedStopsHaveDuplicateOrderIndex()
    {
        var route = CreateRoute(OperatorId);
        var destination = CreateStation();
        var firstStop = CreateStop(OperatorId);
        var secondStop = CreateStop(OperatorId);
        var alternativeRoute = AlternativeRoute.Create(route.Id, "Alt", destination.Id, null, null);
        var handler = CreateUpdateHandler(
            new FakeAlternativeRouteRepository([alternativeRoute], [route]),
            new FakeStationRepository([destination]),
            new FakeStopRepository([firstStop, secondStop]));

        var act = () => handler.Handle(new UpdateAlternativeRouteCommand(
            OperatorId,
            alternativeRoute.Id,
            null,
            false,
            null,
            false,
            null,
            false,
            null,
            false,
            null,
            false,
            null,
            true,
            [
                new AlternativeRouteStopInput(firstStop.Id, 1, 10, 3m),
                new AlternativeRouteStopInput(secondStop.Id, 1, 20, 6m),
            ]), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().Contain(error => error.Field == "orderIndex");
    }

    [Fact]
    public async Task UpdateAlternativeRoute_AllowsReactivationWithMoreThanTwoActiveRoutes()
    {
        var route = CreateRoute(OperatorId);
        var destination = CreateStation();
        var inactiveAlternativeRoute = AlternativeRoute.Create(route.Id, "Inactive", destination.Id, null, null);
        inactiveAlternativeRoute.Deactivate();
        var activeOne = AlternativeRoute.Create(route.Id, "Alt 1", destination.Id, null, null);
        var activeTwo = AlternativeRoute.Create(route.Id, "Alt 2", destination.Id, null, null);
        var handler = CreateUpdateHandler(
            new FakeAlternativeRouteRepository([inactiveAlternativeRoute, activeOne, activeTwo], [route]),
            new FakeStationRepository([destination]),
            new FakeStopRepository([]));

        var result = await handler.Handle(new UpdateAlternativeRouteCommand(
            OperatorId,
            inactiveAlternativeRoute.Id,
            null,
            false,
            null,
            false,
            null,
            false,
            null,
            false,
            null,
            false,
            true,
            false,
            null), CancellationToken.None);

        result.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task DeactivateAlternativeRoute_DeactivatesButDoesNotRemoveEntity()
    {
        var route = CreateRoute(OperatorId);
        var destination = CreateStation();
        var alternativeRoute = AlternativeRoute.Create(route.Id, "Alt", destination.Id, null, null);
        var alternativeRouteRepository = new FakeAlternativeRouteRepository([alternativeRoute], [route]);
        var handler = new DeactivateAlternativeRouteHandler(
            alternativeRouteRepository,
            new FakeIdentityInternalClient(OperatorWriteEligibilityValidation.Allowed()),
            new FakeUnitOfWork());

        await handler.Handle(new DeactivateAlternativeRouteCommand(OperatorId, alternativeRoute.Id), CancellationToken.None);

        alternativeRoute.IsActive.Should().BeFalse();
        alternativeRouteRepository.Entities.Should().Contain(alternativeRoute);
    }

    [Fact]
    public async Task OperatorAlternativeRoutesController_Delete_ReturnsIsActiveFalseShape()
    {
        var mediator = new CapturingMediator(Unit.Value);
        var controller = CreateAlternativeRoutesController(mediator);

        var response = await controller.DeleteAsync(Guid.NewGuid(), CancellationToken.None);

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(new Dictionary<string, bool> { ["isActive"] = false });
        mediator.LastRequest.Should().BeOfType<DeactivateAlternativeRouteCommand>()
            .Which.OperatorId.Should().Be(OperatorId);
    }

    [Fact]
    public async Task OperatorAlternativeRoutesController_PutGeometry_UsesWriteRoleAndDispatchesCommand()
    {
        var alternativeRouteId = Guid.NewGuid();
        const string pathPolyline = "_p~iF~ps|U_ulLnnqC_mqNvxq`@";
        var responseDto = new AlternativeRouteDto(
            alternativeRouteId,
            Guid.NewGuid(),
            "Alt",
            null,
            Guid.NewGuid(),
            null,
            null,
            pathPolyline,
            true,
            [],
            default,
            default);
        var mediator = new CapturingMediator(responseDto);
        var controller = CreateAlternativeRoutesController(mediator);

        var response = await controller.PutGeometryAsync(
            alternativeRouteId,
            new SetRouteGeometryRequest(pathPolyline),
            CancellationToken.None);

        response.Result.Should().BeOfType<OkObjectResult>();
        var command = mediator.LastRequest.Should().BeOfType<SetAlternativeRouteGeometryCommand>().Subject;
        command.OperatorId.Should().Be(OperatorId);
        command.AlternativeRouteId.Should().Be(alternativeRouteId);
        command.PathPolyline.Should().Be(pathPolyline);
        typeof(OperatorAlternativeRoutesController).GetMethod(nameof(OperatorAlternativeRoutesController.PutGeometryAsync))!
            .GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("OPERATOR_ADMIN");
    }

    [Fact]
    public async Task OperatorRoutesController_AddAlternativeRoute_UsesWriteRoleAndSendsCommand()
    {
        var routeId = Guid.NewGuid();
        var response = new AlternativeRouteDto(Guid.NewGuid(), routeId, "Alt", null, Guid.NewGuid(), null, null, null, true, [], default, default);
        var mediator = new CapturingMediator(response);
        var controller = CreateRoutesController(mediator);
        var request = new CreateAlternativeRouteRequest(
            "Alt",
            null,
            response.DestinationStationId,
            null,
            null,
            [new AlternativeRouteStopRequest(Guid.NewGuid(), 1, 10, null)]);

        var result = await controller.AddAlternativeRouteAsync(routeId, request, CancellationToken.None);

        result.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status201Created);
        mediator.LastRequest.Should().BeOfType<CreateAlternativeRouteCommand>()
            .Which.OperatorId.Should().Be(OperatorId);
        typeof(OperatorRoutesController).GetMethod(nameof(OperatorRoutesController.AddAlternativeRouteAsync))!
            .GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("OPERATOR_ADMIN");
        typeof(OperatorRoutesController).GetMethod(nameof(OperatorRoutesController.GetAlternativeRoutesAsync))!
            .GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("OPERATOR_STAFF,OPERATOR_ADMIN");
    }

    private static CreateAlternativeRouteHandler CreateCreateHandler(
        FakeAlternativeRouteRepository alternativeRouteRepository,
        FakeRouteRepository routeRepository,
        FakeStationRepository stationRepository,
        FakeStopRepository stopRepository)
        => new(
            alternativeRouteRepository,
            new FakeIdentityInternalClient(OperatorWriteEligibilityValidation.Allowed()),
            routeRepository,
            stationRepository,
            stopRepository,
            new FakeUnitOfWork());

    private static UpdateAlternativeRouteHandler CreateUpdateHandler(
        FakeAlternativeRouteRepository alternativeRouteRepository,
        FakeStationRepository stationRepository,
        FakeStopRepository stopRepository)
        => new(
            alternativeRouteRepository,
            new FakeIdentityInternalClient(OperatorWriteEligibilityValidation.Allowed()),
            stationRepository,
            stopRepository,
            new FakeUnitOfWork());

    private static CreateAlternativeRouteCommand CreateCommand(Guid routeId, Guid destinationStationId, Guid stopId)
        => new(
            OperatorId,
            routeId,
            "Incident bypass",
            "Use when Hai Van tunnel is blocked.",
            destinationStationId,
            11m,
            20,
            [new AlternativeRouteStopInput(stopId, 1, 10, 3m)]);

    private static UpdateAlternativeRouteCommand UpdateCommand(Guid alternativeRouteId, Guid destinationStationId, Guid stopId)
        => new(
            OperatorId,
            alternativeRouteId,
            "Updated bypass",
            true,
            null,
            true,
            destinationStationId,
            true,
            12m,
            true,
            22,
            true,
            true,
            true,
            [new AlternativeRouteStopInput(stopId, 1, 10, 3m)]);

    private static Route CreateRoute(Guid operatorId)
        => Route.Create(operatorId, "Da Nang to Hue", Guid.NewGuid(), Guid.NewGuid(), Money.FromRaw(250000), 100m, 180);

    private static Station CreateStation()
        => Station.Create("Destination", $"destination-{Guid.NewGuid():N}", "Hue", "Thua Thien Hue");

    private static Stop CreateStop(Guid operatorId)
        => Stop.Create(operatorId, $"Stop {Guid.NewGuid():N}", 16.1m, 108.2m);

    private static OperatorRoutesController CreateRoutesController(IMediator mediator)
    {
        var controller = new OperatorRoutesController(mediator);
        controller.ControllerContext = CreateControllerContext();
        return controller;
    }

    private static OperatorAlternativeRoutesController CreateAlternativeRoutesController(IMediator mediator)
    {
        var controller = new OperatorAlternativeRoutesController(mediator);
        controller.ControllerContext = CreateControllerContext();
        return controller;
    }

    private static ControllerContext CreateControllerContext()
        => new()
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim("operatorId", OperatorId.ToString())],
                    "TestAuth")),
            },
        };

    private sealed class FakeAlternativeRouteRepository : IAlternativeRouteRepository
    {
        private readonly List<Route> routes;

        public FakeAlternativeRouteRepository(
            IReadOnlyCollection<AlternativeRoute> alternativeRoutes,
            IReadOnlyCollection<Route>? routes = null)
        {
            Entities = alternativeRoutes.ToList();
            this.routes = routes?.ToList() ?? [];
        }

        public List<AlternativeRoute> Entities { get; }

        public List<AlternativeRouteStop> Stops { get; } = [];

        public Task<AlternativeRoute> AddAsync(AlternativeRoute entity, CancellationToken ct)
        {
            Entities.Add(entity);
            return Task.FromResult(entity);
        }

        public Task<bool> ExistsStopAsync(Guid alternativeRouteId, Guid stopId, CancellationToken cancellationToken)
            => Task.FromResult(Stops.Any(stop => stop.AlternativeRouteId == alternativeRouteId && stop.StopId == stopId));

        public Task<bool> ExistsStopOrderIndexAsync(Guid alternativeRouteId, int orderIndex, CancellationToken cancellationToken)
            => Task.FromResult(Stops.Any(stop => stop.AlternativeRouteId == alternativeRouteId && stop.OrderIndex == orderIndex));

        public Task<AlternativeRoute?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(Entities.FirstOrDefault(alternativeRoute => alternativeRoute.Id == id));

        public Task<AlternativeRoute?> GetOwnedByIdAsync(Guid operatorId, Guid alternativeRouteId, CancellationToken cancellationToken)
            => Task.FromResult(Entities.FirstOrDefault(alternativeRoute => alternativeRoute.Id == alternativeRouteId
                && routes.Any(route => route.Id == alternativeRoute.RouteId && route.OperatorId == operatorId)));

        public Task<IReadOnlyList<AlternativeRouteStop>> ListStopsAsync(Guid alternativeRouteId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<AlternativeRouteStop>>(Stops.Where(stop => stop.AlternativeRouteId == alternativeRouteId).ToList());

        public IQueryable<AlternativeRoute> Query() => Entities.AsQueryable();

        public IQueryable<AlternativeRoute> QueryNoTracking() => Entities.AsQueryable();

        public void Remove(AlternativeRoute entity) => Entities.Remove(entity);

        public Task ReplaceStopsAsync(Guid alternativeRouteId, IReadOnlyCollection<AlternativeRouteStop> stops, CancellationToken cancellationToken)
        {
            Stops.RemoveAll(stop => stop.AlternativeRouteId == alternativeRouteId);
            Stops.AddRange(stops);
            return Task.CompletedTask;
        }

        public void Update(AlternativeRoute entity) { }
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

        public Task<bool> ExistsActiveOwnedByOperatorAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken)
            => Task.FromResult(routes.Any(route => route.Id == routeId && route.OperatorId == operatorId && route.IsActive));

        public Task<Route?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(routes.FirstOrDefault(route => route.Id == id));

        public Task<Route?> GetOwnedActiveByIdAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken)
            => Task.FromResult(routes.FirstOrDefault(route => route.Id == routeId && route.OperatorId == operatorId && route.IsActive));

        public Task<Route?> GetOwnedByIdAsync(Guid operatorId, Guid routeId, CancellationToken cancellationToken)
            => Task.FromResult(routes.FirstOrDefault(route => route.Id == routeId && route.OperatorId == operatorId));

        public Task<IReadOnlyList<Route>> ListByOperatorAsync(Guid operatorId, string? search, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Route>>(routes.Where(route => route.OperatorId == operatorId).ToList());

        public IQueryable<Route> Query() => routes.AsQueryable();

        public IQueryable<Route> QueryNoTracking() => routes.AsQueryable();

        public void Remove(Route entity) => routes.Remove(entity);

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

        public Task<IReadOnlyList<Station>> SearchActiveByNameAsync(string? q, string? city, string? province, Guid? locationId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Station>>(stations);

        public IQueryable<Station> Query() => stations.AsQueryable();

        public IQueryable<Station> QueryNoTracking() => stations.AsQueryable();

        public void Remove(Station entity) => stations.Remove(entity);

        public void Update(Station entity) { }
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
