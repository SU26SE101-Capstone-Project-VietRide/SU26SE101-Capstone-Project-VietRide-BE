using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using VietRide.Identity.Application.Features.Passenger.GetPassengerBookings;
using VietRide.Identity.Application.Features.Users.GetMe;
using VietRide.Shared.Application.Pagination;

namespace VietRide.Identity.IntegrationTests.Api;

public sealed class PassengerEndpointsTests : IClassFixture<AuthWebApplicationFactory>
{
    private static readonly Guid CallerUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly AuthWebApplicationFactory _factory;

    public PassengerEndpointsTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetMe_HappyPath_Returns200EnvelopeWithCallerProfile()
    {
        using var client = CreateClientWithSender(new HappyPathPassengerSender());
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/passenger/me");
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateInternalJwt(CallerUserId, "PASSENGER")}");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertSuccessEnvelope(doc, 200);
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("id").GetGuid().Should().Be(CallerUserId);
        data.GetProperty("email").GetString().Should().Be("passenger@example.com");
        data.GetProperty("displayName").GetString().Should().Be("Test Passenger");
        data.GetProperty("phone").GetString().Should().Be("+84901234567");
        data.GetProperty("role").GetString().Should().Be("PASSENGER");
        data.GetProperty("status").GetString().Should().Be("ACTIVE");
    }

    [Fact]
    public async Task GetMe_WithoutAuth_Returns401()
    {
        using var client = CreateClientWithSender(new HappyPathPassengerSender());

        var response = await client.GetAsync("/v1/passenger/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetBookings_HappyPath_Returns200EmptyPaginatedEnvelope()
    {
        using var client = CreateClientWithSender(new HappyPathPassengerSender());
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/passenger/bookings");
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", $"Bearer {CreateInternalJwt(CallerUserId, "PASSENGER")}");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        AssertSuccessEnvelope(doc, 200);
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("items").GetArrayLength().Should().Be(0);
        data.GetProperty("page").GetInt32().Should().Be(1);
        data.GetProperty("pageSize").GetInt32().Should().Be(20);
        data.GetProperty("total").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task GetBookings_WithoutAuth_Returns401()
    {
        using var client = CreateClientWithSender(new HappyPathPassengerSender());

        var response = await client.GetAsync("/v1/passenger/bookings");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private HttpClient CreateClientWithSender(ISender sender)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISender>();
                services.RemoveAll<IMediator>();
                services.AddSingleton(sender);
            });
        }).CreateIdempotentClient();
    }

    private static void AssertSuccessEnvelope(JsonDocument doc, int expectedStatusCode)
    {
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("statusCode").GetInt32().Should().Be(expectedStatusCode);
        doc.RootElement.TryGetProperty("data", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("meta", out _).Should().BeTrue();
    }

    private static string CreateInternalJwt(Guid userId, string role)
    {
        var now = DateTime.UtcNow;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthWebApplicationFactory.InternalJwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim("role", role),
            ],
            notBefore: now.AddSeconds(-5),
            expires: now.AddSeconds(120),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class HappyPathPassengerSender : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
            => Task.FromResult((TResponse)Handle(request));

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => Task.FromResult<object?>(Handle(request));

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Passenger endpoint tests do not use streaming MediatR requests.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Passenger endpoint tests do not use streaming MediatR requests.");

        private static object Handle(object request)
            => request switch
            {
                GetMeQuery query => new GetMeResponseDto(
                    Id: query.UserId,
                    Email: "passenger@example.com",
                    DisplayName: "Test Passenger",
                    Phone: "+84901234567",
                    Role: "PASSENGER",
                    OperatorId: null,
                    Status: "ACTIVE",
                    AvatarUrl: null),

                GetPassengerBookingsQuery => new PagedResult<object>(
                    Items: Array.Empty<object>(),
                    Total: 0,
                    Page: 1,
                    PageSize: 20),

                _ => throw new InvalidOperationException($"Unexpected request type {request.GetType().Name}."),
            };
    }
}
