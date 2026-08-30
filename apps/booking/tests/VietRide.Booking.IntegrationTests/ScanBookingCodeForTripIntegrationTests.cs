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
        var response = await client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.Private.Should().BeTrue();
        response.Headers.CacheControl.NoStore.Should().BeTrue();
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
        item.GetProperty("bookingCode").GetString().Should().Be(BookingCodeValue);
        item.GetProperty("buyerName").GetString().Should().Be("Nguyen Van Buyer");
        item.GetProperty("buyerPhone").GetString().Should().Be("+84888151546");
        item.EnumerateObject().Should().HaveCount(8);
    }

    [Fact]
    public async Task GetManifest_AssignedCrew_ReturnsBuyerContactPickupNameAndNoStore()
    {
        _factory.ResetCalls();
        var assistantUserId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var pickupStopId = Guid.NewGuid();
        var booking = CreateConfirmedBooking(tripId, operatorId, pickupStopId);

        _factory.BookingRepository.QueryNoTracking().Returns(new[] { booking }.AsQueryable());
        _factory.TripClient.GetTripSnapshotAsync(tripId, Arg.Any<CancellationToken>())
            .Returns(CreateTripSnapshot(
                tripId,
                operatorId,
                assistantUserId,
                pickupStopId,
                assignedAsAssistant: true));

        using var client = _factory.CreateAuthenticatedClient(assistantUserId, "ASSISTANT");
        using var response = await client.GetAsync($"/v1/bookings/trips/{tripId}/manifest");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.Private.Should().BeTrue();
        response.Headers.CacheControl.NoStore.Should().BeTrue();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = document.RootElement
            .GetProperty("data")
            .GetProperty("items")[0];
        item.GetProperty("seatNumber").GetString().Should().Be("A01");
        item.GetProperty("pickupStop").GetGuid().Should().Be(pickupStopId);
        item.GetProperty("pickupPointName").GetString().Should().Be("Pickup stop");
        item.GetProperty("buyerName").GetString().Should().Be("Nguyen Van Buyer");
        item.GetProperty("buyerPhone").GetString().Should().Be("+84888151546");
    }

    private static BookingEntity CreateConfirmedBooking(
        Guid tripId,
        Guid operatorId,
        Guid? pickupStopId = null)
    {
        var booking = BookingEntity.CreatePendingPayment(
            bookingCode: BookingCode.Parse(BookingCodeValue),
            passengerUserId: Guid.NewGuid(),
            tripId: tripId,
            operatorId: operatorId,
            pickupStationId: pickupStopId.HasValue ? null : Guid.NewGuid(),
            pickupStopId: pickupStopId,
            dropoffStationId: Guid.NewGuid(),
            dropoffStopId: null,
            baseFare: Money.FromRaw(200_000),
            discountAmount: Money.Zero,
            totalAmount: Money.FromRaw(200_000),
            buyerDisplayName: "Nguyen Van Buyer",
            buyerPhone: "+84888151546");
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
        Guid assignedUserId,
        Guid? pickupStopId = null,
        bool assignedAsAssistant = false)
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
            Stops: pickupStopId.HasValue
                ?
                [
                    new TripStopSnapshot(
                        pickupStopId.Value,
                        1,
                        true,
                        true,
                        Now.AddHours(2),
                        10,
                        150_000,
                        Name: "Pickup stop"),
                ]
                : [],
            SeatSummary: new TripSeatSummary(40, 39),
            DriverUserId: assignedAsAssistant ? null : assignedUserId,
            AssistantUserId: assignedAsAssistant ? assignedUserId : null);
}
