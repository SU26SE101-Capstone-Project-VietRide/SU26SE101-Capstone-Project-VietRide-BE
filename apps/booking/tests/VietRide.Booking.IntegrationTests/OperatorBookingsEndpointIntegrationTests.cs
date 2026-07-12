using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace VietRide.Booking.IntegrationTests;

public sealed class OperatorBookingsEndpointIntegrationTests
    : IClassFixture<BookingStatsWebApplicationFactory>
{
    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private readonly BookingStatsWebApplicationFactory _factory;

    public OperatorBookingsEndpointIntegrationTests(BookingStatsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetById_WithMalformedId_ReturnsValidationEnvelopeInsteadOfNotFound()
    {
        using var client = _factory.CreateAuthenticatedClient("OPERATOR_STAFF", OperatorId);

        var response = await client.GetAsync("/v1/operator/bookings/not-a-uuid");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
        doc.RootElement.GetProperty("meta").TryGetProperty("traceId", out _).Should().BeTrue();
    }
}
