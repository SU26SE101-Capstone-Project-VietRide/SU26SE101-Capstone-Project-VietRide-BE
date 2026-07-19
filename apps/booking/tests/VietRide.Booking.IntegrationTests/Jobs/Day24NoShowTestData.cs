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
        var seeded = await SeedAsync(db, alongRoute, mixed ? 2 : 1, mixed ? 1 : 0);
        var boardedPassengerId = seeded.BoardedPassengerIds.Count == 0
            ? (Guid?)null
            : seeded.BoardedPassengerIds.Single();
        return (seeded.Booking, seeded.PendingPassengerIds.Single(), boardedPassengerId);
    }

    public static async Task<SeededNoShow> SeedAsync(
        BookingDbContext db,
        bool alongRoute,
        int passengerCount,
        int boardedPassengerCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(passengerCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(passengerCount, 5);
        ArgumentOutOfRangeException.ThrowIfNegative(boardedPassengerCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(boardedPassengerCount, passengerCount);
        var booking = BookingEntity.CreatePendingPayment(
            BookingCode.Generate(DateTimeOffset.UtcNow), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            alongRoute ? null : Guid.NewGuid(), alongRoute ? Guid.NewGuid() : null,
            null, null, Money.FromRaw(100_000), Money.Zero, Money.FromRaw(100_000));
        var passengerIds = new List<Guid>(passengerCount);
        for (var index = 0; index < passengerCount; index++)
        {
            var ticket = booking.AddTicketedPassenger(
                $"A{index + 1:00}", TicketCode.Generate(DateTimeOffset.UtcNow.AddTicks(index)),
                Money.FromRaw(100_000), Money.Zero, Money.FromRaw(100_000));
            passengerIds.Add(ticket.PassengerId);
        }

        booking.Confirm(DateTimeOffset.UtcNow.AddHours(-1));
        foreach (var passengerId in passengerIds.Take(boardedPassengerCount))
        {
            var passenger = booking.Passengers.Single(candidate => candidate.Id == passengerId);
            passenger.MarkBoarded(DateTimeOffset.UtcNow.AddMinutes(-30), booking.PickupStopId);
        }

        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return new SeededNoShow(
            booking,
            passengerIds.Skip(boardedPassengerCount).ToArray(),
            passengerIds.Take(boardedPassengerCount).ToArray());
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

public sealed record SeededNoShow(
    BookingEntity Booking,
    IReadOnlyList<Guid> PendingPassengerIds,
    IReadOnlyList<Guid> BoardedPassengerIds);
