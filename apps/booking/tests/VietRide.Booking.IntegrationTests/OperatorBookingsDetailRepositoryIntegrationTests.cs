using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Infrastructure;

namespace VietRide.Booking.IntegrationTests;

[Collection(VoucherPersistenceCollection.CollectionName)]
public sealed class OperatorBookingsDetailRepositoryIntegrationTests
    : IClassFixture<VoucherPersistenceIntegrationTests.DbBackedVoucherFactory>
{
    private readonly VoucherPersistenceIntegrationTests.DbBackedVoucherFactory _factory;

    public OperatorBookingsDetailRepositoryIntegrationTests(VoucherPersistenceIntegrationTests.DbBackedVoucherFactory factory)
        => _factory = factory;

    [Fact]
    public async Task Detail_TenantPredicateLeanSeatsAndRealTimelineAreDeterministic()
    {
        await _factory.InitializeAsync();
        var bookingId = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        var firstHistoryId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondHistoryId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var occurredAt = new DateTimeOffset(2026, 7, 11, 1, 0, 0, TimeSpan.Zero);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
            await Seed(db, bookingId, owner, occurredAt, firstHistoryId, secondHistoryId);
        }

        await using var readScope = _factory.Services.CreateAsyncScope();
        var repository = readScope.ServiceProvider.GetRequiredService<IBookingRepository>();
        _factory.SqlCapture.Clear();
        var detail = await repository.GetOperatorBookingDetailAsync(bookingId, owner);

        detail.Should().NotBeNull();
        detail!.Trip.Should().BeEquivalentTo(new
        {
            RouteName = "Route",
            OriginName = "Origin",
            DestinationName = "Destination",
            DepartureAt = occurredAt,
            CurrentDepartureAt = occurredAt,
        });
        detail.Seats.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            TicketCode = "VT-20260711-DETAIL01",
            SeatNumber = "A1",
            TicketStatus = "CANCELLED",
            BoardingStatus = "PENDING",
        });
        detail.StatusTimeline.Select(row => (row.Status, row.ReasonCode)).Should().Equal(
            ("PENDING_PAYMENT", (string?)null), ("CANCELLED", "USER_INITIATED"));
        _factory.SqlCapture.Commands.First().Should().Contain("WHERE b.id = @__bookingId_0 AND b.operator_id = @__operatorId_1");
        _factory.SqlCapture.Commands.Should().Contain(sql =>
            sql.Contains("booking_status_history", StringComparison.OrdinalIgnoreCase) &&
            sql.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase) && sql.Contains("occurred_at", StringComparison.OrdinalIgnoreCase));

        (await repository.GetOperatorBookingDetailAsync(bookingId, foreign)).Should().BeNull();
        _factory.SqlCapture.Clear();
        (await repository.BookingExistsAsync(bookingId)).Should().BeTrue();
        var existenceSql = _factory.SqlCapture.Commands.Should().ContainSingle().Subject;
        existenceSql.Should().Contain("EXISTS");
        existenceSql.Should().NotContain("operator_id");
        existenceSql.Should().NotContain("passenger_user_id");
        (await repository.BookingExistsAsync(Guid.NewGuid())).Should().BeFalse();
    }

    private static async Task Seed(BookingDbContext db, Guid bookingId, Guid owner, DateTimeOffset at, Guid first, Guid second)
    {
        var buyer = Guid.NewGuid();
        var trip = Guid.NewGuid();
        var passenger = Guid.NewGuid();
        var ticket = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO vietride_booking.bookings (id, booking_code, passenger_user_id, trip_id, operator_id,
 pickup_station_id, base_fare, discount_amount, total_amount, status, cancellation_reason, refund_override,
 trip_snapshot_origin_name, trip_snapshot_dest_name, trip_snapshot_departure, trip_current_departure,
 trip_snapshot_route_name, created_at, updated_at)
VALUES ({bookingId}, {"VR-20260711-DETAIL01"}, {buyer}, {trip}, {owner}, {Guid.NewGuid()}, 100000, 0, 100000,
 'CANCELLED'::booking_status, 'USER_INITIATED'::booking_cancellation_reason, FALSE,
 'Origin', 'Destination', {at}, {at}, 'Route', {at}, {at});");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO vietride_booking.passengers (id, booking_id, seat_number, boarding_status, created_at, updated_at)
VALUES ({passenger}, {bookingId}, {"A1"}, 'PENDING'::passenger_boarding_status, {at}, {at});");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO vietride_booking.tickets (id, booking_id, passenger_id, ticket_code, seat_number, status,
 fare_amount, discount_amount, paid_amount, created_at, updated_at)
VALUES ({ticket}, {bookingId}, {passenger}, {"VT-20260711-DETAIL01"}, {"A1"}, 'CANCELLED'::ticket_status,
 100000, 0, 100000, {at}, {at});");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO vietride_booking.booking_status_history (id, booking_id, status, occurred_at, reason_code, actor_user_id, source)
VALUES ({second}, {bookingId}, 'CANCELLED'::booking_status, {at}, {"USER_INITIATED"}, {buyer}, {"CANCEL_BOOKING"}),
       ({first}, {bookingId}, 'PENDING_PAYMENT'::booking_status, {at}, NULL, {buyer}, {"CREATE_BOOKING"});");
    }
}
