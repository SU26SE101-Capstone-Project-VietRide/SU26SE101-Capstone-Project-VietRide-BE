using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;

namespace VietRide.Trip.IntegrationTests.Internal.Trips;

public sealed class InternalTripsAuthTests : IClassFixture<InternalTripsWebApplicationFactory>
{
    private readonly InternalTripsWebApplicationFactory factory;

    public InternalTripsAuthTests(InternalTripsWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    public static IEnumerable<object[]> InternalTripRequests()
    {
        var tripId = Guid.NewGuid();
        yield return [HttpMethod.Get, $"/internal/v1/trips/{tripId}", null!];
        yield return [HttpMethod.Post, "/internal/v1/trips/summaries/batch", JsonContent.Create(new { tripIds = new[] { tripId } })];
        yield return [HttpMethod.Post, $"/internal/v1/trips/{tripId}/lock-seats", JsonContent.Create(new { seatNumbers = new[] { "A01" }, holdOwnerId = Guid.NewGuid(), ttlSeconds = 60 })];
        yield return [HttpMethod.Post, $"/internal/v1/trips/{tripId}/release-seats", JsonContent.Create(new { seatLockToken = Guid.NewGuid(), seatNumbers = new[] { "A01" } })];
        yield return [HttpMethod.Post, $"/internal/v1/trips/{tripId}/book-seats", JsonContent.Create(new { seatLockToken = Guid.NewGuid(), bookingId = Guid.NewGuid(), passengerSeatAssignments = new[] { new { passengerId = Guid.NewGuid(), seatNumber = "A01" } } })];
    }

    [Theory]
    [MemberData(nameof(InternalTripRequests))]
    public async Task InternalTripEndpoint_MissingInternalJwt_Returns401(HttpMethod method, string path, HttpContent? content)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(method, path) { Content = content };

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(InternalTripRequests))]
    public async Task InternalTripEndpoint_TamperedInternalJwt_Returns401(HttpMethod method, string path, HttpContent? content)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", "Bearer not-a-valid-jwt");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LockSeats_MissingIdempotencyKey_Returns422ValidationError()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/trips/00000000-0000-0000-0000-000000000001/lock-seats")
        {
            Content = JsonContent.Create(new { seatNumbers = new[] { "A01" }, holdOwnerId = Guid.NewGuid(), ttlSeconds = 60 }),
        };
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", CreateInternalJwt());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task BatchTripSummaries_MissingInternalJwt_Returns401AuthTokenInvalid()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/internal/v1/trips/summaries/batch",
            new { tripIds = new[] { Guid.NewGuid() } });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("error").GetProperty("code").GetString().Should().Be("AUTH_TOKEN_INVALID");
    }

    [Fact]
    public async Task BatchTripSummaries_TamperedInternalJwt_Returns401AuthTokenInvalid()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/trips/summaries/batch")
        {
            Content = JsonContent.Create(new { tripIds = new[] { Guid.NewGuid() } }),
        };
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", "Bearer not-a-valid-jwt");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("error").GetProperty("code").GetString().Should().Be("AUTH_TOKEN_INVALID");
    }

    [Fact]
    public async Task BatchTripSummaries_InvalidBatchReturns422WithoutIdempotencyKey()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/trips/summaries/batch")
        {
            Content = JsonContent.Create(new { tripIds = Array.Empty<Guid>() }),
        };
        request.Headers.TryAddWithoutValidation("X-Internal-Auth", CreateInternalJwt());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
    }

    private static string CreateInternalJwt()
    {
        var secret = InternalTripsWebApplicationFactory.TestSecret;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims: [new Claim("sub", Guid.NewGuid().ToString())],
            expires: DateTime.UtcNow.AddMinutes(2),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public sealed class InternalTripsWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestSecret = "test-secret-at-least-32-chars-long-xxxxx";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("INTERNAL_JWT_SECRET", TestSecret);
        builder.UseSetting("Trip:BackgroundWorkers:Enabled", "false");
        builder.UseSetting(
            "ConnectionStrings:Default",
            global::VietRide.Trip.IntegrationTests.VietRideWebApplicationFactory.ResolveConnectionString("postgres"));
        builder.UseEnvironment("Testing");
    }
}
