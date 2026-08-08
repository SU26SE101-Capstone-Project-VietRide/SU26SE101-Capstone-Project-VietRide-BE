using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.Admin.Dashboard;
using VietRide.Booking.Infrastructure;

namespace VietRide.Booking.IntegrationTests;

[Collection(VoucherPersistenceCollection.CollectionName)]
public sealed class AdminDashboardPersistenceIntegrationTests
    : IClassFixture<VoucherPersistenceIntegrationTests.DbBackedVoucherFactory>
{
    private readonly VoucherPersistenceIntegrationTests.DbBackedVoucherFactory _factory;

    public AdminDashboardPersistenceIntegrationTests(
        VoucherPersistenceIntegrationTests.DbBackedVoucherFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Summary_UsesPersistedEqualPeriodTotalsAndCurrentApprovedOperatorIntersection()
    {
        await _factory.InitializeAsync();
        var approvedA = Guid.NewGuid();
        var approvedB = Guid.NewGuid();
        var unapproved = Guid.NewGuid();
        var currentFrom = new DateOnly(2098, 7, 1);
        var currentTo = new DateOnly(2098, 7, 31);
        var previousFrom = new DateOnly(2098, 5, 31);
        var previousTo = new DateOnly(2098, 6, 30);

        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<BookingDbContext>();
            await InsertStatsAsync(db, approvedA, new DateOnly(2098, 7, 1), 10, 1_000);
            await InsertStatsAsync(db, approvedB, new DateOnly(2098, 7, 31), 5, 500);
            await InsertStatsAsync(db, approvedA, new DateOnly(2098, 8, 1), 99, 99_000);
            await InsertStatsAsync(db, approvedA, new DateOnly(2098, 5, 31), 4, 400);
            await InsertStatsAsync(db, unapproved, new DateOnly(2098, 6, 30), 6, 600);
            await InsertStatsAsync(db, approvedA, new DateOnly(2098, 5, 30), 99, 99_000);
        }

        await using var readScope = _factory.Services.CreateAsyncScope();
        var repository = readScope.ServiceProvider.GetRequiredService<IBookingStatsRepository>();
        var identity = Substitute.For<IIdentityDashboardMetricsClient>();
        var payment = Substitute.For<IPaymentRevenueSummaryClient>();
        identity.GetAsync(currentFrom, currentTo, Arg.Any<CancellationToken>())
            .Returns(new IdentityDashboardMetricsDto(20, [approvedB, approvedA], [], []));
        identity.GetAsync(previousFrom, previousTo, Arg.Any<CancellationToken>())
            .Returns(new IdentityDashboardMetricsDto(10, [approvedA, unapproved], [], []));
        payment.GetAsync(currentFrom, currentTo, Arg.Any<CancellationToken>())
            .Returns(new PaymentRevenueSummaryDto(1_500, 1_200, 1_000, 200, 300));
        payment.GetAsync(previousFrom, previousTo, Arg.Any<CancellationToken>())
            .Returns(new PaymentRevenueSummaryDto(1_000, 800, 700, 100, 200));
        var handler = new GetAdminDashboardSummaryQueryHandler(repository, identity, payment);

        var result = await handler.Handle(
            new GetAdminDashboardSummaryQuery(currentFrom, currentTo),
            CancellationToken.None);

        result.TotalProjectRevenueVnd.Should().Be(new AdminDashboardComparisonResponse(1_500, 1_000, 50m, "UP"));
        result.Bookings.Should().Be(new AdminDashboardComparisonResponse(15, 10, 50m, "UP"));
        result.ActiveOperators.Should().Be(new AdminDashboardComparisonResponse(2, 1, 100m, "UP"));
        result.ActiveUsers.Should().Be(new AdminDashboardComparisonResponse(20, 10, 100m, "UP"));
    }

    private static Task InsertStatsAsync(
        BookingDbContext db,
        Guid operatorId,
        DateOnly statDate,
        int totalBookings,
        long totalRevenue)
        => db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO vietride_booking.booking_stats (
    id, operator_id, operator_name, stat_date, trip_id,
    total_bookings, total_confirmed, total_cancelled, total_no_show,
    total_completed, total_revenue, total_refunded, total_seats_booked, updated_at
)
VALUES (
    {Guid.NewGuid()}, {operatorId}, {'O' + operatorId.ToString()}, {statDate}, {Guid.NewGuid()},
    {totalBookings}, {totalBookings}, 0, 0,
    {totalBookings}, {totalRevenue}, 0, {totalBookings}, now()
);");
}
