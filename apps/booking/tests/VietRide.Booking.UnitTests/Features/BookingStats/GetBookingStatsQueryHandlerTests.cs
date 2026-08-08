using FluentAssertions;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.BookingStats.GetAdminBookingStatsAggregate;
using VietRide.Booking.Application.Features.BookingStats.GetOperatorBookingStats;
using VietRide.Shared.Application.Exceptions;
using BookingStatsEntity = VietRide.Booking.Domain.Entities.BookingStats;

namespace VietRide.Booking.UnitTests.Features.BookingStats;

public sealed class GetBookingStatsQueryHandlerTests
{
    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

    [Fact]
    public async Task OperatorStats_MapsOperationalCountsWithoutRevenue()
    {
        var stats = new FakeBookingStatsRepository();
        stats.OperatorRows =
        [
            new OperatorBookingStatsReadModel(
                OperatorId,
                new DateOnly(2026, 6, 26),
                TotalBookings: 3,
                TotalRevenue: 450_000,
                TotalCancellations: 1,
                TotalNoShows: 2,
                TotalCompleted: 4),
        ];
        var handler = new GetOperatorBookingStatsQueryHandler(stats);

        var result = await handler.Handle(
            new GetOperatorBookingStatsQuery(OperatorId, null, null, "date"),
            CancellationToken.None);

        result.Items.Should().ContainSingle();
        var item = result.Items.Single();
        item.OperatorId.Should().Be(OperatorId);
        item.TotalNoShows.Should().Be(2);
        item.TotalPartialNoShows.Should().Be(0);
        stats.LastOperatorId.Should().Be(OperatorId);
        stats.LastOperatorGroupBy.Should().Be("date");
    }

