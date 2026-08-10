using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Infrastructure;

namespace VietRide.Booking.IntegrationTests;

[Collection(VoucherPersistenceCollection.CollectionName)]
public sealed class BookingStatsRepositoryIntegrationTests
    : IClassFixture<VoucherPersistenceIntegrationTests.DbBackedVoucherFactory>
{
    private static readonly Guid OperatorId = Guid.NewGuid();
    private readonly VoucherPersistenceIntegrationTests.DbBackedVoucherFactory _factory;

    public BookingStatsRepositoryIntegrationTests(
        VoucherPersistenceIntegrationTests.DbBackedVoucherFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AdminAggregate_DoesNotSplitSameOperatorByHistoricalOperatorName()
    {
        await _factory.InitializeAsync();

        var statDate = new DateOnly(2026, 6, 26);
        var oldName = "Old Name";
        string? missingName = null;
        var newName = "New Name";
        var oldUpdatedAt = DateTimeOffset.Parse("2026-06-26T01:00:00Z");
        var missingUpdatedAt = DateTimeOffset.Parse("2026-06-26T02:00:00Z");
        var newUpdatedAt = DateTimeOffset.Parse("2026-06-26T03:00:00Z");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO vietride_booking.booking_stats (
    id,
    operator_id,
    operator_name,
    stat_date,
    trip_id,
    total_bookings,
    total_confirmed,
    total_cancelled,
    total_no_show,
    total_completed,
    total_revenue,
    total_refunded,
    total_seats_booked,
    updated_at
)
VALUES
    ({Guid.NewGuid()}, {OperatorId}, {oldName}, {statDate}, {Guid.NewGuid()}, 2, 2, 1, 1, 1, 200000, 0, 2, {oldUpdatedAt}),
    ({Guid.NewGuid()}, {OperatorId}, {missingName}, {statDate}, {Guid.NewGuid()}, 3, 3, 0, 0, 2, 300000, 0, 3, {missingUpdatedAt}),
    ({Guid.NewGuid()}, {OperatorId}, {newName}, {statDate}, {Guid.NewGuid()}, 5, 5, 2, 1, 4, 500000, 0, 5, {newUpdatedAt});");
        }

        await using var readScope = _factory.Services.CreateAsyncScope();
        var repository = readScope.ServiceProvider.GetRequiredService<IBookingStatsRepository>();

        var operatorStats = await repository.GetOperatorStatsAsync(OperatorId, null, null, "date");
        var byOperator = await repository.GetAdminAggregateStatsAsync(null, null, "operator");
        var byDate = await repository.GetAdminAggregateStatsAsync(null, null, "date");

        operatorStats.Should().ContainSingle(row => row.OperatorId == OperatorId && row.Date == statDate);
        var operatorStatsRow = operatorStats.Single(row => row.OperatorId == OperatorId && row.Date == statDate);
        operatorStatsRow.TotalBookings.Should().Be(10);
        operatorStatsRow.TotalRevenue.Should().Be(1_000_000);
        operatorStatsRow.TotalCancellations.Should().Be(3);
        operatorStatsRow.TotalNoShows.Should().Be(2);
        operatorStatsRow.TotalCompleted.Should().Be(7);

        byOperator.Should().ContainSingle(row => row.OperatorId == OperatorId);
        var operatorRow = byOperator.Single(row => row.OperatorId == OperatorId);
        operatorRow.OperatorName.Should().Be("New Name");
        operatorRow.TotalBookings.Should().Be(10);
        operatorRow.TotalRevenue.Should().Be(1_000_000);
        operatorRow.TotalCancellations.Should().Be(3);
        operatorRow.TotalNoShows.Should().Be(2);
        operatorRow.TotalCompleted.Should().Be(7);

        byDate.Should().ContainSingle(row => row.OperatorId == OperatorId && row.Date == statDate);
        var dateRow = byDate.Single(row => row.OperatorId == OperatorId && row.Date == statDate);
        dateRow.OperatorName.Should().Be("New Name");
        dateRow.TotalBookings.Should().Be(10);
        dateRow.TotalRevenue.Should().Be(1_000_000);
    }

    [Fact]
    public async Task BookingStatsMonth_Repository_GroupsByFirstVietnamCalendarDateWithoutSplittingOperators()
    {
        await _factory.InitializeAsync();

        var operatorId = Guid.NewGuid();
        var otherOperatorId = Guid.NewGuid();
        var operatorName = "Operator A";
        var otherOperatorName = "Operator B";
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO vietride_booking.booking_stats (
    id, operator_id, operator_name, stat_date, trip_id,
    total_bookings, total_confirmed, total_cancelled, total_no_show,
    total_completed, total_revenue, total_refunded, total_seats_booked, updated_at
)
VALUES
    ({Guid.NewGuid()}, {operatorId}, {operatorName}, {new DateOnly(2026, 1, 31)}, {Guid.NewGuid()}, 2, 2, 1, 0, 1, 200000, 0, 2, now()),
    ({Guid.NewGuid()}, {operatorId}, {operatorName}, {new DateOnly(2026, 2, 1)}, {Guid.NewGuid()}, 3, 3, 0, 1, 2, 300000, 0, 3, now()),
    ({Guid.NewGuid()}, {otherOperatorId}, {otherOperatorName}, {new DateOnly(2026, 2, 28)}, {Guid.NewGuid()}, 5, 5, 2, 0, 4, 500000, 0, 5, now());");
        }

        await using var readScope = _factory.Services.CreateAsyncScope();
        var repository = readScope.ServiceProvider.GetRequiredService<IBookingStatsRepository>();

        var operatorMonths = await repository.GetOperatorStatsAsync(
            operatorId,
            new DateOnly(2026, 1, 15),
            new DateOnly(2026, 2, 28),
            "month");
        var adminMonths = await repository.GetAdminAggregateStatsAsync(
            new DateOnly(2026, 1, 15),
            new DateOnly(2026, 2, 28),
            "month");

        operatorMonths.Should().HaveCount(2);
        operatorMonths.Select(row => row.Date).Should().Equal(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 2, 1));
        operatorMonths.Sum(row => row.TotalBookings).Should().Be(5);

        adminMonths.Should().HaveCount(2);
        adminMonths.Should().OnlyContain(row => row.OperatorId == null && row.OperatorName == null);
        adminMonths.Single(row => row.Date == new DateOnly(2026, 2, 1)).TotalBookings.Should().Be(8);
        adminMonths.Sum(row => row.TotalRevenue).Should().Be(1_000_000);
    }
}
