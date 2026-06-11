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
using VietRide.Trip.Application.Features.Internal.Stops;
using VietRide.Trip.Application.Features.Stops;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Features.Stops;

public sealed class StopHandlersTests
{
    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherOperatorId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task CreateStop_CreatesOperatorScopedStop_WhenOperatorIsEligible()
    {
        var repository = new FakeStopRepository([]);
        var handler = CreateHandler(repository);

        var result = await handler.Handle(CreateCommand(googlePlaceId: "opaque-google-place"), CancellationToken.None);

        result.OperatorId.Should().Be(OperatorId);
        result.GooglePlaceId.Should().Be("opaque-google-place");
        result.Description.Should().Be("Điểm đón trung tâm");
        result.Address.Should().Be("Đường Trung Tâm");
        repository.Entities.Should().ContainSingle();
    }

    [Fact]
    public async Task CreateStop_ThrowsForbiddenAndDoesNotCreate_WhenOperatorIsInactiveOrNotApproved()
    {
        var repository = new FakeStopRepository([]);
        var handler = CreateHandler(repository, OperatorWriteEligibilityValidation.Forbidden("Operator is not approved."));

        var act = () => handler.Handle(CreateCommand(), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ForbiddenException>();
        exception.Which.ErrorCode.Should().Be("FORBIDDEN");
        repository.Entities.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateStop_ThrowsValidationExceptionAndDoesNotCreate_WhenIdentityLogicalFkValidationFails()
    {
        var repository = new FakeStopRepository([]);
        var handler = CreateHandler(
            repository,
            OperatorWriteEligibilityValidation.ValidationFailure("Operator logical FK validation failed."));

        var act = () => handler.Handle(CreateCommand(), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().Contain(error => error.Field == "operatorId");
        repository.Entities.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateStopValidator_RejectsInvalidCoordinatesWithValidationError()
    {
        var behavior = new ValidationBehavior<CreateStopCommand, StopDto>([new CreateStopValidator()]);
        var command = CreateCommand(latitude: 91m, longitude: 181m);

        var act = () => behavior.Handle(
            command,
            () => Task.FromResult(new StopDto(Guid.NewGuid(), OperatorId, "Stop", null, 0m, 0m, null, null, true, default, default)),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Select(error => error.Field).Should().Contain([
            nameof(CreateStopCommand.Latitude),
            nameof(CreateStopCommand.Longitude)]);
    }

    [Theory]
    [InlineData(null, 108.2208)]
    [InlineData(16.0678, null)]
    public async Task CreateStopValidator_RejectsMissingLatitudeOrLongitudeAndDoesNotCreateStop(
        double? latitude,
        double? longitude)
    {
        var behavior = new ValidationBehavior<CreateStopCommand, StopDto>([new CreateStopValidator()]);
        var repository = new FakeStopRepository([]);
        var command = CreateCommand(
            latitude: latitude.HasValue ? (decimal)latitude.Value : null,
            longitude: longitude.HasValue ? (decimal)longitude.Value : null);

        var act = () => behavior.Handle(
            command,
            async () => await CreateHandler(repository).Handle(command, CancellationToken.None),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().Contain(error =>
            error.Field == nameof(CreateStopCommand.Latitude)
            || error.Field == nameof(CreateStopCommand.Longitude));
        repository.Entities.Should().BeEmpty();
    }

    [Fact]
    public async Task ListStops_ReturnsOnlyCallerStopsWithTenantScopedTotalAndSearch()
    {
        var ownMatch = Stop.Create(OperatorId, "Bến xe Trung Tâm", 16.0678m, 108.2208m, address: "Da Nang");
        var ownNonMatch = Stop.Create(OperatorId, "Bến xe Phía Bắc", 16.10m, 108.20m, address: "Hai Chau");
        var otherMatch = Stop.Create(OtherOperatorId, "Bến xe Trung Tâm", 16.0678m, 108.2208m, address: "Da Nang");
        var handler = new ListStopsHandler(new FakeStopRepository([ownMatch, ownNonMatch, otherMatch]));

        var result = await handler.Handle(new ListStopsQuery(OperatorId, 1, 20, "Trung Tâm"), CancellationToken.None);

        result.Items.Should().ContainSingle(item => item.Id == ownMatch.Id);
        result.TotalItems.Should().Be(1);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(20);
    }

    [Fact]
    public async Task ListStops_ClampsPageSizeToMaxOneHundred_WhenRequestedPageSizeExceedsLimit()
    {
        var stops = Enumerable.Range(1, 101)
            .Select(index => Stop.Create(OperatorId, $"Stop {index:D3}", 16.0678m, 108.2208m))
            .ToArray();
        var handler = new ListStopsHandler(new FakeStopRepository(stops));

        var result = await handler.Handle(new ListStopsQuery(OperatorId, 1, 101, null), CancellationToken.None);

        result.PageSize.Should().Be(100);
        result.Items.Should().HaveCount(100);
        result.TotalItems.Should().Be(101);
    }

    [Fact]
    public async Task GetStop_ThrowsStopNotFound_ForMissingOrCrossOperatorStop()
    {
        var crossOperatorStop = Stop.Create(OtherOperatorId, "Bến xe Trung Tâm", 16.0678m, 108.2208m);
        var handler = new GetStopHandler(new FakeStopRepository([crossOperatorStop]));

        var act = () => handler.Handle(new GetStopQuery(OperatorId, crossOperatorStop.Id), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedNotFoundException>();
        exception.Which.ErrorCode.Should().Be("STOP_NOT_FOUND");
    }

    [Fact]
    public async Task GetStopById_ReturnsCanonicalRawDto_WhenStopExists()
    {
        var stop = Stop.Create(OperatorId, "Bến xe Trung Tâm", 16.0678m, 108.2208m, "Điểm đón trung tâm", "Đường Trung Tâm", "google-place-id");
        var handler = new GetStopByIdHandler(new FakeStopRepository([stop]));

        var result = await handler.Handle(new GetStopByIdQuery(stop.Id), CancellationToken.None);

        result.Id.Should().Be(stop.Id);
        result.OperatorId.Should().Be(stop.OperatorId);
        result.Name.Should().Be(stop.Name);
        result.Description.Should().Be(stop.Description);
        result.Latitude.Should().Be(stop.Latitude);
        result.Longitude.Should().Be(stop.Longitude);
        result.Address.Should().Be(stop.Address);
        result.GooglePlaceId.Should().Be(stop.GooglePlaceId);
    }

    [Fact]
    public async Task GetStopById_ThrowsCodedStopNotFound_WhenStopIsMissing()
    {
        var handler = new GetStopByIdHandler(new FakeStopRepository([]));

        var act = () => handler.Handle(new GetStopByIdQuery(Guid.NewGuid()), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedNotFoundException>();
        exception.Which.ErrorCode.Should().Be("STOP_NOT_FOUND");
    }

    [Fact]
    public async Task InternalStopsController_ReturnsRawDto_WithoutApiResponseEnvelope()
    {
        var stop = new InternalStopDto(
            Guid.NewGuid(),
            OperatorId,
            "Bến xe Trung Tâm",
            "Điểm đón trung tâm",
            16.0678m,
            108.2208m,
            "Đường Trung Tâm",
            "google-place-id",
            true,
            default,
            default);
        var mediator = new CapturingMediator(stop);
        var controller = new InternalStopsController(mediator);

        var response = await controller.GetByIdAsync(stop.Id, CancellationToken.None);

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeSameAs(stop);
        ok.Value.Should().NotBeOfType<ApiResponse>();
        var query = mediator.LastRequest.Should().BeOfType<GetStopByIdQuery>().Subject;
        query.Id.Should().Be(stop.Id);
    }

    [Fact]
    public async Task UpdateStop_UpdatesOnlyCallerStop_WhenOperatorIsEligible()
    {
        var stop = Stop.Create(OperatorId, "Bến xe Cũ", 16.0678m, 108.2208m);
        var handler = CreateUpdateHandler(new FakeStopRepository([stop]));

        var result = await handler.Handle(new UpdateStopCommand(
            OperatorId,
            stop.Id,
            "Bến xe Mới",
            16.1m,
            108.3m,
            "Mô tả mới",
            "Địa chỉ mới",
            "place-id"), CancellationToken.None);

        result.Name.Should().Be("Bến xe Mới");
        result.GooglePlaceId.Should().Be("place-id");
    }

    [Fact]
    public async Task UpdateStop_ThrowsValidationExceptionAndDoesNotUpdate_WhenIdentityLogicalFkValidationFails()
    {
        var stop = Stop.Create(OperatorId, "Bến xe Cũ", 16.0678m, 108.2208m);
        var handler = CreateUpdateHandler(
            new FakeStopRepository([stop]),
            OperatorWriteEligibilityValidation.ValidationFailure("Operator logical FK validation failed."));

        var act = () => handler.Handle(new UpdateStopCommand(
            OperatorId,
            stop.Id,
            "Bến xe Mới",
            16.1m,
            108.3m,
            "Mô tả mới",
            "Địa chỉ mới",
            "place-id"), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().Contain(error => error.Field == "operatorId");
        stop.Name.Should().Be("Bến xe Cũ");
        stop.GooglePlaceId.Should().BeNull();
    }

    [Fact]
    public async Task UpdateStop_ThrowsStopNotFound_ForCrossOperatorStop()
    {
        var crossOperatorStop = Stop.Create(OtherOperatorId, "Bến xe Trung Tâm", 16.0678m, 108.2208m);
        var handler = CreateUpdateHandler(new FakeStopRepository([crossOperatorStop]));

        var act = () => handler.Handle(new UpdateStopCommand(OperatorId, crossOperatorStop.Id, "New", null, null, null, null, null), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedNotFoundException>();
        exception.Which.ErrorCode.Should().Be("STOP_NOT_FOUND");
    }

    [Fact]
    public async Task OperatorStopsController_SendsOperatorScopedCreateCommand()
    {
        var mediator = new CapturingMediator(new StopDto(Guid.NewGuid(), OperatorId, "Stop", null, 16.0678m, 108.2208m, null, null, true, default, default));
        var controller = CreateController(mediator, "OPERATOR_ADMIN");
        var request = new CreateStopRequest("Stop", 16.0678m, 108.2208m, "Description", "Address", "google-place-id");

        await controller.PostAsync(request, CancellationToken.None);

        var command = mediator.LastRequest.Should().BeOfType<CreateStopCommand>().Subject;
        command.OperatorId.Should().Be(OperatorId);
        command.GooglePlaceId.Should().Be("google-place-id");
    }

    [Fact]
    public void OperatorStopsController_UsesExpectedRolesAndDoesNotExposeDeleteEndpoint()
    {
        typeof(OperatorStopsController).GetMethod(nameof(OperatorStopsController.PostAsync))!
            .GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("OPERATOR_ADMIN");
        typeof(OperatorStopsController).GetMethod(nameof(OperatorStopsController.PatchAsync))!
            .GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("OPERATOR_ADMIN");
        typeof(OperatorStopsController).GetMethod(nameof(OperatorStopsController.GetAsync))!
            .GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("OPERATOR_STAFF,OPERATOR_ADMIN");
        typeof(OperatorStopsController).GetMethod(nameof(OperatorStopsController.GetByIdAsync))!
            .GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("OPERATOR_STAFF,OPERATOR_ADMIN");

        typeof(OperatorStopsController).GetMethods()
            .SelectMany(method => method.GetCustomAttributes<HttpDeleteAttribute>())
            .Should().BeEmpty();
    }

    private static CreateStopCommand CreateCommand(
        decimal? latitude = 16.0678m,
        decimal? longitude = 108.2208m,
        string? googlePlaceId = null)
        => new(
            OperatorId,
            "Bến xe Trung Tâm",
            latitude,
            longitude,
            "Điểm đón trung tâm",
            "Đường Trung Tâm",
            googlePlaceId);

    private static CreateStopHandler CreateHandler(
        FakeStopRepository repository,
        OperatorWriteEligibilityValidation? eligibility = null)
        => new(
            new FakeIdentityInternalClient(eligibility ?? OperatorWriteEligibilityValidation.Allowed()),
            repository,
            new FakeUnitOfWork());

    private static UpdateStopHandler CreateUpdateHandler(
        FakeStopRepository repository,
        OperatorWriteEligibilityValidation? eligibility = null)
        => new(
            new FakeIdentityInternalClient(eligibility ?? OperatorWriteEligibilityValidation.Allowed()),
            repository,
            new FakeUnitOfWork());

    private static OperatorStopsController CreateController(CapturingMediator mediator, string role)
    {
        var controller = new OperatorStopsController(mediator);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim("sub", Guid.NewGuid().ToString()),
                    new Claim("role", role),
                    new Claim("operatorId", OperatorId.ToString())], "TestAuth")),
            },
        };

        return controller;
    }

    private sealed class FakeStopRepository : IStopRepository
    {
        public FakeStopRepository(IReadOnlyCollection<Stop> stops)
        {
            Entities = stops.ToList();
        }

        public List<Stop> Entities { get; }

        public Task<Stop> AddAsync(Stop entity, CancellationToken ct)
        {
            Entities.Add(entity);
            return Task.FromResult(entity);
        }

        public Task<Stop?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(Entities.FirstOrDefault(stop => stop.Id == id));

        public IQueryable<Stop> Query() => Entities.AsQueryable();

        public IQueryable<Stop> QueryNoTracking() => Entities.AsQueryable();

        public void Remove(Stop entity) => Entities.Remove(entity);

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
