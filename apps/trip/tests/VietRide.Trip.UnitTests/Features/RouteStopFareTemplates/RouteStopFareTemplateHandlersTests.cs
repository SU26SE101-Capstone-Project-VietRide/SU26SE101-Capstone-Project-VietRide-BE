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
using VietRide.Trip.Application.Features.RouteStopFareTemplates;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Features.RouteStopFareTemplates;

public sealed class RouteStopFareTemplateHandlersTests
{
    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherOperatorId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset EffectiveFrom = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateRouteStopFareTemplate_CreatesTemplate_WhenRouteAndStopAreValid()
    {
        var route = CreateRoute(OperatorId);
        var stop = CreateStop(OperatorId);
        var routeStop = RouteStop.Create(route.Id, stop.Id, 1, 20, 5m, true, true);
        var fareTemplateRepository = new FakeRouteStopFareTemplateRepository([]);
        var handler = CreateCreateHandler(
            new FakeRouteRepository([route]),
            fareTemplateRepository,
            new FakeRouteStopRepository([routeStop]),
            new FakeStopRepository([stop]));

        var result = await handler.Handle(CreateCommand(route.Id, stop.Id, fareFromThisStop: 123456), CancellationToken.None);

        result.RouteId.Should().Be(route.Id);
        result.StopId.Should().Be(stop.Id);
        result.FareFromThisStop.Should().Be(123456);
        result.EffectiveFrom.Should().Be(EffectiveFrom);
        fareTemplateRepository.Entities.Should().ContainSingle();
    }

