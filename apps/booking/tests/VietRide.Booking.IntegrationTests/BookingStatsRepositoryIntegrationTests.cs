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
    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
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

        var operatorStats = await repository.GetOperatorStatsAsync(OperatorId, null, null);
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
}
