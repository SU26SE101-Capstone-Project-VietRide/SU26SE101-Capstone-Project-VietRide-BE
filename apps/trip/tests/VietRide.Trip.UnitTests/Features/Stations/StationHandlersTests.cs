using System.Globalization;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using MediatR;
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
using VietRide.Trip.Application.Features.Internal.Stations;
using VietRide.Trip.Application.Features.Stations;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Features.Stations;

public sealed class StationHandlersTests
{
    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task SearchStations_DelegatesToRepository_SearchMethod()
    {
        var repository = new SpyStationRepository([
            Station.Create("Bến xe Miền Tây", "ben-xe-mien-tay", "Ho Chi Minh City", "Ho Chi Minh", latitude: 10.7212345m, longitude: 106.6267890m),
            Station.Create("Bến xe Gia Lâm", "ben-xe-gia-lam", "Ha Noi", "Ha Noi")
        ]);
        var handler = new SearchStationsQueryHandler(repository);

        var result = await handler.Handle(new SearchStationsQuery("Mien Tay", "Ho Chi Minh City", "Ho Chi Minh"), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Name.Should().Be("Bến xe Miền Tây");
        repository.LastQuery.Should().Be("Mien Tay");
        repository.LastCity.Should().Be("Ho Chi Minh City");
        repository.LastProvince.Should().Be("Ho Chi Minh");
    }

    [Fact]
    public async Task GetStationById_ReturnsCanonicalRawDto_WhenStationExists()
    {
        var station = Station.Create("Bến xe Miền Tây", "ben-xe-mien-tay", "Ho Chi Minh City", "Ho Chi Minh", latitude: 10.7212345m, longitude: 106.6267890m);
        var handler = new GetStationByIdHandler(new FakeStationRepository([station]));

        var result = await handler.Handle(new GetStationByIdQuery(station.Id), CancellationToken.None);

        result.Id.Should().Be(station.Id);
        result.Name.Should().Be(station.Name);
        result.Slug.Should().Be(station.Slug);
        result.City.Should().Be(station.City);
        result.Province.Should().Be(station.Province);
        result.Latitude.Should().Be(station.Latitude);
        result.Longitude.Should().Be(station.Longitude);
    }

    [Fact]
    public async Task GetStationById_ThrowsCodedStationNotFound_WhenStationIsMissing()
    {
        var handler = new GetStationByIdHandler(new FakeStationRepository([]));

        var act = () => handler.Handle(new GetStationByIdQuery(Guid.NewGuid()), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedNotFoundException>();
        exception.Which.ErrorCode.Should().Be("STATION_NOT_FOUND");
    }

    [Fact]
    public async Task InternalStationsController_ReturnsRawDto_WithoutApiResponseEnvelope()
    {
        var station = new InternalStationDto(
            Guid.NewGuid(),
            "Bến xe Miền Tây",
            "ben-xe-mien-tay",
            "Ho Chi Minh City",
            "Ho Chi Minh",
            10.7212345m,
            106.6267890m,
            true,
            default,
            default);
        var mediator = new CapturingMediator(station);
        var controller = new InternalStationsController(mediator);

        var response = await controller.GetByIdAsync(station.Id, CancellationToken.None);

        var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeSameAs(station);
        ok.Value.Should().NotBeOfType<ApiResponse>();
        var query = mediator.LastRequest.Should().BeOfType<GetStationByIdQuery>().Subject;
        query.Id.Should().Be(station.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchStations_ThrowsValidationException_WhenQueryIsMissingBlankOrInvalid(string? q)
    {
        var behavior = new ValidationBehavior<SearchStationsQuery, IReadOnlyList<StationSearchResult>>(
            [new SearchStationsQueryValidator()]);

        var act = () => behavior.Handle(
            new SearchStationsQuery(q, null, null),
            () => Task.FromResult<IReadOnlyList<StationSearchResult>>([]),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().Contain(error => error.Field == nameof(SearchStationsQuery.Q));
    }

    [Fact]
    public async Task CreateOrLinkOperatorStation_LinksExistingStation_WhenOperatorIsEligible()
    {
        var station = Station.Create("Bến xe Miền Tây", "ben-xe-mien-tay", "Ho Chi Minh City", "Ho Chi Minh", latitude: 10.7212345m, longitude: 106.6267890m);
        var stationRepository = new FakeStationRepository([station]);
        var operatorStationRepository = new FakeOperatorStationRepository([]);
        var handler = CreateHandler(stationRepository, operatorStationRepository);

        var result = await handler.Handle(new CreateOrLinkOperatorStationCommand(
            OperatorId,
            station.Id,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            "Quầy VietRide",
            "Quầy 12",
            "0900000000",
            "Có mặt trước giờ chạy 30 phút"), CancellationToken.None);

        result.OperatorId.Should().Be(OperatorId);
        result.StationId.Should().Be(station.Id);
        result.IsActive.Should().BeTrue();
        operatorStationRepository.Entities.Should().ContainSingle();
    }

    [Fact]
    public async Task CreateOrLinkOperatorStation_ReturnsExistingMapping_WhenStationAlreadyLinked()
    {
        var station = Station.Create("Bến xe Miền Tây", "ben-xe-mien-tay", "Ho Chi Minh City", "Ho Chi Minh");
        var existing = OperatorStation.Create(OperatorId, station.Id);
        var stationRepository = new FakeStationRepository([station]);
        var operatorStationRepository = new FakeOperatorStationRepository([existing]);
        var handler = CreateHandler(stationRepository, operatorStationRepository);

        var result = await handler.Handle(new CreateOrLinkOperatorStationCommand(
            OperatorId,
            station.Id,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            null,
            null,
            null,
            null), CancellationToken.None);

        result.OperatorId.Should().Be(OperatorId);
        result.StationId.Should().Be(station.Id);
        operatorStationRepository.Entities.Should().ContainSingle();
    }

    [Fact]
    public async Task CreateOrLinkOperatorStation_ThrowsStationNotFound_WhenLinkTargetIsMissing()
    {
        var handler = CreateHandler(new FakeStationRepository([]), new FakeOperatorStationRepository([]));

        var act = () => handler.Handle(new CreateOrLinkOperatorStationCommand(
            OperatorId,
            Guid.NewGuid(),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            null,
            null,
            null,
            null), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedNotFoundException>();
        exception.Which.ErrorCode.Should().Be("STATION_NOT_FOUND");
    }

    [Fact]
    public async Task CreateOrLinkOperatorStation_ReturnsDuplicateNearbyWarning_WithoutCreatingStation()
    {
        var station = Station.Create("Bến xe Miền Tây", "ben-xe-mien-tay", "Ho Chi Minh City", "Ho Chi Minh", latitude: 10.7212345m, longitude: 106.6267890m);
        var stationRepository = new FakeStationRepository([station]);
        var handler = CreateHandler(stationRepository, new FakeOperatorStationRepository([]));

        var result = await handler.Handle(CreateStationCommand(latitude: 10.7212346m, longitude: 106.6267891m), CancellationToken.None);

        result.Warning.Should().NotBeNull();
        result.Warning!.Code.Should().Be("STATION_DUPLICATE_NEARBY");
        result.NearbyStations.Should().ContainSingle();
        stationRepository.Entities.Should().ContainSingle();
    }

    [Fact]
    public async Task CreateOrLinkOperatorStation_CreatesCollisionSafeSlugs_ForSameNameDifferentProvince()
    {
        var first = Station.Create("Bến xe Trung Tâm", "ben-xe-trung-tam-da-nang-da-nang", "Da Nang", "Da Nang");
        var stationRepository = new FakeStationRepository([first]);
        var handler = CreateHandler(stationRepository, new FakeOperatorStationRepository([]));

        await handler.Handle(CreateStationCommand(city: "Da Nang", province: "Da Nang"), CancellationToken.None);

        stationRepository.Entities.Select(station => station.Slug).Should().OnlyHaveUniqueItems();
        stationRepository.Entities.Should().HaveCount(2);
    }

    [Fact]
    public async Task CreateOrLinkOperatorStation_ThrowsForbidden_WhenOperatorIsNotEligible()
    {
        var handler = CreateHandler(
            new FakeStationRepository([]),
            new FakeOperatorStationRepository([]),
            OperatorWriteEligibilityValidation.Forbidden("Operator is not approved."));

        var act = () => handler.Handle(CreateStationCommand(), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ForbiddenException>();
        exception.Which.ErrorCode.Should().Be("FORBIDDEN");
    }

    [Theory]
    [InlineData(null, 108.2208)]
    [InlineData(16.0678, null)]
    public async Task CreateOrLinkOperatorStationValidator_RejectsCreateBranch_WhenLatitudeOrLongitudeIsMissing(
        double? latitude,
        double? longitude)
    {
        var behavior = new ValidationBehavior<CreateOrLinkOperatorStationCommand, CreateOrLinkOperatorStationResponse>(
            [new CreateOrLinkOperatorStationValidator()]);
        var command = CreateStationCommand(
            latitude: latitude.HasValue ? (decimal)latitude.Value : null,
            longitude: longitude.HasValue ? (decimal)longitude.Value : null);

        var act = () => behavior.Handle(
            command,
            () => Task.FromResult(CreateOrLinkOperatorStationResponse.Linked(OperatorId, Guid.NewGuid(), true)),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().Contain(error =>
            error.Field == nameof(CreateOrLinkOperatorStationCommand.Latitude)
            || error.Field == nameof(CreateOrLinkOperatorStationCommand.Longitude));
    }

    [Fact]
    public async Task CreateOrLinkOperatorStation_ThrowsValidationException_WhenIdentityLogicalFkValidationFails()
    {
        var handler = CreateHandler(
            new FakeStationRepository([]),
            new FakeOperatorStationRepository([]),
            OperatorWriteEligibilityValidation.ValidationFailure("Operator logical FK validation failed."));

        var act = () => handler.Handle(CreateStationCommand(), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().Contain(error => error.Field == "operatorId");
    }

    [Fact]
    public async Task CreateOrLinkOperatorStationValidator_RejectsSchemaLengthAndEmailViolations()
    {
        var behavior = new ValidationBehavior<CreateOrLinkOperatorStationCommand, CreateOrLinkOperatorStationResponse>(
            [new CreateOrLinkOperatorStationValidator()]);
        var command = new CreateOrLinkOperatorStationCommand(
            OperatorId,
            null,
            new string('N', 256),
            new string('C', 101),
            new string('P', 101),
            16.0678m,
            108.2208m,
            new string('A', 501),
            new string('0', 21),
            "not-an-email",
            null,
            null,
            true,
            new string('D', 256),
            new string('L', 256),
            null,
            null);

        var act = () => behavior.Handle(
            command,
            () => Task.FromResult(CreateOrLinkOperatorStationResponse.Linked(OperatorId, Guid.NewGuid(), true)),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Select(error => error.Field).Should().Contain([
            nameof(CreateOrLinkOperatorStationCommand.Name),
            nameof(CreateOrLinkOperatorStationCommand.City),
            nameof(CreateOrLinkOperatorStationCommand.Province),
            nameof(CreateOrLinkOperatorStationCommand.AddressStreet),
            nameof(CreateOrLinkOperatorStationCommand.StationContactPhone),
            nameof(CreateOrLinkOperatorStationCommand.ContactEmail),
            nameof(CreateOrLinkOperatorStationCommand.DisplayNameOverride),
            nameof(CreateOrLinkOperatorStationCommand.CounterLocation)]);
    }

    [Fact]
    public async Task OperatorStationsController_UsesContactPhoneOnlyForStation_WhenCreatingStation()
    {
        var mediator = new CapturingMediator(CreateOrLinkOperatorStationResponse.Linked(OperatorId, Guid.NewGuid(), true));
        var controller = CreateOperatorStationsController(mediator);
        var request = new CreateOrLinkOperatorStationRequest(
            null,
            null,
            null,
            "0900000000",
            null,
            "Bến xe Trung Tâm",
            "Da Nang",
            "Da Nang",
            16.0678m,
            108.2208m,
            null,
            null,
            null,
            null,
            true);

        await controller.PostAsync(request, CancellationToken.None);

        var command = mediator.LastRequest.Should().BeOfType<CreateOrLinkOperatorStationCommand>().Subject;
        command.StationContactPhone.Should().Be("0900000000");
        command.OperatorStationContactPhone.Should().BeNull();
    }

    [Fact]
    public async Task OperatorStationsController_UsesContactPhoneOnlyForMapping_WhenLinkingStation()
    {
        var mediator = new CapturingMediator(CreateOrLinkOperatorStationResponse.Linked(OperatorId, Guid.NewGuid(), true));
        var controller = CreateOperatorStationsController(mediator);
        var request = new CreateOrLinkOperatorStationRequest(
            Guid.NewGuid(),
            null,
            null,
            "0900000000",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false);

        await controller.PostAsync(request, CancellationToken.None);

        var command = mediator.LastRequest.Should().BeOfType<CreateOrLinkOperatorStationCommand>().Subject;
        command.StationContactPhone.Should().BeNull();
        command.OperatorStationContactPhone.Should().Be("0900000000");
    }

    private static CreateOrLinkOperatorStationCommand CreateStationCommand(
        decimal? latitude = 16.0678m,
        decimal? longitude = 108.2208m,
        string city = "Da Nang",
        string province = "Da Nang") => new(
            OperatorId,
            null,
            "Bến xe Trung Tâm",
            city,
            province,
            latitude,
            longitude,
            "Đường Trung Tâm",
            "0236000000",
            "station@example.com",
            "{\"mon\":\"05:00-22:00\"}",
            "[\"waiting_room\"]",
            true,
            null,
            null,
            null,
            null);

    private static CreateOrLinkOperatorStationHandler CreateHandler(
        FakeStationRepository stationRepository,
        FakeOperatorStationRepository operatorStationRepository,
        OperatorWriteEligibilityValidation? eligibility = null) => new(
            new FakeIdentityInternalClient(eligibility ?? OperatorWriteEligibilityValidation.Allowed()),
            operatorStationRepository,
            stationRepository,
            new FakeUnitOfWork());

    private static OperatorStationsController CreateOperatorStationsController(CapturingMediator mediator)
    {
        var controller = new OperatorStationsController(mediator);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim("sub", Guid.NewGuid().ToString()),
                    new Claim("role", "OPERATOR_ADMIN"),
                    new Claim("operatorId", OperatorId.ToString())
                ], "TestAuth"))
            }
        };

        return controller;
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
            return Task.FromResult((TResponse)(object)response);
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

    private class FakeStationRepository : IStationRepository
    {
        public FakeStationRepository(IReadOnlyCollection<Station> stations)
        {
            Entities = stations.ToList();
        }

        public List<Station> Entities { get; }

        public Task<Station> AddAsync(Station entity, CancellationToken ct)
        {
            Entities.Add(entity);
            return Task.FromResult(entity);
        }

        public Task<Station?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Entities.FirstOrDefault(station => station.Id == id));

        public IQueryable<Station> Query() => Entities.AsQueryable();

        public IQueryable<Station> QueryNoTracking() => Entities.AsQueryable();

        public virtual Task<IReadOnlyList<Station>> SearchActiveByNameAsync(
            string q,
            string? city,
            string? province,
            CancellationToken cancellationToken)
        {
            var keyword = NormalizeSearchTerm(q);
            var stations = Entities
                .Where(station => station.IsActive && station.DeletedAt == null)
                .Where(station => NormalizeSearchTerm(station.Name).Contains(keyword));

            if (!string.IsNullOrWhiteSpace(city))
            {
                var cityFilter = city.Trim();
                stations = stations.Where(station => station.City == cityFilter);
            }

            if (!string.IsNullOrWhiteSpace(province))
            {
                var provinceFilter = province.Trim();
                stations = stations.Where(station => station.Province == provinceFilter);
            }

            return Task.FromResult<IReadOnlyList<Station>>(stations.ToList());
        }

        public void Remove(Station entity) => Entities.Remove(entity);

        public void Update(Station entity) { }

        private static string NormalizeSearchTerm(string value)
        {
            var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);

            foreach (var character in normalized)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(character);
                if (category != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(character);
                }
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }
    }

    private sealed class SpyStationRepository : FakeStationRepository
    {
        public SpyStationRepository(IReadOnlyCollection<Station> stations)
            : base(stations)
        {
        }

        public string? LastQuery { get; private set; }

        public string? LastCity { get; private set; }

        public string? LastProvince { get; private set; }

        public override Task<IReadOnlyList<Station>> SearchActiveByNameAsync(
            string q,
            string? city,
            string? province,
            CancellationToken cancellationToken)
        {
            LastQuery = q;
            LastCity = city;
            LastProvince = province;

            return base.SearchActiveByNameAsync(q, city, province, cancellationToken);
        }
    }

    private sealed class FakeOperatorStationRepository : IOperatorStationRepository
    {
        public FakeOperatorStationRepository(IReadOnlyCollection<OperatorStation> operatorStations)
        {
            Entities = operatorStations.ToList();
        }

        public List<OperatorStation> Entities { get; }

        public Task<OperatorStation> AddAsync(OperatorStation entity, CancellationToken ct)
        {
            Entities.Add(entity);
            return Task.FromResult(entity);
        }

        public Task<OperatorStation?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Entities.FirstOrDefault(station => station.Id == id));

        public IQueryable<OperatorStation> Query() => Entities.AsQueryable();

        public IQueryable<OperatorStation> QueryNoTracking() => Entities.AsQueryable();

        public void Remove(OperatorStation entity) => Entities.Remove(entity);

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
}
