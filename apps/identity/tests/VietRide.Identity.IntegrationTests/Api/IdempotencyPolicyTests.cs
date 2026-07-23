using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace VietRide.Identity.IntegrationTests.Api;

public sealed class IdempotencyPolicyTests : IClassFixture<VietRideWebApplicationFactory>
{
    private readonly VietRideWebApplicationFactory _factory;

    public IdempotencyPolicyTests(VietRideWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RequiredMutation_WithoutKey_Returns422Required()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/v1/auth/register",
            new { });

        await AssertErrorAsync(
            response,
            HttpStatusCode.UnprocessableEntity,
            "IDEMPOTENCY_KEY_REQUIRED");
    }

    [Fact]
    public async Task RequiredMutation_WithNonUuidV4Key_Returns422ValidationError()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/v1/auth/register")
        {
            Content = JsonContent.Create(new { }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "not-a-uuid-v4");

        var response = await client.SendAsync(request);

        await AssertErrorAsync(
            response,
            HttpStatusCode.UnprocessableEntity,
            "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Login_WithoutKey_RemainsExempt()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/auth/login", new { });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("VALIDATION_ERROR");
    }

    private static async Task AssertErrorAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        response.StatusCode.Should().Be(expectedStatus);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be(expectedCode);
    }
}