    [Fact]
    public async Task CreateRouteStopFareTemplate_NormalizesOffsetWindowToUtc_BeforePersisting()
    {
        var route = CreateRoute(OperatorId);
        var stop = CreateStop(OperatorId);
        var routeStop = RouteStop.Create(route.Id, stop.Id, 1, 20, 5m, true, true);
        var fareTemplateRepository = new FakeRouteStopFareTemplateRepository([]);
        var handler = CreateCreateHandler(
            new FakeRouteRepository([route]),
            fareTemplateRepository,
            new FakeRouteStopRepository([routeStop]),
            new FakeStopRepository([stop]));
        var effectiveFrom = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(7));
        var effectiveUntil = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(7));

        var result = await handler.Handle(
            CreateCommand(route.Id, stop.Id, effectiveFrom: effectiveFrom, effectiveUntil: effectiveUntil),
            CancellationToken.None);

        result.EffectiveFrom.Should().Be(effectiveFrom.ToUniversalTime());
        result.EffectiveUntil.Should().Be(effectiveUntil.ToUniversalTime());
        fareTemplateRepository.Entities.Should().ContainSingle(template =>
            template.EffectiveFrom == effectiveFrom.ToUniversalTime()
            && template.EffectiveUntil == effectiveUntil.ToUniversalTime());
    }

    [Fact]
    public async Task CreateRouteStopFareTemplate_AcceptsNonOverlappingWindowsForSameRouteStop()
    {
        var route = CreateRoute(OperatorId);
        var stop = CreateStop(OperatorId);
        var routeStop = RouteStop.Create(route.Id, stop.Id, 1, 20, 5m, true, true);
        var existing = RouteStopFareTemplate.Create(
            route.Id,
            stop.Id,
            Money.FromRaw(100000),
            EffectiveFrom,
            EffectiveFrom.AddDays(10));
        var fareTemplateRepository = new FakeRouteStopFareTemplateRepository([existing]);
        var handler = CreateCreateHandler(
            new FakeRouteRepository([route]),
            fareTemplateRepository,
            new FakeRouteStopRepository([routeStop]),
            new FakeStopRepository([stop]));

        var result = await handler.Handle(
            CreateCommand(route.Id, stop.Id, effectiveFrom: EffectiveFrom.AddDays(10), effectiveUntil: EffectiveFrom.AddDays(20)),
            CancellationToken.None);

        result.EffectiveFrom.Should().Be(EffectiveFrom.AddDays(10));
        fareTemplateRepository.Entities.Should().HaveCount(2);
    }

    [Fact]
    public async Task CreateRouteStopFareTemplate_ThrowsValidation_WhenEffectiveUntilIsNotAfterEffectiveFrom()
    {
        var route = CreateRoute(OperatorId);
        var stop = CreateStop(OperatorId);
        var routeStop = RouteStop.Create(route.Id, stop.Id, 1, 20, 5m, true, true);
        var handler = CreateCreateHandler(
            new FakeRouteRepository([route]),
            new FakeRouteStopFareTemplateRepository([]),
            new FakeRouteStopRepository([routeStop]),
            new FakeStopRepository([stop]));

        var act = () => handler.Handle(
            CreateCommand(route.Id, stop.Id, effectiveUntil: EffectiveFrom),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().Contain(error => error.Field == "effectiveUntil");
    }

    [Fact]
    public async Task CreateRouteStopFareTemplate_ThrowsValidation_WhenStopIsNotConfiguredOnRoute()
    {
        var route = CreateRoute(OperatorId);
        var stop = CreateStop(OperatorId);
        var handler = CreateCreateHandler(
            new FakeRouteRepository([route]),
            new FakeRouteStopFareTemplateRepository([]),
            new FakeRouteStopRepository([]),
            new FakeStopRepository([stop]));

        var act = () => handler.Handle(CreateCommand(route.Id, stop.Id), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().Contain(error => error.Field == "stopId");
    }

    [Fact]
    public async Task CreateRouteStopFareTemplate_ThrowsValidation_WhenWindowOverlapsExistingOpenEndedTemplate()
    {
        var route = CreateRoute(OperatorId);
        var stop = CreateStop(OperatorId);
        var routeStop = RouteStop.Create(route.Id, stop.Id, 1, 20, 5m, true, true);
        var existing = RouteStopFareTemplate.Create(route.Id, stop.Id, Money.FromRaw(100000), EffectiveFrom, null);
        var handler = CreateCreateHandler(
            new FakeRouteRepository([route]),
            new FakeRouteStopFareTemplateRepository([existing]),
            new FakeRouteStopRepository([routeStop]),
            new FakeStopRepository([stop]));

        var act = () => handler.Handle(
            CreateCommand(route.Id, stop.Id, effectiveFrom: EffectiveFrom.AddDays(30), effectiveUntil: EffectiveFrom.AddDays(40)),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().Contain(error => error.Field == "effectiveFrom");
        exception.Which.Errors.Should().Contain(error => error.Field == "effectiveUntil");
    }

    [Fact]
    public async Task CreateRouteStopFareTemplate_ThrowsRouteNotFound_ForCrossOperatorRoute()
    {
        var otherRoute = CreateRoute(OtherOperatorId);
        var stop = CreateStop(OperatorId);
        var handler = CreateCreateHandler(
            new FakeRouteRepository([otherRoute]),
            new FakeRouteStopFareTemplateRepository([]),
            new FakeRouteStopRepository([]),
            new FakeStopRepository([stop]));

        var act = () => handler.Handle(CreateCommand(otherRoute.Id, stop.Id), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedNotFoundException>();
        exception.Which.ErrorCode.Should().Be("ROUTE_NOT_FOUND");
    }

    [Fact]
    public async Task ListRouteStopFareTemplates_ReturnsTemplates_WhenRouteBelongsToOperator()
    {
        var route = CreateRoute(OperatorId);
        var stop = CreateStop(OperatorId);
        var template = RouteStopFareTemplate.Create(route.Id, stop.Id, Money.FromRaw(100000), EffectiveFrom, null);
        var handler = new ListRouteStopFareTemplatesHandler(
            new FakeRouteRepository([route]),
            new FakeRouteStopFareTemplateRepository([template]));

        var result = await handler.Handle(new ListRouteStopFareTemplatesQuery(OperatorId, route.Id, 1, 20), CancellationToken.None);

        result.Items.Should().ContainSingle(item => item.Id == template.Id);
    }

    [Fact]
    public async Task OperatorRoutesController_DispatchesFareTemplateCommandsAndUsesRoles()
    {
        var dto = new RouteStopFareTemplateDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100000, EffectiveFrom, null, default, default);
        var mediator = new CapturingMediator(dto);
        var controller = CreateController(mediator);
        var request = new CreateRouteStopFareTemplateRequest(dto.StopId, 100000, EffectiveFrom, null);

        var response = await controller.AddFareTemplateAsync(dto.RouteId, request, CancellationToken.None);

        response.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status201Created);
        mediator.LastRequest.Should().BeOfType<CreateRouteStopFareTemplateCommand>()
            .Which.OperatorId.Should().Be(OperatorId);
        typeof(OperatorRoutesController).GetMethod(nameof(OperatorRoutesController.AddFareTemplateAsync))!
            .GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("OPERATOR_ADMIN");
    }

    [Fact]
    public async Task OperatorRoutesController_DispatchesFareTemplateListQuery()
    {
        var paged = PagedResult<RouteStopFareTemplateDto>.Create([], 1, 20, 0);
        var mediator = new CapturingMediator(paged);
        var controller = CreateController(mediator);
        var routeId = Guid.NewGuid();

        var response = await controller.GetFareTemplatesAsync(routeId, 1, 20, CancellationToken.None);

        response.Result.Should().BeOfType<OkObjectResult>();
        mediator.LastRequest.Should().BeOfType<ListRouteStopFareTemplatesQuery>()
            .Which.Should().Match<ListRouteStopFareTemplatesQuery>(query =>
                query.OperatorId == OperatorId && query.RouteId == routeId && query.Page == 1 && query.PageSize == 20);
        typeof(OperatorRoutesController).GetMethod(nameof(OperatorRoutesController.GetFareTemplatesAsync))!
            .GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("OPERATOR_STAFF,OPERATOR_ADMIN");
    }

    private static CreateRouteStopFareTemplateCommand CreateCommand(
        Guid routeId,
        Guid stopId,
        long fareFromThisStop = 100000,
        DateTimeOffset? effectiveFrom = null,
        DateTimeOffset? effectiveUntil = null)
        => new(OperatorId, routeId, stopId, fareFromThisStop, effectiveFrom ?? EffectiveFrom, effectiveUntil);

    private static CreateRouteStopFareTemplateHandler CreateCreateHandler(
        FakeRouteRepository routeRepository,
        FakeRouteStopFareTemplateRepository fareTemplateRepository,
        FakeRouteStopRepository routeStopRepository,
        FakeStopRepository stopRepository,
        OperatorWriteEligibilityValidation? eligibility = null)
        => new(
            new FakeIdentityInternalClient(eligibility ?? OperatorWriteEligibilityValidation.Allowed()),
            routeRepository,
            fareTemplateRepository,
            routeStopRepository,
            stopRepository,
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

    private sealed class FakeRouteStopFareTemplateRepository : IRouteStopFareTemplateRepository
    {
        public FakeRouteStopFareTemplateRepository(IReadOnlyCollection<RouteStopFareTemplate> templates)
        {
            Entities = templates.ToList();
        }

        public List<RouteStopFareTemplate> Entities { get; }

        public Task<RouteStopFareTemplate> AddAsync(RouteStopFareTemplate entity, CancellationToken ct)
        {
            Entities.Add(entity);
            return Task.FromResult(entity);
        }

        public Task<RouteStopFareTemplate?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(Entities.FirstOrDefault(template => template.Id == id));

        public Task<bool> ExistsOverlappingAsync(
            Guid routeId,
            Guid stopId,
            DateTimeOffset effectiveFrom,
            DateTimeOffset? effectiveUntil,
            CancellationToken cancellationToken)
            => Task.FromResult(Entities.Any(template =>
                template.RouteId == routeId
                && template.StopId == stopId
                && (!effectiveUntil.HasValue || template.EffectiveFrom < effectiveUntil.Value)
                && (!template.EffectiveUntil.HasValue || template.EffectiveUntil.Value > effectiveFrom)));

        public Task<IReadOnlyList<RouteStopFareTemplate>> ListByRouteAsync(Guid routeId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<RouteStopFareTemplate>>(Entities.Where(template => template.RouteId == routeId).ToList());

        public IQueryable<RouteStopFareTemplate> Query() => Entities.AsQueryable();

        public IQueryable<RouteStopFareTemplate> QueryNoTracking() => Entities.AsQueryable();

        public void Remove(RouteStopFareTemplate entity) => Entities.Remove(entity);

        public void Update(RouteStopFareTemplate entity) { }
    }

    private sealed class FakeRouteStopRepository : IRouteStopRepository
    {
        private readonly List<RouteStop> routeStops;

        public FakeRouteStopRepository(IReadOnlyCollection<RouteStop> routeStops)
        {
            this.routeStops = routeStops.ToList();
        }

        public Task<RouteStop> AddAsync(RouteStop entity, CancellationToken ct)
        {
            routeStops.Add(entity);
            return Task.FromResult(entity);
        }

        public Task<RouteStop?> GetByIdAsync((Guid RouteId, Guid StopId) id, CancellationToken ct)
            => GetByRouteAndStopAsync(id.RouteId, id.StopId, ct);

        public Task<RouteStop?> GetByRouteAndStopAsync(Guid routeId, Guid stopId, CancellationToken cancellationToken)
            => Task.FromResult(routeStops.FirstOrDefault(routeStop => routeStop.RouteId == routeId && routeStop.StopId == stopId));

        public Task<bool> ExistsByRouteAndOrderIndexAsync(Guid routeId, int orderIndex, CancellationToken cancellationToken)
            => Task.FromResult(routeStops.Any(routeStop => routeStop.RouteId == routeId && routeStop.OrderIndex == orderIndex));

        public IQueryable<RouteStop> Query() => routeStops.AsQueryable();

        public IQueryable<RouteStop> QueryNoTracking() => routeStops.AsQueryable();

        public void Remove(RouteStop entity) => routeStops.Remove(entity);

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
