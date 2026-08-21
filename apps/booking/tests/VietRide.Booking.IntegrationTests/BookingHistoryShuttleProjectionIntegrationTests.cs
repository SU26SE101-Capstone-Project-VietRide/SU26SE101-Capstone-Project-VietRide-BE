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

        return booking;
    }
}
