using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Booking.Infrastructure;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.IntegrationTests;

[Collection(VoucherPersistenceCollection.CollectionName)]
public sealed class BookingHistoryShuttleProjectionIntegrationTests
    : IClassFixture<VoucherPersistenceIntegrationTests.DbBackedVoucherFactory>
{
    private readonly VoucherPersistenceIntegrationTests.DbBackedVoucherFactory _factory;

    public BookingHistoryShuttleProjectionIntegrationTests(
        VoucherPersistenceIntegrationTests.DbBackedVoucherFactory factory) => _factory = factory;

    [Fact]
    public async Task PassengerHistory_RoundTripsBookedPointSnapshots()
    {
        await _factory.InitializeAsync();
        var userId = Guid.NewGuid();
        var pickupStopId = Guid.NewGuid();
        var dropoffStationId = Guid.NewGuid();
        var pickupPlannedAt = DateTimeOffset.Parse("2026-09-10T02:15:00Z");
        var dropoffPlannedAt = DateTimeOffset.Parse("2026-09-10T05:45:00Z");
        var pickupPoint = new BookingPointSnapshot(
            BookingPointSnapshot.StopType,
            pickupStopId,
            "C",
            "12 Nguyen Hue",
            pickupPlannedAt);
        var dropoffPoint = new BookingPointSnapshot(
            BookingPointSnapshot.StationType,
            dropoffStationId,
            "D",
            "45 Le Loi",
            dropoffPlannedAt);
        var booking = Domain.Entities.Booking.CreatePendingPayment(
            BookingCode.Generate(DateTimeOffset.UtcNow),
            userId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            pickupStopId,
            dropoffStationId,
            null,
            Money.FromRaw(100_000),
            Money.Zero,
            Money.FromRaw(100_000),
            pickupPointSnapshot: pickupPoint,
            dropoffPointSnapshot: dropoffPoint);

        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<BookingDbContext>();
            await db.Bookings.AddAsync(booking);
            await db.SaveChangesAsync();
        }

        await using var readScope = _factory.Services.CreateAsyncScope();
        var repository = readScope.ServiceProvider.GetRequiredService<IBookingRepository>();
        var page = await repository.ListPassengerHistoryAsync(
            userId,
            null,
            null,
            null,
            1,
            20,
            CancellationToken.None,
            includeShuttleRequests: false);

        var persisted = page.Items.Should().ContainSingle().Subject;
        persisted.PickupPointTypeSnapshot.Should().Be(BookingPointSnapshot.StopType);
        persisted.PickupPointIdSnapshot.Should().Be(pickupStopId);
        persisted.PickupPointNameSnapshot.Should().Be("C");
        persisted.PickupPointAddressSnapshot.Should().Be("12 Nguyen Hue");
        persisted.PickupPointPlannedAtSnapshot.Should().Be(pickupPlannedAt);
        persisted.DropoffPointTypeSnapshot.Should().Be(BookingPointSnapshot.StationType);
        persisted.DropoffPointIdSnapshot.Should().Be(dropoffStationId);
        persisted.DropoffPointNameSnapshot.Should().Be("D");
        persisted.DropoffPointAddressSnapshot.Should().Be("45 Le Loi");
        persisted.DropoffPointPlannedAtSnapshot.Should().Be(dropoffPlannedAt);
    }

    [Fact]
    public async Task PassengerHistory_ConditionallyLoadsShuttleIntentsWithoutChangingBookingPagination()
    {
        await _factory.InitializeAsync();
        var userId = Guid.NewGuid();
        var withShuttle = CreateBooking(userId, withShuttle: true);
        var withoutShuttle = CreateBooking(userId, withShuttle: false);

        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<BookingDbContext>();
            await db.Bookings.AddRangeAsync(withShuttle, withoutShuttle);
            await db.SaveChangesAsync();
        }

        _factory.SqlCapture.Clear();
        await using (var publicScope = _factory.Services.CreateAsyncScope())
        {
            var repository = publicScope.ServiceProvider.GetRequiredService<IBookingRepository>();
            var page = await repository.ListPassengerHistoryAsync(
                userId,
                null,
                null,
                null,
                1,
                20,
                CancellationToken.None,
                includeShuttleRequests: true);

            page.TotalItems.Should().Be(2);
            page.Items.Should().HaveCount(2);
            page.Items.Single(booking => booking.Id == withShuttle.Id)
                .ShuttleIntents.Should().HaveCount(2);
            page.Items.Single(booking => booking.Id == withoutShuttle.Id)
                .ShuttleIntents.Should().BeEmpty();
            AssertOperationalSeatLoaded(page.Items.Single(booking => booking.Id == withShuttle.Id));
        }

        _factory.SqlCapture.Commands.Should().Contain(command =>
            command.Contains("booking_shuttle_intents", StringComparison.OrdinalIgnoreCase));

        _factory.SqlCapture.Clear();
        await using (var internalScope = _factory.Services.CreateAsyncScope())
        {
            var repository = internalScope.ServiceProvider.GetRequiredService<IBookingRepository>();
            var page = await repository.ListPassengerHistoryAsync(
                userId,
                null,
                null,
                null,
                1,
                20,
                CancellationToken.None,
                includeShuttleRequests: false);

            page.TotalItems.Should().Be(2);
            page.Items.Should().HaveCount(2).And.OnlyContain(booking => booking.ShuttleIntents.Count == 0);
            AssertOperationalSeatLoaded(page.Items.Single(booking => booking.Id == withShuttle.Id));
        }

        _factory.SqlCapture.Commands.Should().NotContain(command =>
            command.Contains("booking_shuttle_intents", StringComparison.OrdinalIgnoreCase));
    }

    private static Domain.Entities.Booking CreateBooking(Guid userId, bool withShuttle)
    {
        var booking = Domain.Entities.Booking.CreatePendingPayment(
            BookingCode.Generate(DateTimeOffset.UtcNow),
            userId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            null,
            Money.FromRaw(100_000),
            Money.Zero,
            Money.FromRaw(100_000));
        if (withShuttle)
        {
            booking.RequestShuttle(
                BookingShuttleIntent.InboundDirection,
                "12 Nguyen Hue",
                10.7731m,
                106.7032m,
                3_200);
            booking.RequestShuttle(
                BookingShuttleIntent.OutboundDirection,
                "45 Le Loi",
                10.7750m,
                106.7010m,
                4_200);
        }

        booking.AddTicketedPassenger(
            "A01",
            TicketCode.Generate(DateTimeOffset.UtcNow),
            Money.FromRaw(100_000),
            Money.Zero,
            Money.FromRaw(100_000));
        booking.Passengers.Single().ApplyVehicleSubstitutionSeat("A10");

        return booking;
    }

    private static void AssertOperationalSeatLoaded(Domain.Entities.Booking booking)
    {
        var ticket = booking.Tickets.Should().ContainSingle().Subject;
        var passenger = booking.Passengers.Should().ContainSingle().Subject;
        ticket.PassengerId.Should().Be(passenger.Id);
        ticket.SeatNumber.Should().Be("A01");
        passenger.SeatNumber.Should().Be("A10");
    }
}
