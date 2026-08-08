using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels.Reports;
using VietRide.Shared.Web.Authentication;

namespace VietRide.Parcel.IntegrationTests;

public sealed class ParcelReportEndpointTests
    : IClassFixture<ParcelReportWebApplicationFactory>
{
    private static readonly Guid OperatorId =
        Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private readonly ParcelReportWebApplicationFactory factory;

    public ParcelReportEndpointTests(ParcelReportWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task ParcelReportSummary_ReturnsCanonicalPaymentFieldsWithoutLegacyMoneyFields()
    {
        factory.Mediator.FailPayment = false;
        using var client = CreateAuthenticatedClient();

        using var response = await client.GetAsync(
            "/v1/operator/parcels/reports/summary?from=2026-07-01&to=2026-07-31");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var document = JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");
        data.GetProperty("grossParcelRevenueVnd").GetInt64().Should().Be(1_000);
        data.GetProperty("parcelRefundsVnd").GetInt64().Should().Be(-250);
        data.GetProperty("netParcelRevenueVnd").GetInt64().Should().Be(750);
        data.TryGetProperty("totalRevenue", out _).Should().BeFalse();
        data.TryGetProperty("totalRefunded", out _).Should().BeFalse();

        var query = factory.Mediator.LastRequest.Should()
            .BeOfType<GetParcelReportSummaryQuery>().Subject;
        query.OperatorId.Should().Be(OperatorId);
        query.From.Should().Be(new DateOnly(2026, 7, 1));
        query.To.Should().Be(new DateOnly(2026, 7, 31));
    }

    [Fact]
    public async Task ParcelReportSummary_WhenPaymentUnavailable_Returns503WithoutData()
    {
        factory.Mediator.FailPayment = true;
        using var client = CreateAuthenticatedClient();

        using var response = await client.GetAsync(
            "/v1/operator/parcels/reports/summary?from=2026-07-01&to=2026-07-31");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, body);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("error").GetProperty("code")
            .GetString().Should().Be("UPSTREAM_UNAVAILABLE");
        document.RootElement.TryGetProperty("data", out _).Should().BeFalse();
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            InternalJwtAuthenticationExtensions.HeaderName,
            $"Bearer {CreateJwt()}");
        return client;
    }

    private static string CreateJwt()
    {
        var now = DateTime.UtcNow;
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-secret-at-least-32-chars-long-xxxxx")),
            SecurityAlgorithms.HmacSha256);
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "vietride-gateway",
            audience: "vietride-internal",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
                new Claim("role", "OPERATOR_ADMIN"),
                new Claim("operatorId", OperatorId.ToString()),
            ],
            notBefore: now.AddSeconds(-5),
            expires: now.AddMinutes(15),
            signingCredentials: credentials));
    }
}

public sealed class ParcelReportWebApplicationFactory : VietRideWebApplicationFactory
{
    public ParcelReportMediator Mediator { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMediator>();
            services.RemoveAll<ISender>();
            services.RemoveAll<IPublisher>();
            services.AddSingleton<IMediator>(Mediator);
            services.AddSingleton<ISender>(Mediator);
            services.AddSingleton<IPublisher>(Mediator);
        });
    }
}

public sealed class ParcelReportMediator : IMediator
{
    public bool FailPayment { get; set; }
    public object? LastRequest { get; private set; }

    public Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        if (FailPayment)
        {
            throw new ParcelDependencyUnavailableException(
                "UPSTREAM_UNAVAILABLE",
                "Payment revenue summary is temporarily unavailable.");
        }

        var query = request as GetParcelReportSummaryQuery
            ?? throw new NotSupportedException(request.GetType().Name);
        object response = new ParcelReportSummaryResponse(
            query.OperatorId,
            query.From!.Value,
            query.To!.Value,
            9,
            8,
            7,
            1,
            2,
            1_000,
            -250,
            750,
            "ParcelStats");
        return Task.FromResult((TResponse)response);
    }

    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(request.GetType().Name);

    public Task Publish(object notification, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task Publish<TNotification>(
        TNotification notification,
        CancellationToken cancellationToken = default)
        where TNotification : INotification
        => Task.CompletedTask;

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default)
        => EmptyStream<TResponse>();

    public IAsyncEnumerable<object?> CreateStream(
        object request,
        CancellationToken cancellationToken = default)
        => EmptyStream<object?>();

    private static async IAsyncEnumerable<T> EmptyStream<T>()
    {
        await Task.CompletedTask;
        yield break;
    }
}
