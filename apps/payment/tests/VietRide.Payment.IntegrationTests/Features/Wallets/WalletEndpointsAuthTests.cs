using System.Net;
using FluentAssertions;

namespace VietRide.Payment.IntegrationTests.Features.Wallets;

public sealed class WalletEndpointsAuthTests : IClassFixture<VietRideWebApplicationFactory>
{
    private readonly VietRideWebApplicationFactory _factory;

    public WalletEndpointsAuthTests(VietRideWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetWallet_WithoutJwt_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/v1/wallet");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetWalletTransactions_WithoutJwt_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/v1/wallet/transactions");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
