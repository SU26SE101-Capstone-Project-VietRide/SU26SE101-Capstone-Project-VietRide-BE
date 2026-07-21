using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.OperatorBookings.ListOperatorBookings;
using VietRide.Booking.Domain.Enums;
using VietRide.Booking.Infrastructure;

namespace VietRide.Booking.IntegrationTests;

[Collection(VoucherPersistenceCollection.CollectionName)]
public sealed class OperatorBookingsListRepositoryIntegrationTests
    : IClassFixture<VoucherPersistenceIntegrationTests.DbBackedVoucherFactory>
{
    private readonly VoucherPersistenceIntegrationTests.DbBackedVoucherFactory _factory;

    public OperatorBookingsListRepositoryIntegrationTests(
        VoucherPersistenceIntegrationTests.DbBackedVoucherFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task List_AppliesTenantToItemsAndCount_AllFiltersProjectionAndPastEndPage()
    {
        await _factory.InitializeAsync();
        var owner = Guid.NewGuid();
        var foreignOwner = Guid.NewGuid();
        var trip = Guid.NewGuid();
        var passenger = Guid.NewGuid();
        var departure = new DateTimeOffset(2026, 7, 10, 17, 0, 0, TimeSpan.Zero);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
            await InsertBooking(db, Guid.NewGuid(), "VR-20260711-OWNER001", owner, passenger, trip,
                "CONFIRMED", departure, 120_000, new DateTimeOffset(2026, 7, 1, 1, 0, 0, TimeSpan.Zero));
            await InsertBooking(db, Guid.NewGuid(), "VR-20260711-FOREIGN", foreignOwner, passenger, trip,
                "CONFIRMED", departure, 999_000, new DateTimeOffset(2026, 7, 2, 1, 0, 0, TimeSpan.Zero));
        }

        await using var readScope = _factory.Services.CreateAsyncScope();
        var repository = readScope.ServiceProvider.GetRequiredService<IBookingRepository>();
        var criteria = new OperatorBookingListCriteria(
            owner, [BookingStatus.CONFIRMED], trip, departure, departure.AddDays(1), passenger,
                "vr-20260711-owner001", 1, 20, "totalAmount", false);

        _factory.SqlCapture.Clear();
        var result = await repository.ListOperatorBookingsAsync(criteria);

        var listSql = _factory.SqlCapture.Commands.Should().HaveCount(2).And.Subject;
        listSql.Should().ContainSingle(sql =>
            sql.TrimStart().StartsWith("SELECT count(*)", StringComparison.OrdinalIgnoreCase));
        listSql.Should().ContainSingle(sql =>
            !sql.TrimStart().StartsWith("SELECT count(*)", StringComparison.OrdinalIgnoreCase));
        listSql.Should().OnlyContain(sql =>
            sql.Contains("WHERE operator_id = @p0", StringComparison.OrdinalIgnoreCase) &&
            sql.Contains("AND UPPER(booking_code) = UPPER(@p1)", StringComparison.OrdinalIgnoreCase),
            "both the count and items SQL must tenant-scope the booking-code subquery itself");

        result.TotalItems.Should().Be(1, "the tenant predicate must also constrain count");
        var item = result.Items.Should().ContainSingle().Subject;
        item.BookingCode.Should().Be("VR-20260711-OWNER001");
        item.Status.Should().Be("CONFIRMED");
        item.Trip.Should().BeEquivalentTo(new
        {
            RouteName = "Route",
            OriginName = "Origin",
            DestinationName = "Destination",
            DepartureAt = departure,
            CurrentDepartureAt = departure,
        });
        item.SeatCount.Should().Be(1);
        item.TotalAmount.Should().Be(120_000);

        var pastEnd = await repository.ListOperatorBookingsAsync(criteria with { Page = 2 });
        pastEnd.Items.Should().BeEmpty();
        pastEnd.TotalItems.Should().Be(1);
    }

    [Fact]
    public async Task List_IntMaxPage_ReturnsEmptyPageWithoutOffsetOverflow()
    {
        await _factory.InitializeAsync();
        var owner = Guid.NewGuid();
        var departure = new DateTimeOffset(2026, 7, 10, 17, 0, 0, TimeSpan.Zero);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
            await InsertBooking(db, Guid.NewGuid(), "VR-20260711-MAXPAGE", owner, Guid.NewGuid(), Guid.NewGuid(),
                "CONFIRMED", departure, 120_000, departure);
        }

        await using var readScope = _factory.Services.CreateAsyncScope();
        var handler = new ListOperatorBookingsQueryHandler(
            readScope.ServiceProvider.GetRequiredService<IBookingRepository>(),
            new UnusedIdentityUserServiceClient());

        var result = await handler.Handle(new ListOperatorBookingsQuery(
            owner, null, null, null, null, null, int.MaxValue, 100), default);

        result.Items.Should().BeEmpty();
        result.TotalItems.Should().Be(1);
        result.Page.Should().Be(int.MaxValue);
        result.PageSize.Should().Be(100);
        result.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public async Task List_BookingCodeSqlInjectionPayloadIsTreatedAsDataAndLeaksNoRows()
    {
        await _factory.InitializeAsync();
        var owner = Guid.NewGuid();
        var foreignOwner = Guid.NewGuid();
        var ownerCode = $"VR-{Guid.NewGuid():N}"[..30];
        var foreignCode = $"VR-{Guid.NewGuid():N}"[..30];
        var departure = new DateTimeOffset(2026, 7, 10, 17, 0, 0, TimeSpan.Zero);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
            await InsertBooking(db, Guid.NewGuid(), ownerCode, owner, Guid.NewGuid(), Guid.NewGuid(),
                "CONFIRMED", departure, 120_000, departure);
            await InsertBooking(db, Guid.NewGuid(), foreignCode, foreignOwner, Guid.NewGuid(), Guid.NewGuid(),
                "CONFIRMED", departure, 999_000, departure);
        }

        await using var readScope = _factory.Services.CreateAsyncScope();
        var repository = readScope.ServiceProvider.GetRequiredService<IBookingRepository>();
        var criteria = new OperatorBookingListCriteria(
            owner, null, null, null, null, null, "' OR 1=1 --", 1, 20, "createdAt", true);

        var result = await repository.ListOperatorBookingsAsync(criteria);

        result.Items.Should().BeEmpty();
        result.TotalItems.Should().Be(0);
    }

    private static async Task InsertBooking(
        BookingDbContext db,
        Guid id,
        string code,
        Guid operatorId,
        Guid passengerUserId,
        Guid tripId,
        string status,
        DateTimeOffset departure,
        long amount,
        DateTimeOffset createdAt)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO vietride_booking.bookings (
    id, booking_code, passenger_user_id, trip_id, operator_id, pickup_station_id,
    base_fare, discount_amount, total_amount, status, refund_override,
    trip_snapshot_origin_name, trip_snapshot_dest_name, trip_snapshot_departure, trip_current_departure,
    trip_snapshot_route_name, created_at, updated_at)
VALUES (
    {id}, {code}, {passengerUserId}, {tripId}, {operatorId}, {Guid.NewGuid()},
    {amount}, 0, {amount}, {status}::booking_status, FALSE,
    'Origin', 'Destination', {departure}, {departure}, 'Route', {createdAt}, {createdAt});");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO vietride_booking.passengers (
    id, booking_id, seat_number, boarding_status, created_at, updated_at)
VALUES ({Guid.NewGuid()}, {id}, 'A1', 'PENDING'::passenger_boarding_status,
    {createdAt}, {createdAt});");
    }

    private sealed class UnusedIdentityUserServiceClient : IIdentityUserServiceClient
    {
        public Task<Guid?> GetUserIdByPhoneAsync(string phone, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The no-phone query must not call Identity.");
    }
}
