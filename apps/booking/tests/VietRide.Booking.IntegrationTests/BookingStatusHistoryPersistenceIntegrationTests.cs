using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Domain.Constants;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Domain.ValueObjects;
using VietRide.Booking.Infrastructure;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.IntegrationTests;

[Collection(VoucherPersistenceCollection.CollectionName)]
public sealed class BookingStatusHistoryPersistenceIntegrationTests
    : IClassFixture<VoucherPersistenceIntegrationTests.DbBackedVoucherFactory>
{
    private readonly VoucherPersistenceIntegrationTests.DbBackedVoucherFactory _factory;

    public BookingStatusHistoryPersistenceIntegrationTests(
        VoucherPersistenceIntegrationTests.DbBackedVoucherFactory factory) => _factory = factory;

    [Fact]
    public async Task Repository_RoundTripsInDeterministicOrder_AndHasInsertReadOnlySurface()
    {
        await _factory.InitializeAsync();
        var now = new DateTimeOffset(2026, 7, 11, 3, 0, 0, TimeSpan.Zero);
        var booking = CreateBooking(now);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IBookingStatusHistoryRepository>();
            var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
            await db.Bookings.AddAsync(booking);
            await repository.AddAsync(BookingStatusHistory.Create(
                booking.Id, BookingStatus.CONFIRMED, now.AddMinutes(1),
                BookingStatusHistorySource.ConfirmOnPayment));
            await repository.AddAsync(BookingStatusHistory.Create(
                booking.Id, BookingStatus.PENDING_PAYMENT, now,
                BookingStatusHistorySource.CreateBooking, booking.PassengerUserId));
            await db.SaveChangesAsync();
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IBookingStatusHistoryRepository>();
            var rows = await repository.QueryNoTracking()
                .Where(history => history.BookingId == booking.Id)
                .ToListAsync();

            rows.Select(history => history.Status).Should().Equal(
                BookingStatus.PENDING_PAYMENT, BookingStatus.CONFIRMED);
            rows.Should().OnlyContain(history =>
                scope.ServiceProvider.GetRequiredService<BookingDbContext>()
                    .Entry(history).State == EntityState.Detached);
        }

        typeof(IBookingStatusHistoryRepository).GetMethods().Select(method => method.Name)
            .Should().BeEquivalentTo(nameof(IBookingStatusHistoryRepository.AddAsync),
                nameof(IBookingStatusHistoryRepository.QueryNoTracking));
    }

    [Fact]
    public async Task ForeignKeyRestrictsBookingDelete_AndTransactionRollbackRemovesTransitionAndHistory()
    {
        await _factory.InitializeAsync();
        var now = new DateTimeOffset(2026, 7, 11, 4, 0, 0, TimeSpan.Zero);
        var booking = CreateBooking(now);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IBookingStatusHistoryRepository>();
            var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
            await db.Bookings.AddAsync(booking);
            await repository.AddAsync(BookingStatusHistory.Create(
                booking.Id, BookingStatus.PENDING_PAYMENT, now,
                BookingStatusHistorySource.CreateBooking, booking.PassengerUserId));
            await db.SaveChangesAsync();

            var delete = () => db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM vietride_booking.bookings WHERE id = {booking.Id}");
            await delete.Should().ThrowAsync<PostgresException>()
                .Where(exception => exception.SqlState == "23503");
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IBookingStatusHistoryRepository>();
            var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var persisted = await db.Bookings.SingleAsync(item => item.Id == booking.Id);
            persisted.Confirm(now.AddMinutes(2));
            await repository.AddAsync(BookingStatusHistory.Create(
                booking.Id, BookingStatus.CONFIRMED, now.AddMinutes(2),
                BookingStatusHistorySource.ConfirmOnPayment));
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IBookingStatusHistoryRepository>();
            var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
            (await db.Bookings.SingleAsync(item => item.Id == booking.Id)).Status
                .Should().Be(BookingStatus.PENDING_PAYMENT);
            (await repository.QueryNoTracking().CountAsync(history => history.BookingId == booking.Id))
                .Should().Be(1, "a failed lifecycle transaction must not leave history or duplicate replay rows");
        }
    }

    private static Domain.Entities.Booking CreateBooking(DateTimeOffset now)
        => Domain.Entities.Booking.CreatePendingPayment(
            BookingCode.Generate(now), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), null, null, null, Money.FromRaw(100_000), Money.Zero,
            Money.FromRaw(100_000));
}
