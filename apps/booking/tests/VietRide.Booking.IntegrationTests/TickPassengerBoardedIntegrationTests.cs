using System.Net;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.IntegrationTests;

public sealed class TickPassengerBoardedIntegrationTests
    : IClassFixture<CancelBookingWebApplicationFactory>
{
    private static readonly DateTimeOffset Now = new(2026, 6, 17, 8, 0, 0, TimeSpan.Zero);

    private readonly CancelBookingWebApplicationFactory _factory;

    public TickPassengerBoardedIntegrationTests(CancelBookingWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostTickPassenger_DriverReturns200EnvelopeAndReplaysIdempotently()
    {
        _factory.ResetCalls();
        var driverUserId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var booking = CreateConfirmedBooking(tripId, operatorId);
        var passenger = booking.Passengers.Single();

        _factory.BookingRepository.QueryNoTracking().Returns(new[] { booking }.AsQueryable());
        _factory.BookingRepository.FindByIdWithPassengersAsync(
                booking.Id,
                Arg.Any<CancellationToken>())
            .Returns(booking);
        _factory.TripClient.GetTripSnapshotAsync(tripId, Arg.Any<CancellationToken>())
            .Returns(CreateTripSnapshot(tripId, operatorId, driverUserId));

        var client = _factory.CreateAuthenticatedClient(driverUserId, "DRIVER");
        var idempotencyKey = Guid.NewGuid().ToString();
        using var firstRequest = BuildRequest(tripId, passenger.Id, idempotencyKey);
        using var secondRequest = BuildRequest(tripId, passenger.Id, idempotencyKey);

        var firstResponse = await client.SendAsync(firstRequest);
        var firstJson = await firstResponse.Content.ReadAsStringAsync();
        var secondResponse = await client.SendAsync(secondRequest);
        var secondJson = await secondResponse.Content.ReadAsStringAsync();

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondJson.Should().Be(firstJson);
        passenger.BoardingStatus.Should().Be(PassengerBoardingStatus.BOARDED);
        passenger.BoardedAt.Should().Be(Now);

        using var document = JsonDocument.Parse(firstJson);
        var root = document.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeTrue();
        root.GetProperty("statusCode").GetInt32().Should().Be(200);
        var data = root.GetProperty("data");
        data.GetProperty("passengerRecordId").GetGuid().Should().Be(passenger.Id);
        data.GetProperty("boardingStatus").GetString().Should().Be("BOARDED");
        data.GetProperty("boardedAt").GetDateTimeOffset().Should().Be(Now);
        data.GetProperty("ticketStatus").GetString().Should().Be("USED");

        await _factory.TripClient.Received(1)
            .GetTripSnapshotAsync(tripId, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task PostTickPassenger_MissingOrBlankIdempotencyKey_Returns422WithoutMutation(
        string? idempotencyKey)
    {
        _factory.ResetCalls();
        var driverUserId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var booking = CreateConfirmedBooking(tripId, operatorId);
        var passenger = booking.Passengers.Single();

        _factory.BookingRepository.QueryNoTracking().Returns(new[] { booking }.AsQueryable());
        _factory.BookingRepository.FindByIdWithPassengersAsync(
                booking.Id,
                Arg.Any<CancellationToken>())
            .Returns(booking);
        _factory.TripClient.GetTripSnapshotAsync(tripId, Arg.Any<CancellationToken>())
            .Returns(CreateTripSnapshot(tripId, operatorId, driverUserId));

        var client = _factory.CreateAuthenticatedClient(driverUserId, "DRIVER");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/bookings/trips/{tripId}/boarding/passenger/{passenger.Id}");
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey)
                .Should().BeTrue();
        }

        var response = await client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("statusCode").GetInt32().Should().Be(422);
        root.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("VALIDATION_ERROR");
        passenger.BoardingStatus.Should().Be(PassengerBoardingStatus.PENDING);
        passenger.BoardedAt.Should().BeNull();
        await _factory.TripClient.DidNotReceiveWithAnyArgs()
            .GetTripSnapshotAsync(default, default);
    }

    private static HttpRequestMessage BuildRequest(
        Guid tripId,
        Guid passengerRecordId,
        string idempotencyKey)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/v1/bookings/trips/{tripId}/boarding/passenger/{passengerRecordId}");
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private static BookingEntity CreateConfirmedBooking(Guid tripId, Guid operatorId)
    {
        var booking = BookingEntity.CreatePendingPayment(
            bookingCode: BookingCode.Generate(Now),
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
