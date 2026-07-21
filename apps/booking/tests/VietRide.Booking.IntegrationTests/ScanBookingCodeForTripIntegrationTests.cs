using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.IntegrationTests;

public sealed class ScanBookingCodeForTripIntegrationTests
    : IClassFixture<CancelBookingWebApplicationFactory>
{
    private const string BookingCodeValue = "VR-20260630-ABCD2345";

    private static readonly DateTimeOffset Now = new(2026, 6, 30, 3, 0, 0, TimeSpan.Zero);

    private readonly CancelBookingWebApplicationFactory _factory;

    public ScanBookingCodeForTripIntegrationTests(CancelBookingWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostQrScan_ConfirmedBooking_ReturnsPassengerRecordsInApiEnvelope()
    {
        _factory.ResetCalls();
        var driverUserId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var booking = CreateConfirmedBooking(tripId, operatorId);

        _factory.BookingRepository.FindByBookingCodeAsync(
                BookingCodeValue,
                Arg.Any<CancellationToken>())
            .Returns(booking);
        _factory.BookingRepository.FindByIdWithPassengersAsync(
                booking.Id,
                Arg.Any<CancellationToken>())
            .Returns(booking);
        _factory.TripClient.GetTripSnapshotAsync(tripId, Arg.Any<CancellationToken>())
            .Returns(CreateTripSnapshot(tripId, operatorId, driverUserId));

        var client = _factory.CreateAuthenticatedClient(driverUserId, "DRIVER");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/bookings/trips/{tripId}/boarding/qr-scan")
        {
            Content = JsonContent.Create(new { bookingCode = BookingCodeValue }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString())
            .Should().BeTrue();
        var response = await client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeTrue();
        root.GetProperty("statusCode").GetInt32().Should().Be(200);
        var items = root.GetProperty("data").GetProperty("items");
        items.GetArrayLength().Should().Be(1);
        var item = items[0];
        item.GetProperty("seatNumber").GetString().Should().Be("A01");
        item.GetProperty("boardingStatus").GetString().Should().Be("PENDING");
        item.GetProperty("ticketCode").GetString().Should().StartWith("VT-");
        item.EnumerateObject().Should().HaveCount(5);
    }

    private static BookingEntity CreateConfirmedBooking(Guid tripId, Guid operatorId)
    {
        var booking = BookingEntity.CreatePendingPayment(
            bookingCode: BookingCode.Parse(BookingCodeValue),
            passengerUserId: Guid.NewGuid(),
            tripId: tripId,
            operatorId: operatorId,
            pickupStationId: Guid.NewGuid(),
            pickupStopId: null,
            dropoffStationId: Guid.NewGuid(),
            dropoffStopId: null,
            baseFare: Money.FromRaw(200_000),
            discountAmount: Money.Zero,
            totalAmount: Money.FromRaw(200_000));
        booking.AddTicketedPassenger(
            "A01",
            TicketCode.Generate(Now),
            Money.FromRaw(200_000),
            Money.Zero,
            Money.FromRaw(200_000));
        booking.Confirm(Now.AddMinutes(-10));
        return booking;
    }

    private static TripSnapshot CreateTripSnapshot(
        Guid tripId,
        Guid operatorId,
        Guid driverUserId)
        => new(
            TripId: tripId,
            OperatorId: operatorId,
            RouteId: Guid.NewGuid(),
            VehicleId: Guid.NewGuid(),
            Status: "BOARDING",
            DepartureDateTime: Now.AddHours(1),
            EstimatedArrivalTime: Now.AddHours(5),
            BaseFare: 200_000,
            OriginStation: new TripStationSnapshot(Guid.NewGuid(), "Origin"),
            DestinationStation: new TripStationSnapshot(Guid.NewGuid(), "Destination"),
            Stops: [],
            SeatSummary: new TripSeatSummary(40, 39),
            DriverUserId: driverUserId);
}
