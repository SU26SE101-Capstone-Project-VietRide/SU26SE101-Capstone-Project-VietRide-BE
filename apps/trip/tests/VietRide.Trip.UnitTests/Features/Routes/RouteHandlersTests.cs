using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
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
        result.BaseFare.Should().Be(250500);
        routeRepository.Entities.Should().Contain(route => route.Id == result.Id);
        returnRoute.ReturnRouteId.Should().BeNull("returnRouteId is one-way and must not mutate the target route");
    }

    [Fact]
    public async Task CreateRoute_CreatesRoute_WhenShuttleModuleIsDisabled()
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
            shuttleModuleEnabled: false);

        var result = await handler.Handle(
            CreateCommand(origin.Id, destination.Id),
            CancellationToken.None);

        result.OperatorId.Should().Be(OperatorId);
        routeRepository.Entities.Should().ContainSingle(route => route.Id == result.Id);
    }

    [Fact]
    public async Task CreateRoute_AllowsCodeReuseAfterSoftDelete()
    {
        var origin = CreateStation("Origin", "origin");
        var destination = CreateStation("Destination", "destination");
        var deletedRoute = Route.Create(
            OperatorId,
            "Deleted route",
            origin.Id,
            destination.Id,
            VietRide.Shared.Kernel.ValueObjects.Money.FromRaw(250000),
            100m,
            180,
            code: "SG-DL-01");
        deletedRoute.SoftDelete(new DateTimeOffset(2026, 8, 23, 0, 0, 0, TimeSpan.Zero));
        var routeRepository = new FakeRouteRepository([deletedRoute]);
        var handler = CreateHandler(
            routeRepository,
            new FakeStationRepository([origin, destination]),
            new FakeOperatorStationRepository([
                OperatorStation.Create(OperatorId, origin.Id),
                OperatorStation.Create(OperatorId, destination.Id)]));

        var result = await handler.Handle(
            CreateCommand(origin.Id, destination.Id, code: "sg-dl-01"),
            CancellationToken.None);

        result.Code.Should().Be("SG-DL-01");
        routeRepository.Entities.Should().HaveCount(2);
    }

    [Fact]
    public async Task CreateRoute_ReturnsCodedConflictForActiveDuplicateCode()
    {
        var origin = CreateStation("Origin", "origin");
        var destination = CreateStation("Destination", "destination");
        var existingRoute = Route.Create(
            OperatorId,
            "Existing route",
            origin.Id,
            destination.Id,
            VietRide.Shared.Kernel.ValueObjects.Money.FromRaw(250000),
            100m,
            180,
            code: "SG-DL-01");
        var handler = CreateHandler(
            new FakeRouteRepository([existingRoute]),
            new FakeStationRepository([origin, destination]),
            new FakeOperatorStationRepository([
                OperatorStation.Create(OperatorId, origin.Id),
                OperatorStation.Create(OperatorId, destination.Id)]));

        var action = () => handler.Handle(
            CreateCommand(origin.Id, destination.Id, code: "SG-DL-01"),
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<CodedConflictException>();
        exception.Which.ErrorCode.Should().Be("ROUTE_CODE_DUPLICATED");
        exception.Which.Errors.Should().ContainSingle(error => error.Field == "code");
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
        var handler = new ListRoutesHandler(
            new FakeRouteRepository([route, other]),
            new FakeDriverScheduleRepository([]));

        var result = await handler.Handle(new ListRoutesQuery(OperatorId, 1, 20, "Hue"), CancellationToken.None);

        result.Items.Should().ContainSingle(item => item.Id == route.Id);
        result.Items.Single().DepartureSchedules.Should().BeEmpty();
        result.TotalItems.Should().Be(1);
    }

    [Fact]
    public async Task ListRoutes_AppliesIsActiveBeforeTotalAndPaging()
    {
        var active = CreateRoute(OperatorId, "Active route");
        var inactive = CreateRoute(OperatorId, "Inactive route");
        inactive.Deactivate();
        var handler = new ListRoutesHandler(
            new FakeRouteRepository([active, inactive]),
            new FakeDriverScheduleRepository([]));

        var result = await handler.Handle(
            new ListRoutesQuery(OperatorId, 1, 20, null, false),
            CancellationToken.None);

        result.Items.Should().ContainSingle(item => item.Id == inactive.Id);
        result.TotalItems.Should().Be(1);
    }

    [Fact]
    public async Task ListRoutes_ReturnsAllOwnedSchedulesForRoutesOnCurrentPageInStableOrder()
    {
        var route = CreateRoute(OperatorId, "A route");
        var routeOutsidePage = CreateRoute(OperatorId, "Z route");
        var activeSchedule = CreateDriverSchedule(
            OperatorId,
            route.Id,
            new TimeOnly(8, 0),
            new DateOnly(2026, 1, 1),
            null,
            true,
            [1, 3, 5]);
        var inactiveSchedule = CreateDriverSchedule(
            OperatorId,
            route.Id,
            new TimeOnly(6, 0),
            new DateOnly(2026, 1, 1),
            null,
            false,
            [2]);
        var expiredSchedule = CreateDriverSchedule(
            OperatorId,
            route.Id,
            new TimeOnly(7, 0),
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31),
            true,
            [4]);
        var futureSchedule = CreateDriverSchedule(
            OperatorId,
            route.Id,
            new TimeOnly(9, 0),
            new DateOnly(2027, 1, 1),
            null,
            true,
            [6, 7]);
        var otherOperatorSchedule = CreateDriverSchedule(
            OtherOperatorId,
            route.Id,
            new TimeOnly(5, 0),
            new DateOnly(2026, 1, 1),
            null,
            true,
            [1]);
        var scheduleOutsidePage = CreateDriverSchedule(
            OperatorId,
            routeOutsidePage.Id,
            new TimeOnly(10, 0),
            new DateOnly(2026, 1, 1),
            null,
            true,
            [1]);
        var handler = new ListRoutesHandler(
            new FakeRouteRepository([route, routeOutsidePage]),
            new FakeDriverScheduleRepository([
                activeSchedule,
                inactiveSchedule,
                expiredSchedule,
                futureSchedule,
                otherOperatorSchedule,
                scheduleOutsidePage,
            ]));

        var result = await handler.Handle(
            new ListRoutesQuery(OperatorId, 1, 1, null),
            CancellationToken.None);

        var item = result.Items.Should().ContainSingle().Subject;
        item.Id.Should().Be(route.Id);
        item.DepartureSchedules.Select(schedule => schedule.Id).Should().Equal(
            inactiveSchedule.Id,
            expiredSchedule.Id,
            activeSchedule.Id,
            futureSchedule.Id);
        item.DepartureSchedules.Should().Contain(schedule => !schedule.IsActive);
        item.DepartureSchedules.Should().Contain(schedule => schedule.ValidUntil == new DateOnly(2025, 12, 31));
        item.DepartureSchedules.Should().Contain(schedule => schedule.ValidFrom == new DateOnly(2027, 1, 1));
        item.DepartureSchedules.Should().OnlyContain(schedule => schedule.TimeZone == "Asia/Ho_Chi_Minh");
        item.DepartureSchedules.Should().NotContain(schedule => schedule.Id == otherOperatorSchedule.Id);
        item.DepartureSchedules.Should().NotContain(schedule => schedule.Id == scheduleOutsidePage.Id);
    }

    [Fact]
    public async Task ListRoutesValidator_RejectsInvalidPagination()
    {
        var behavior = new ValidationBehavior<ListRoutesQuery, PagedResult<RouteListItemDto>>([new ListRoutesValidator()]);
        var query = new ListRoutesQuery(OperatorId, 0, 0, null);

        var act = () => behavior.Handle(
            query,
            () => Task.FromResult(PagedResult<RouteListItemDto>.Create([], 1, 20, 0)),
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
            true,
            260500,
            105.5m,
            180,
            false), CancellationToken.None);

        result.Name.Should().Be("Da Nang to Hue Express");
        result.ReturnRouteId.Should().Be(returnRoute.Id);
        result.BaseFare.Should().Be(260500);
        result.TotalDistanceKm.Should().Be(105.5m);
        result.EstimatedDurationMinutes.Should().Be(180);
        result.IsActive.Should().BeFalse();
        returnRoute.ReturnRouteId.Should().BeNull("returnRouteId is one-way and must not mutate the target route");
    }

    [Fact]
    public async Task UpdateRoute_PreservesReturnRouteId_WhenFieldIsOmitted()
    {
        var route = CreateRoute(OperatorId, "Da Nang to Hue");
        var returnRoute = CreateRoute(OperatorId, "Hue to Da Nang");
        route.UpdateDetails(
            route.Name,
            route.OriginStationId,
            route.DestinationStationId,
            route.BaseFare,
            route.TotalDistanceKm,
            route.EstimatedDurationMinutes,
            returnRoute.Id);
        var handler = CreateUpdateHandler(new FakeRouteRepository([route, returnRoute]));

        var result = await handler.Handle(new UpdateRouteCommand(
            OperatorId,
            route.Id,
            "Da Nang to Hue Express",
            null,
            false,
            null,
            null,
            null,
            null), CancellationToken.None);

        result.ReturnRouteId.Should().Be(returnRoute.Id);
    }

    [Fact]
    public async Task UpdateRoute_ClearsReturnRouteId_WhenFieldIsExplicitNull()
    {
        var route = CreateRoute(OperatorId, "Da Nang to Hue");
        var returnRoute = CreateRoute(OperatorId, "Hue to Da Nang");
        route.UpdateDetails(
            route.Name,
            route.OriginStationId,
            route.DestinationStationId,
            route.BaseFare,
            route.TotalDistanceKm,
            route.EstimatedDurationMinutes,
            returnRoute.Id);
        var handler = CreateUpdateHandler(new FakeRouteRepository([route, returnRoute]));

        var result = await handler.Handle(new UpdateRouteCommand(
            OperatorId,
            route.Id,
            null,
            null,
            true,
            null,
            null,
            null,
            null), CancellationToken.None);

        result.ReturnRouteId.Should().BeNull();
    }

    [Fact]
    public async Task UpdateRoute_ThrowsRouteNotFound_ForCrossOperatorRoute()
    {
        var route = CreateRoute(OtherOperatorId, "Da Nang to Hue");
        var handler = CreateUpdateHandler(new FakeRouteRepository([route]));

        var act = () => handler.Handle(new UpdateRouteCommand(OperatorId, route.Id, "New", null, false, null, null, null, null), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedNotFoundException>();
        exception.Which.ErrorCode.Should().Be("ROUTE_NOT_FOUND");
    }

    [Fact]
    public async Task UpdateRoute_ThrowsRouteNotFound_WhenReturnRouteDoesNotExist()
    {
        var route = CreateRoute(OperatorId, "Da Nang to Hue");
        var handler = CreateUpdateHandler(new FakeRouteRepository([route]));

        var act = () => handler.Handle(new UpdateRouteCommand(OperatorId, route.Id, null, Guid.NewGuid(), true, null, null, null, null), CancellationToken.None);

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
            false,
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
        var route = new RouteDto(routeId, OperatorId, "Da Nang to Hue", Guid.NewGuid(), Guid.NewGuid(), null, 250000, 100m, 180, null, true, default, default);
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

    [Fact]
    public async Task OperatorRoutesController_PutGeometry_UsesWriteRoleAndDispatchesCommand()
    {
        var routeId = Guid.NewGuid();
        const string pathPolyline = "_p~iF~ps|U_ulLnnqC_mqNvxq`@";
        var route = new RouteDto(routeId, OperatorId, "Da Nang to Hue", Guid.NewGuid(), Guid.NewGuid(), null, 250000, 100m, 180, pathPolyline, true, default, default);
        var mediator = new CapturingMediator(route);
        var controller = CreateController(mediator);

        var response = await controller.PutGeometryAsync(
            routeId,
            new SetRouteGeometryRequest(pathPolyline),
            CancellationToken.None);

        response.Result.Should().BeOfType<OkObjectResult>();
        var command = mediator.LastRequest.Should().BeOfType<SetRouteGeometryCommand>().Subject;
        command.OperatorId.Should().Be(OperatorId);
        command.RouteId.Should().Be(routeId);
        command.PathPolyline.Should().Be(pathPolyline);
        typeof(OperatorRoutesController).GetMethod(nameof(OperatorRoutesController.PutGeometryAsync))!
            .GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("OPERATOR_ADMIN");
    }

    private static CreateRouteHandler CreateHandler(
        FakeRouteRepository routeRepository,
        FakeStationRepository stationRepository,
        FakeOperatorStationRepository operatorStationRepository,
        OperatorWriteEligibilityValidation? eligibility = null,
        bool shuttleModuleEnabled = true)
        => new(
            new FakeIdentityInternalClient(
                eligibility ?? OperatorWriteEligibilityValidation.Allowed(),
                shuttleModuleEnabled),
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

    private static CreateRouteCommand CreateCommand(
        Guid originStationId,
        Guid destinationStationId,
        Guid? returnRouteId = null,
        string? code = null)
        => new(OperatorId, "Da Nang to Hue", originStationId, destinationStationId, returnRouteId, 250500, 100m, 180, true, code);

    private static Route CreateRoute(Guid operatorId, string name, bool isActive = true)
    {
        var route = Route.Create(operatorId, name, Guid.NewGuid(), Guid.NewGuid(), VietRide.Shared.Kernel.ValueObjects.Money.FromRaw(250000), 100m, 180);

        if (!isActive)
        {
            route.Deactivate();
        }

        return route;
    }

    private static DriverSchedule CreateDriverSchedule(
        Guid operatorId,
        Guid routeId,
        TimeOnly departureTime,
        DateOnly validFrom,
        DateOnly? validUntil,
        bool isActive,
        IReadOnlyCollection<int> dayOfWeek) =>
        DriverSchedule.Create(
            operatorId,
            routeId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            JsonSerializer.SerializeToElement(dayOfWeek),
            departureTime,
            validFrom,
            validUntil,
            isActive);

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

    private sealed class FakeDriverScheduleRepository : IDriverScheduleRepository
    {
        private readonly List<DriverSchedule> schedules;

        public FakeDriverScheduleRepository(IReadOnlyCollection<DriverSchedule> schedules)
        {
            this.schedules = schedules.ToList();
        }

        public Task<DriverSchedule> AddAsync(DriverSchedule entity, CancellationToken cancellationToken)
        {
            schedules.Add(entity);
            return Task.FromResult(entity);
        }

        public Task<DriverSchedule?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
            => Task.FromResult(schedules.FirstOrDefault(schedule => schedule.Id == id));

        public Task<bool> HasDriverConflictAsync(
            Guid driverUserId,
            IReadOnlyCollection<int> dayOfWeek,
            TimeOnly departureTime,
            DateOnly validFrom,
            DateOnly? validUntil,
            Guid? excludeScheduleId = null,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        public IQueryable<DriverSchedule> Query() => schedules.AsQueryable();

        public IQueryable<DriverSchedule> QueryNoTracking() => schedules.AsQueryable();

        public void Remove(DriverSchedule entity) => schedules.Remove(entity);

        public void Update(DriverSchedule entity) { }
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

        public Task<IReadOnlyList<Station>> SearchActiveByNameAsync(string? q, string? city, string? province, Guid? locationId, CancellationToken cancellationToken)
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
        private readonly bool shuttleModuleEnabled;

        public FakeIdentityInternalClient(
            OperatorWriteEligibilityValidation eligibility,
            bool shuttleModuleEnabled = true)
        {
            this.eligibility = eligibility;
            this.shuttleModuleEnabled = shuttleModuleEnabled;
        }

        public Task<OperatorWriteEligibilityValidation> ValidateOperatorCanWriteAsync(Guid operatorId, CancellationToken cancellationToken = default)
            => Task.FromResult(eligibility);

        public Task<OperatorWriteEligibilityValidation> ValidateOperatorSubscriptionCanWriteAsync(
            Guid operatorId,
            bool requireShuttleModule,
            CancellationToken cancellationToken = default)
            => Task.FromResult(requireShuttleModule && !shuttleModuleEnabled
                ? new OperatorWriteEligibilityValidation(
                    false,
                    403,
                    "SUBSCRIPTION_MODULE_DISABLED",
                    "Shuttle module is disabled.")
                : OperatorWriteEligibilityValidation.Allowed());

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
