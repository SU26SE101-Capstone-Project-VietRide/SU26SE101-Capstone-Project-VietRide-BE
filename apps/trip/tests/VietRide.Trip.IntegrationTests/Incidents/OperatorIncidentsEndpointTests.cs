using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Features.Incidents.OperatorIncidents;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.IntegrationTests.Incidents;

public sealed class OperatorIncidentsEndpointTests
{
    private const string TestSecret = "test-secret-at-least-32-chars-long-xxxxx";

    [Theory]
    [InlineData("OPERATOR_ADMIN")]
    [InlineData("OPERATOR_STAFF")]
    public async Task List_OperatorRole_ReturnsEnvelopeAndDispatchesJwtTenant(string role)
    {
        var operatorId = Guid.NewGuid();
        var incident = CreateDto();
        var mediator = new StubMediator(_ => PagedResult<OperatorIncidentDto>.Create([incident], 1, 20, 1));
        using var factory = new EndpointFactory(mediator);
        using var client = factory.CreateClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            $"/v1/operator/incidents?tripId={incident.Trip.TripId:D}&category=ACCIDENT&status=OPEN&from=2026-08-01&to=2026-08-02&page=1&pageSize=20",
            role,
            operatorId);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        data.GetProperty("items")[0].GetProperty("incidentId").GetGuid().Should().Be(incident.IncidentId);
        data.GetProperty("items")[0].GetProperty("trip").GetProperty("route").GetProperty("name")
            .GetString().Should().Be("HCM - Da Lat");
        var query = mediator.LastRequest.Should().BeOfType<ListOperatorIncidentsQuery>().Subject;
        query.OperatorId.Should().Be(operatorId);
        query.TripId.Should().Be(incident.Trip.TripId);
        query.Category.Should().Be("ACCIDENT");
        query.Status.Should().Be("OPEN");
    }

    [Fact]
    public async Task Detail_MissingIncident_ReturnsIncidentNotFoundEnvelope()
    {
        var mediator = new StubMediator(_ => throw new CodedNotFoundException("INCIDENT_NOT_FOUND", "Incident was not found."));
        using var factory = new EndpointFactory(mediator);
        using var client = factory.CreateClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            $"/v1/operator/incidents/{Guid.NewGuid():D}",
            "OPERATOR_ADMIN",
            Guid.NewGuid());

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("INCIDENT_NOT_FOUND");
    }

    [Fact]
    public async Task List_NonOperatorRole_IsForbiddenBeforeDispatch()
    {
        var mediator = new StubMediator(_ => throw new InvalidOperationException("Must not dispatch."));
        using var factory = new EndpointFactory(mediator);
        using var client = factory.CreateClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            "/v1/operator/incidents",
            "DRIVER",
            Guid.NewGuid());

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        mediator.LastRequest.Should().BeNull();
    }

    [Theory]
    [InlineData("/v1/operator/incidents?status=UNKNOWN")]
    [InlineData("/v1/operator/incidents?category=UNKNOWN")]
    public async Task InvalidFilters_ReturnValidationEnvelope(string path)
    {
        var mediator = new StubMediator(_ => throw new CodedValidationException(
            "VALIDATION_ERROR",
            "Incident filters are invalid."));
        using var factory = new EndpointFactory(mediator);
        using var client = factory.CreateClient();
        using var request = CreateRequest(HttpMethod.Get, path, "OPERATOR_ADMIN", Guid.NewGuid());

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("VALIDATION_ERROR");
        mediator.LastRequest.Should().BeOfType<ListOperatorIncidentsQuery>();
    }

    [Fact]
    public async Task MalformedIncidentId_ReturnsValidationEnvelopeWithoutDispatch()
    {
        var mediator = new StubMediator(_ => throw new InvalidOperationException("Must not dispatch."));
        using var factory = new EndpointFactory(mediator);
        using var client = factory.CreateClient();
        using var request = CreateRequest(
            HttpMethod.Get,
            "/v1/operator/incidents/not-a-uuid",
            "OPERATOR_ADMIN",
            Guid.NewGuid());

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("VALIDATION_ERROR");
        mediator.LastRequest.Should().BeNull();
    }

    private static OperatorIncidentDto CreateDto()
        => new(
            Guid.NewGuid(),
            "ACCIDENT",
            "Minor collision",
            ["https://storage.example/incident.jpg"],
            10.75m,
            106.67m,
            DateTimeOffset.Parse("2026-08-01T03:00:00Z"),
            "OPEN",
            null,
            null,
            null,
            new OperatorIncidentTripDto(
                Guid.NewGuid(),
                "IN_PROGRESS",
                DateTimeOffset.Parse("2026-08-01T01:00:00Z"),
                new OperatorIncidentRouteDto(
                    Guid.NewGuid(),
                    "HCM - Da Lat",
                    new OperatorIncidentStationDto(Guid.NewGuid(), "Mien Dong"),
                    new OperatorIncidentStationDto(Guid.NewGuid(), "Da Lat"))),
            new OperatorIncidentReporterDto(Guid.NewGuid(), "Driver A", "DRIVER"));

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, string role, Guid operatorId)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", "Bearer " + CreateJwt(role, operatorId));
        return request;
    }

    private static string CreateJwt(string role, Guid operatorId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            "vietride-gateway",
            "vietride-internal",
            [
                new Claim("sub", Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, role),
                new Claim("operatorId", operatorId.ToString()),
            ],
            expires: DateTime.UtcNow.AddMinutes(2),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class EndpointFactory(IMediator mediator) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("INTERNAL_JWT_SECRET", TestSecret);
            builder.UseSetting("Trip:BackgroundWorkers:Enabled", "false");
            builder.UseSetting(
                "ConnectionStrings:Default",
                VietRideWebApplicationFactory.ResolveConnectionString("postgres"));
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMediator>();
                services.AddSingleton(mediator);
            });
        }
    }

    private sealed class StubMediator(Func<object, object?> responder) : IMediator
    {
        public object? LastRequest { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            var response = responder(request);
            return Task.FromResult(response is TResponse typed ? typed : default!);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(responder(request));
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
