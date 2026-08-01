using System.Net;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using VietRide.Booking.Application.Features.OperatorBookings.GetOperatorBookingDetail;
using VietRide.Booking.Application.Features.OperatorBookings.ListOperatorBookings;

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

    [Fact]
    public async Task ListAndDetail_SerializeSameAdditiveBuyerInsideApiEnvelope()
    {
        var bookingId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var createdAt = DateTimeOffset.Parse("2026-07-29T01:00:00Z");
        var buyer = new OperatorBookingBuyerDto(
            buyerId,
            "Buyer Account",
            "0900000000",
            "buyer@example.test",
            "https://example.test/avatar.jpg");
        var trip = new OperatorBookingTripDto(
            "HCM - Đà Lạt",
            "Hồ Chí Minh",
            "Đà Lạt",
            createdAt.AddDays(1),
            createdAt.AddDays(1));
        _factory.BookingRepository.ListOperatorBookingsAsync(
                Arg.Is<OperatorBookingListCriteria>(criteria => criteria.OperatorId == OperatorId),
                Arg.Any<CancellationToken>())
            .Returns(new OperatorBookingListPage(
                [new OperatorBookingListItem(
                    bookingId,
                    "VR-20260729-BUYER01",
                    tripId,
                    "CONFIRMED",
                    trip,
                    1,
                    100_000,
                    createdAt,
                    buyer,
                    buyerId)],
                1));
        _factory.BookingRepository.GetOperatorBookingDetailAsync(
                bookingId,
                OperatorId,
                Arg.Any<CancellationToken>())
            .Returns(new OperatorBookingDetailDto(
                bookingId,
                "VR-20260729-BUYER01",
                buyerId,
                tripId,
                "CONFIRMED",
                trip,
                1,
                100_000,
                0,
                100_000,
                Guid.NewGuid(),
                null,
                null,
                null,
                null,
                null,
                null,
                createdAt,
                [],
                [],
                buyer));

        using var client = _factory.CreateAuthenticatedClient("OPERATOR_ADMIN", OperatorId);
        using var listResponse = await client.GetAsync("/v1/operator/bookings?page=1&pageSize=20");
        using var detailResponse = await client.GetAsync($"/v1/operator/bookings/{bookingId:D}");

        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var listJson = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        using var detailJson = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        listJson.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        detailJson.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        var listBuyer = listJson.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("buyer");
        var detailBuyer = detailJson.RootElement.GetProperty("data").GetProperty("buyer");
        listBuyer.GetRawText().Should().Be(detailBuyer.GetRawText());
        listBuyer.GetProperty("userId").GetGuid().Should().Be(buyerId);
        listBuyer.GetProperty("displayName").GetString().Should().Be("Buyer Account");
        listBuyer.GetProperty("phone").GetString().Should().Be("0900000000");
        listBuyer.GetProperty("email").GetString().Should().Be("buyer@example.test");
        listBuyer.GetProperty("avatarUrl").GetString().Should().Be("https://example.test/avatar.jpg");
        await _factory.IdentityUsers.DidNotReceive().GetUsersAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(),
            Arg.Any<CancellationToken>());
    }
}
