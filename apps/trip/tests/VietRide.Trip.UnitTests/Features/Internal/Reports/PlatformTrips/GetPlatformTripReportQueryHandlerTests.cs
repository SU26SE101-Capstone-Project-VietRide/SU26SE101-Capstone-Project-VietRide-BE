using FluentAssertions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Internal.Reports.PlatformTrips;
using TripEntity = VietRide.Trip.Domain.Entities.Trip;

namespace VietRide.Trip.UnitTests.Features.Internal.Reports.PlatformTrips;

public sealed class GetPlatformTripReportQueryHandlerTests
{
    [Fact]
    public async Task Handle_WithValidUtcRange_ReturnsRepositoryRows()
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero);
        IReadOnlyList<PlatformTripReportItem> rows =
        [
            new(Guid.Parse("40000000-0000-0000-0000-000000000001"), 4),
        ];
        var repository = new FakeTripRepository { Rows = rows };
        var handler = new GetPlatformTripReportQueryHandler(repository);

        var result = await handler.Handle(
            new GetPlatformTripReportQuery(
                "2026-01-01T00:00:00.0000000Z",
                "2026-12-31T23:59:59Z"),
            CancellationToken.None);

        result.Items.Should().BeEquivalentTo(rows);
        repository.ReportCallCount.Should().Be(1);
        repository.LastFrom.Should().Be(from);
        repository.LastTo.Should().Be(to);
    }

    [Theory]
    [InlineData(null, "2026-01-02T00:00:00Z")]
    [InlineData("2026-01-01T00:00:00Z", null)]
    [InlineData("2026-01-01T00:00:00+00:00", "2026-01-02T00:00:00Z")]
    [InlineData("not-a-time", "2026-01-02T00:00:00Z")]
    [InlineData("2026-01-02T00:00:00Z", "2026-01-02T00:00:00Z")]
    [InlineData("2026-01-03T00:00:00Z", "2026-01-02T00:00:00Z")]
    [InlineData("2025-01-01T00:00:00Z", "2026-01-03T00:00:00Z")]
    public async Task Handle_WithInvalidRange_ThrowsCanonicalValidationError(string? from, string? to)
    {
        var repository = new FakeTripRepository();
        var handler = new GetPlatformTripReportQueryHandler(repository);

        var act = () => handler.Handle(
            new GetPlatformTripReportQuery(from, to),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedValidationException>();
        exception.Which.ErrorCode.Should().Be("VALIDATION_ERROR");
        repository.ReportCallCount.Should().Be(0);
    }

    private sealed class FakeTripRepository : ITripRepository
    {
        public IReadOnlyList<PlatformTripReportItem> Rows { get; init; } = [];
        public int ReportCallCount { get; private set; }
        public DateTimeOffset LastFrom { get; private set; }
        public DateTimeOffset LastTo { get; private set; }

        public Task<IReadOnlyList<PlatformTripReportItem>> GetPlatformTripMetricsAsync(
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            CancellationToken cancellationToken = default)
        {
            ReportCallCount++;
            LastFrom = fromUtc;
            LastTo = toUtc;
            return Task.FromResult(Rows);
        }

        public Task<TripEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TripEntity> AddAsync(TripEntity entity, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Update(TripEntity entity)
            => throw new NotSupportedException();

        public void Remove(TripEntity entity)
            => throw new NotSupportedException();

        public IQueryable<TripEntity> Query()
            => throw new NotSupportedException();

        public IQueryable<TripEntity> QueryNoTracking()
            => throw new NotSupportedException();

        public Task<TripEntity?> GetWithSeatsAsync(
            Guid tripId,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
