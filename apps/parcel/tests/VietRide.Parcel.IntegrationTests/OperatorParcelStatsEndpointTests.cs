using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Shared.Web.Authentication;

namespace VietRide.Parcel.IntegrationTests;

public sealed class OperatorParcelStatsEndpointTests
    : IClassFixture<OperatorParcelStatsWebApplicationFactory>
{
    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private readonly OperatorParcelStatsWebApplicationFactory _factory;

    public OperatorParcelStatsEndpointTests(OperatorParcelStatsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task OperatorParcelStats_AdminReturnsEnvelopeAndUsesJwtTenant()
    {
        _factory.Repository.Reset(new OperatorParcelStatsReadResult(
                3,
                [new OperatorParcelStatsBucketReadModel("IN_TRANSIT", null, null, 3)]));
        using var client = CreateAuthenticatedClient("OPERATOR_ADMIN", OperatorId);

        var response = await client.GetAsync(
            "/v1/operator/parcel-stats?from=2026-02-01&to=2026-02-01&groupBy=status&operatorId=bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        var responseBody = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, responseBody);
        using var document = JsonDocument.Parse(responseBody);
        var data = document.RootElement.GetProperty("data");
        data.GetProperty("totalParcels").GetInt64().Should().Be(3);
        var item = data.GetProperty("items")[0];
        item.GetProperty("key").GetString().Should().Be("IN_TRANSIT");
        item.GetProperty("count").GetInt64().Should().Be(3);
        item.TryGetProperty("routeId", out _).Should().BeFalse();

        _factory.Repository.Calls.Should().ContainSingle();
        var call = _factory.Repository.Calls[0];
        call.OperatorId.Should().Be(OperatorId);
        call.FromUtc.Should().Be(DateTimeOffset.Parse("2026-01-31T17:00:00Z"));
        call.ToUtcExclusive.Should().Be(DateTimeOffset.Parse("2026-02-01T17:00:00Z"));
        call.GroupBy.Should().Be("status");
        call.RouteLimit.Should().Be(10);
    }

    [Theory]
    [InlineData(null, null, HttpStatusCode.Unauthorized)]
    [InlineData("PASSENGER", null, HttpStatusCode.Forbidden)]
    [InlineData("OPERATOR_STAFF", "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", HttpStatusCode.Forbidden)]
    [InlineData("OPERATOR_ADMIN", null, HttpStatusCode.Forbidden)]
    public async Task OperatorParcelStats_RejectsAnonymousWrongRoleOrMissingTenant(
        string? role,
        string? operatorId,
        HttpStatusCode expected)
    {
        using var client = role is null
            ? _factory.CreateClient()
            : CreateAuthenticatedClient(role, operatorId is null ? null : Guid.Parse(operatorId));

        var response = await client.GetAsync(
            "/v1/operator/parcel-stats?from=2026-02-01&to=2026-02-01&groupBy=status");

        response.StatusCode.Should().Be(expected);
    }

    [Theory]
    [InlineData("/v1/operator/parcel-stats?to=2026-02-01&groupBy=status")]
    [InlineData("/v1/operator/parcel-stats?from=2026-02-01&to=2026-02-01&groupBy=trip")]
    public async Task OperatorParcelStats_InvalidQueryReturnsValidationEnvelope(string path)
    {
        using var client = CreateAuthenticatedClient("OPERATOR_ADMIN", OperatorId);

        var response = await client.GetAsync(path);
        var responseBody = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, responseBody);
        using var document = JsonDocument.Parse(responseBody);
        document.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
    }

    private HttpClient CreateAuthenticatedClient(string role, Guid? operatorId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            InternalJwtAuthenticationExtensions.HeaderName,
            $"Bearer {CreateJwt(role, operatorId)}");
        return client;
    }

    private static string CreateJwt(string role, Guid? operatorId)
    {
        var now = DateTime.UtcNow;
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-secret-at-least-32-chars-long-xxxxx")),
            SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new("role", role),
        };
        if (operatorId.HasValue)
        {
            claims.Add(new Claim("operatorId", operatorId.Value.ToString()));
        }

        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims: claims,
            notBefore: now.AddSeconds(-5),
            expires: now.AddMinutes(15),
            signingCredentials: credentials));
    }
}

public sealed class OperatorParcelStatsWebApplicationFactory : VietRideWebApplicationFactory
{
    public FakeOperatorParcelStatsRepository Repository { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IOperatorParcelStatsRepository>();
            services.AddSingleton<IOperatorParcelStatsRepository>(Repository);
        });
    }
}

public sealed class FakeOperatorParcelStatsRepository : IOperatorParcelStatsRepository
{
    private readonly List<OperatorParcelStatsRepositoryCall> _calls = [];
    private OperatorParcelStatsReadResult _result = new(0, []);

    public IReadOnlyList<OperatorParcelStatsRepositoryCall> Calls => _calls;

    public void Reset(OperatorParcelStatsReadResult result)
    {
        _calls.Clear();
        _result = result;
    }

    public Task<OperatorParcelStatsReadResult> GetAsync(
        Guid operatorId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtcExclusive,
        string groupBy,
        int routeLimit,
        CancellationToken cancellationToken = default)
    {
        _calls.Add(new OperatorParcelStatsRepositoryCall(
            operatorId,
            fromUtc,
            toUtcExclusive,
            groupBy,
            routeLimit));
        return Task.FromResult(_result);
    }
}

public sealed record OperatorParcelStatsRepositoryCall(
    Guid OperatorId,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtcExclusive,
    string GroupBy,
    int RouteLimit);