    [Fact]
    public async Task BookingStatsMonth_Operator_ZeroFillsMissingMonthsAndReconcilesSummary()
    {
        var stats = new FakeBookingStatsRepository
        {
            OperatorRows =
            [
                new OperatorBookingStatsReadModel(
                    OperatorId,
                    new DateOnly(2026, 2, 1),
                    TotalBookings: 3,
                    TotalRevenue: 450_000,
                    TotalCancellations: 1,
                    TotalNoShows: 2,
                    TotalCompleted: 4),
            ],
        };
        var handler = new GetOperatorBookingStatsQueryHandler(stats);

        var result = await handler.Handle(
            new GetOperatorBookingStatsQuery(
                OperatorId,
                new DateOnly(2026, 1, 15),
                new DateOnly(2026, 3, 10),
                "month"),
            CancellationToken.None);

        result.Items.Select(item => item.Date).Should().Equal(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 2, 1),
            new DateOnly(2026, 3, 1));
        result.Items[0].TotalBookings.Should().Be(0);
        result.Items[1].TotalBookings.Should().Be(3);
        result.Items[2].TotalCompleted.Should().Be(0);
        result.TotalBookings.Should().Be(result.Items.Sum(item => item.TotalBookings));
        stats.LastOperatorGroupBy.Should().Be("month");
    }

    [Fact]
    public async Task AdminStats_MapsOperationalCountsWithoutRevenue()
    {
        var stats = new FakeBookingStatsRepository();
        stats.AdminRows =
        [
            new AdminBookingStatsAggregateReadModel(
                OperatorId,
                "VietRide Express",
                Date: null,
                TotalBookings: 7,
                TotalRevenue: 1_250_000,
                TotalCancellations: 1,
                TotalNoShows: 2,
                TotalCompleted: 4),
        ];
        var handler = new GetAdminBookingStatsAggregateQueryHandler(stats);

        var result = await handler.Handle(
            new GetAdminBookingStatsAggregateQuery(null, null, "operator"),
            CancellationToken.None);

        result.Items.Should().ContainSingle();
        var item = result.Items.Single();
        item.OperatorName.Should().Be("VietRide Express");
        item.TotalPartialNoShows.Should().Be(0);
        stats.LastAdminGroupBy.Should().Be("operator");
    }

    [Fact]
    public async Task BookingStatsMonth_Admin_ZeroFillsMissingMonthsAndReconcilesSummary()
    {
        var stats = new FakeBookingStatsRepository
        {
            AdminRows =
            [
                new AdminBookingStatsAggregateReadModel(
                    OperatorId: null,
                    OperatorName: null,
                    Date: new DateOnly(2026, 2, 1),
                    TotalBookings: 7,
                    TotalRevenue: 1_250_000,
                    TotalCancellations: 1,
                    TotalNoShows: 2,
                    TotalCompleted: 4),
            ],
        };
        var handler = new GetAdminBookingStatsAggregateQueryHandler(stats);

        var result = await handler.Handle(
            new GetAdminBookingStatsAggregateQuery(
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 3, 31),
                "month"),
            CancellationToken.None);

        result.Items.Select(item => item.Date).Should().Equal(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 2, 1),
            new DateOnly(2026, 3, 1));
        result.Items.Should().OnlyContain(item => item.OperatorId == null && item.OperatorName == null);
        result.TotalBookings.Should().Be(7);
        stats.LastAdminGroupBy.Should().Be("month");
    }

    [Fact]
    public async Task OperatorStats_WhenGroupByIsNotDate_ThrowsCodedValidationException()
    {
        var handler = new GetOperatorBookingStatsQueryHandler(new FakeBookingStatsRepository());

        var act = () => handler.Handle(
            new GetOperatorBookingStatsQuery(OperatorId, null, null, "operator"),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<CodedValidationException>();
        ex.Which.ErrorCode.Should().Be("VALIDATION_ERROR");
        ex.Which.Errors.Should().ContainSingle(error => error.Field == "groupBy");
    }

    [Fact]
    public async Task AdminStats_WhenGroupByIsUnsupported_ThrowsCodedValidationException()
    {
        var handler = new GetAdminBookingStatsAggregateQueryHandler(new FakeBookingStatsRepository());

        var act = () => handler.Handle(
            new GetAdminBookingStatsAggregateQuery(null, null, "trip"),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<CodedValidationException>();
        ex.Which.ErrorCode.Should().Be("VALIDATION_ERROR");
        ex.Which.Errors.Should().ContainSingle(error => error.Field == "groupBy");
    }

    [Theory]
    [InlineData("2026-01-01", null)]
    [InlineData(null, "2026-01-31")]
    [InlineData("2026-02-01", "2026-01-31")]
    [InlineData("2025-01-01", "2026-01-02")]
    public async Task BookingStatsMonth_WhenRangeIsMissingReversedOrOver366Days_ThrowsValidation(
        string? fromValue,
        string? toValue)
    {
        var handler = new GetOperatorBookingStatsQueryHandler(new FakeBookingStatsRepository());
        DateOnly? from = fromValue is null ? null : DateOnly.Parse(fromValue);
        DateOnly? to = toValue is null ? null : DateOnly.Parse(toValue);

        var act = () => handler.Handle(
            new GetOperatorBookingStatsQuery(OperatorId, from, to, "month"),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<CodedValidationException>();
        ex.Which.ErrorCode.Should().Be("VALIDATION_ERROR");
        ex.Which.Errors.Should().Contain(error => error.Field == "from" || error.Field == "to");
    }

    [Fact]
    public async Task BookingStatsMonth_WhenRangeEndsAtMaximumDate_ZeroFillsWithoutOverflow()
    {
        var handler = new GetOperatorBookingStatsQueryHandler(new FakeBookingStatsRepository());

        var result = await handler.Handle(
            new GetOperatorBookingStatsQuery(
                OperatorId,
                new DateOnly(9999, 12, 1),
                new DateOnly(9999, 12, 31),
                "month"),
            CancellationToken.None);

        result.Items.Should().ContainSingle();
        result.Items.Single().Date.Should().Be(new DateOnly(9999, 12, 1));
    }

    private sealed class FakeBookingStatsRepository : IBookingStatsRepository
    {
        public IReadOnlyList<OperatorBookingStatsReadModel> OperatorRows { get; set; } = [];
        public IReadOnlyList<AdminBookingStatsAggregateReadModel> AdminRows { get; set; } = [];
        public Guid LastOperatorId { get; private set; }
        public string? LastOperatorGroupBy { get; private set; }
        public string? LastAdminGroupBy { get; private set; }

        public Task<BookingStatsEntity?> GetByIdAsync(Guid id, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<BookingStatsEntity> AddAsync(BookingStatsEntity entity, CancellationToken ct)
            => throw new NotSupportedException();

        public void Update(BookingStatsEntity entity)
            => throw new NotSupportedException();

        public void Remove(BookingStatsEntity entity)
            => throw new NotSupportedException();

        public IQueryable<BookingStatsEntity> Query()
            => throw new NotSupportedException();

        public IQueryable<BookingStatsEntity> QueryNoTracking()
            => throw new NotSupportedException();

        public Task<bool> TryClaimProcessedEventAsync(
            string eventType,
            Guid bookingId,
            DateTimeOffset processedAt,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task UpsertDeltaAsync(BookingStatsEntity delta, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<OperatorBookingStatsReadModel>> GetOperatorStatsAsync(
            Guid operatorId,
            DateOnly? from,
            DateOnly? to,
            string groupBy,
            CancellationToken ct = default)
        {
            LastOperatorId = operatorId;
            LastOperatorGroupBy = groupBy;
            return Task.FromResult(OperatorRows);
        }

        public Task<IReadOnlyList<AdminBookingStatsAggregateReadModel>> GetAdminAggregateStatsAsync(
            DateOnly? from,
            DateOnly? to,
            string groupBy,
            CancellationToken ct = default)
        {
            LastAdminGroupBy = groupBy;
            return Task.FromResult(AdminRows);
        }
    }
}
