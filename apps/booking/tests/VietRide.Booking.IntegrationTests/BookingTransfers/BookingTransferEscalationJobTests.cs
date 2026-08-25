using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.BookingTransfers.EscalatePendingTransfers;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Infrastructure;
using VietRide.Booking.IntegrationTests.Messaging;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Messaging.Outbox;
using VietRide.Shared.Persistence.Outbox;
using VietRide.Shared.Persistence.UnitOfWork;

namespace VietRide.Booking.IntegrationTests.BookingTransfers;

public sealed class BookingTransferEscalationJobTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-25T03:00:00Z");

    [Fact]
    public async Task EscalatesOnlyExpiredPendingGroupsAndDoesNotEmitTwice()
    {
        await TripVehicleSubstitutedConsumerTests.WithDatabaseAsync(
            async (dataSource, oldTripId, operatorId) =>
            {
                var booking = TripVehicleSubstitutedConsumerTests.CreateConfirmedBooking(
                    oldTripId,
                    operatorId,
                    "A01",
                    "A02",
                    "A03",
                    "A04",
                    "A05");
                await TripVehicleSubstitutedConsumerTests.SeedAsync(dataSource, booking);
                var newTripId = Guid.NewGuid();

                await using var db = Day22EventDatabase.CreateDbContext(dataSource, Now);
                var expired = CreateTransfer(
                    booking,
                    passengerIndex: 0,
                    newTripId,
                    BookingTransferConfirmationStatus.PENDING_CONFIRM,
                    Now.AddHours(-3));
                var recent = CreateTransfer(
                    booking,
                    passengerIndex: 1,
                    newTripId,
                    BookingTransferConfirmationStatus.PENDING_CONFIRM,
                    Now.AddHours(-1));
                var notRequired = CreateTransfer(
                    booking,
                    passengerIndex: 2,
                    newTripId,
                    BookingTransferConfirmationStatus.NOT_REQUIRED,
                    Now.AddHours(-3));
                var confirmed = CreateTransfer(
                    booking,
                    passengerIndex: 3,
                    newTripId,
                    BookingTransferConfirmationStatus.PENDING_CONFIRM,
                    Now.AddHours(-3));
                confirmed.Confirm(Guid.NewGuid(), Now.AddHours(-2.5));
                var exactBoundary = CreateTransfer(
                    booking,
                    passengerIndex: 4,
                    newTripId,
                    BookingTransferConfirmationStatus.PENDING_CONFIRM,
                    Now.AddHours(-2));
                db.BookingTransfers.AddRange(expired, recent, notRequired, confirmed, exactBoundary);
                await db.SaveChangesAsync();

                var handler = new EscalatePendingBookingTransfersCommandHandler(
                    CreateTransferRepository(db),
                    Day22EventDatabase.CreateBookingRepository(db),
                    new IntegrationEventOutbox(new OutboxStore(db, new FrozenClock(Now))),
                    new EfUnitOfWork(db),
                    new FrozenClock(Now));

                (await handler.Handle(
                    new EscalatePendingBookingTransfersCommand(),
                    CancellationToken.None)).Should().Be(1);
                (await handler.Handle(
                    new EscalatePendingBookingTransfersCommand(),
                    CancellationToken.None)).Should().Be(0);

                db.ChangeTracker.Clear();
                var persisted = await db.BookingTransfers.AsNoTracking()
                    .ToDictionaryAsync(transfer => transfer.Id);
                persisted[expired.Id].ConfirmationStatus
                    .Should().Be(BookingTransferConfirmationStatus.ESCALATED);
                persisted[recent.Id].ConfirmationStatus
                    .Should().Be(BookingTransferConfirmationStatus.PENDING_CONFIRM);
                persisted[notRequired.Id].ConfirmationStatus
                    .Should().Be(BookingTransferConfirmationStatus.NOT_REQUIRED);
                persisted[confirmed.Id].ConfirmationStatus
                    .Should().Be(BookingTransferConfirmationStatus.CONFIRMED);
                persisted[exactBoundary.Id].ConfirmationStatus
                    .Should().Be(BookingTransferConfirmationStatus.PENDING_CONFIRM);

                var outbox = await db.OutboxEvents.AsNoTracking().SingleAsync(row =>
                    row.EventType == "booking.booking.transfer_escalated");
                using var payload = JsonDocument.Parse(outbox.Payload);
                payload.RootElement.GetProperty("bookingId").GetGuid().Should().Be(booking.Id);
                payload.RootElement.GetProperty("pendingConfirmationCount").GetInt32()
                    .Should().Be(1);
                payload.RootElement.GetProperty("transferIds").EnumerateArray()
                    .Select(item => item.GetGuid()).Should().Equal(expired.Id);
            });
    }

    private static BookingTransfer CreateTransfer(
        VietRide.Booking.Domain.Entities.Booking booking,
        int passengerIndex,
        Guid newTripId,
        BookingTransferConfirmationStatus status,
        DateTimeOffset transferredAt)
    {
        var passenger = booking.Passengers[passengerIndex];
        var ticket = booking.Tickets.Single(item => item.PassengerId == passenger.Id);
        return BookingTransfer.Create(
            booking.Id,
            passenger.Id,
            ticket.Id,
            booking.TripId,
            newTripId,
            passenger.SeatNumber,
            status == BookingTransferConfirmationStatus.NOT_REQUIRED ? null : $"B{passengerIndex + 1:00}",
            status,
            transferredAt,
            Guid.NewGuid());
    }

    private static IBookingTransferRepository CreateTransferRepository(BookingDbContext db)
        => (IBookingTransferRepository)Activator.CreateInstance(
            typeof(BookingDbContext).Assembly.GetType(
                "VietRide.Booking.Infrastructure.Persistence.Repositories.BookingTransferRepository",
                throwOnError: true)!,
            db)!;

    private sealed class FrozenClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
