using Microsoft.EntityFrameworkCore;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Booking.Infrastructure;
using VietRide.Shared.Kernel.ValueObjects;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.IntegrationTests.Jobs;

public static class Day24NoShowTestData
{
    public static async Task<(BookingEntity Booking, Guid PendingPassengerId, Guid? BoardedPassengerId)> SeedAsync(
        BookingDbContext db,
        bool alongRoute,
        bool mixed)
    {
        var booking = BookingEntity.CreatePendingPayment(
            BookingCode.Generate(DateTimeOffset.UtcNow), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            alongRoute ? null : Guid.NewGuid(), alongRoute ? Guid.NewGuid() : null,
            null, null, Money.FromRaw(100_000), Money.Zero, Money.FromRaw(100_000));
        var firstTicket = booking.AddTicketedPassenger(
            "A01", TicketCode.Generate(DateTimeOffset.UtcNow),
            Money.FromRaw(100_000), Money.Zero, Money.FromRaw(100_000));
        var secondTicket = mixed
            ? booking.AddTicketedPassenger(
                "A02", TicketCode.Generate(DateTimeOffset.UtcNow),
                Money.FromRaw(100_000), Money.Zero, Money.FromRaw(100_000))
            : null;
        booking.Confirm(DateTimeOffset.UtcNow.AddHours(-1));
        if (secondTicket is not null)
        {
            var second = booking.Passengers.Single(passenger => passenger.Id == secondTicket.PassengerId);
            second.MarkBoarded(DateTimeOffset.UtcNow.AddMinutes(-30), booking.PickupStopId);
        }

        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return (booking, firstTicket.PassengerId, secondTicket?.PassengerId);
    }

    public static TripSnapshot Trip(BookingEntity booking, DateTimeOffset anchor)
    {
        var stops = booking.PickupStopId.HasValue
            ? new[]
            {
                new TripStopSnapshot(
                    booking.PickupStopId.Value, 1, true, true, anchor, 1, 100_000,
                    Status: "ARRIVED", ActualArrivalTime: anchor),
            }
            : [];
        return new TripSnapshot(
            booking.TripId, booking.OperatorId, Guid.NewGuid(), Guid.NewGuid(), "IN_PROGRESS",
            anchor, anchor.AddHours(2), 100_000,
            new TripStationSnapshot(Guid.NewGuid(), "Origin"),
            new TripStationSnapshot(Guid.NewGuid(), "Destination"),
            stops, new TripSeatSummary(10, 0), ActualDepartureTime: anchor);
    }
}
