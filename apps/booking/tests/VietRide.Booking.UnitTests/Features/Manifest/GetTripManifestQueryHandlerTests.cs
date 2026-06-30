using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.Manifest.GetTripManifest;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.UnitTests.Features.Manifest;

public sealed class GetTripManifestQueryHandlerTests
{
    private static readonly Guid TripId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid OperatorId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid DriverUserId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid AssistantUserId = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid FirstStopId = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid SecondStopId = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid OriginStationId = Guid.Parse("77777777-7777-4777-8777-777777777777");

    private readonly IBookingRepository _bookings = Substitute.For<IBookingRepository>();
    private readonly ITripServiceClient _tripServiceClient = Substitute.For<ITripServiceClient>();

    [Fact]
    public async Task Handle_ConfirmedBookings_OrdersTerminalFirstThenPickupStopOrder()
    {
        var terminal = CreateConfirmedBooking("VR-20260518-ABCD1234", null, "T01");
        var secondStop = CreateConfirmedBooking("VR-20260518-ABCD1235", SecondStopId, "B01");
        var firstStop = CreateConfirmedBooking("VR-20260518-ABCD1236", FirstStopId, "A01");
        Arrange([secondStop, terminal, firstStop], CreateTripSnapshot(DriverUserId));

        var result = await CreateHandler().Handle(
            new GetTripManifestQuery(TripId, DriverUserId),
            CancellationToken.None);

        result.Items.Select(item => item.SeatNumber)
            .Should().Equal("T01", "A01", "B01");
        result.Items[0].PickupStop.Should().BeNull();
        result.Items[1].PickupStop.Should().Be(FirstStopId);
    }

    [Fact]
    public async Task Handle_AssistantAssignedToTrip_ReturnsManifest()
    {
        var booking = CreateConfirmedBooking("VR-20260518-ABCD1234", FirstStopId, "A01");
        Arrange([booking], CreateTripSnapshot(AssistantUserId));

        var result = await CreateHandler().Handle(
            new GetTripManifestQuery(TripId, AssistantUserId),
            CancellationToken.None);

        result.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_CallerNotAssignedToTrip_ThrowsForbidden()
    {
        Arrange([], CreateTripSnapshot(DriverUserId));

        var act = () => CreateHandler().Handle(
            new GetTripManifestQuery(TripId, Guid.NewGuid()),
            CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>()
            .Where(exception => exception.ErrorCode == "FORBIDDEN");
    }

    [Fact]
    public async Task Handle_NoConfirmedBookings_ReturnsEmptyManifest()
    {
        var pending = CreatePendingBooking("VR-20260518-ABCD1234", FirstStopId, "A01");
        Arrange([pending], CreateTripSnapshot(DriverUserId));

        var result = await CreateHandler().Handle(
            new GetTripManifestQuery(TripId, DriverUserId),
            CancellationToken.None);

        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ItemSerialization_ContainsExactlyFourOperationalFields()
    {
        var booking = CreateConfirmedBooking("VR-20260518-ABCD1234", FirstStopId, "A01");
        Arrange([booking], CreateTripSnapshot(DriverUserId));

        var result = await CreateHandler().Handle(
            new GetTripManifestQuery(TripId, DriverUserId),
            CancellationToken.None);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(
            result.Items.Single(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var properties = document.RootElement.EnumerateObject().Select(property => property.Name);

        properties.Should().BeEquivalentTo(
            ["seatNumber", "bookingCode", "pickupStop", "boardingStatus"]);
        document.RootElement.EnumerateObject().Should().HaveCount(4);
        JsonSerializer.Serialize(result).Should().NotContainAny(
            "passengerUserId",
            "fullName",
            "phoneNumber",
            "idNumber");
    }

    private GetTripManifestQueryHandler CreateHandler()
        => new(_bookings, _tripServiceClient);

    private void Arrange(IReadOnlyList<BookingEntity> bookings, TripSnapshot trip)
    {
        _bookings.QueryNoTracking().Returns(bookings.AsQueryable());
        _tripServiceClient.GetTripSnapshotAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(trip);
    }

    private static BookingEntity CreateConfirmedBooking(
        string bookingCode,
        Guid? pickupStopId,
        string seatNumber)
    {
        var booking = CreatePendingBooking(bookingCode, pickupStopId, seatNumber);
        booking.Confirm(DateTimeOffset.UtcNow);
        return booking;
    }

    private static BookingEntity CreatePendingBooking(
        string bookingCode,
        Guid? pickupStopId,
        string seatNumber)
    {
        var booking = BookingEntity.CreatePendingPayment(
            bookingCode: BookingCode.Parse(bookingCode),
            passengerUserId: Guid.NewGuid(),
            tripId: TripId,
            operatorId: OperatorId,
            pickupStationId: pickupStopId is null ? OriginStationId : null,
            pickupStopId: pickupStopId,
            dropoffStationId: Guid.NewGuid(),
            dropoffStopId: null,
            baseFare: Money.FromRaw(200_000),
            discountAmount: Money.Zero,
            totalAmount: Money.FromRaw(200_000));
        booking.AddPassenger(seatNumber);
        return booking;
    }

    private static TripSnapshot CreateTripSnapshot(Guid assignedUserId)
        => new(
            TripId,
            OperatorId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "SCHEDULED",
            DateTimeOffset.UtcNow.AddHours(1),
            DateTimeOffset.UtcNow.AddHours(5),
            200_000,
            new TripStationSnapshot(OriginStationId, "Origin"),
            new TripStationSnapshot(Guid.NewGuid(), "Destination"),
            [
                new TripStopSnapshot(
                    SecondStopId,
                    2,
                    true,
                    true,
                    DateTimeOffset.UtcNow.AddHours(3),
                    20,
                    null),
                new TripStopSnapshot(
                    FirstStopId,
                    1,
                    true,
                    true,
                    DateTimeOffset.UtcNow.AddHours(2),
                    10,
                    null),
            ],
            new TripSeatSummary(40, 37),
            DriverUserId: assignedUserId == DriverUserId ? assignedUserId : null,
            AssistantUserId: assignedUserId == AssistantUserId ? assignedUserId : null);
}
