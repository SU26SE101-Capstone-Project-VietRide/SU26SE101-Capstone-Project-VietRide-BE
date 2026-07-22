using FluentAssertions;
using VietRide.Shared.Application.Reporting;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.OperatorReports;
using TripEntity = VietRide.Trip.Domain.Entities.Trip;

namespace VietRide.Trip.UnitTests.Features.OperatorReports;

public sealed class ExportOccupancyReportQueryHandlerTests
{
    [Fact]
    public async Task Handle_UsesTenantRangeAndRoundsOccupancyAwayFromZero()
    {
        var operatorId = Guid.Parse("41000000-0000-4000-8000-000000000031");
        var tripId = Guid.Parse("41000000-0000-4000-8000-000000000032");
        var repository = new ReportTripRepository(new TripOperatorOccupancyRow(
            tripId,
            Guid.NewGuid(),
            "COMPLETED",
            new DateTimeOffset(2026, 7, 18, 1, 0, 0, TimeSpan.Zero),
            3,
            2));
        var writer = new CapturingWriter();
        var handler = new ExportOccupancyReportQueryHandler(repository, writer, new FixedClock());

        await using var report = await handler.Handle(
            new ExportOccupancyReportQuery(
                operatorId,
                new DateOnly(2026, 7, 18),
                new DateOnly(2026, 7, 18)),
            CancellationToken.None);

        repository.OperatorId.Should().Be(operatorId);
        repository.FromUtc.Should().Be(new DateTimeOffset(2026, 7, 17, 17, 0, 0, TimeSpan.Zero));
        repository.ToUtc.Should().Be(new DateTimeOffset(2026, 7, 18, 17, 0, 0, TimeSpan.Zero));
        writer.Spec!.SheetName.Should().Be("Occupancy");
        writer.Spec.FileName.Should().Be("occupancy-report-20260718-20260718.xlsx");
        writer.Rows.Should().ContainSingle();
        writer.Rows[0].Cells[0].Text.Should().Be(tripId.ToString("D"));
        writer.Rows[0].Cells[6].Decimal.Should().Be(66.67m);
    }

    private sealed class ReportTripRepository : ITripRepository
    {
        private readonly TripOperatorOccupancyRow _row;

        public ReportTripRepository(TripOperatorOccupancyRow row) => _row = row;
        public Guid OperatorId { get; private set; }
        public DateTimeOffset FromUtc { get; private set; }
        public DateTimeOffset ToUtc { get; private set; }

        public async IAsyncEnumerable<TripOperatorOccupancyRow> StreamOperatorOccupancyRowsAsync(
            Guid operatorId,
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            OperatorId = operatorId;
            FromUtc = fromUtc;
            ToUtc = toUtc;
            yield return _row;
            await Task.Yield();
        }

        public Task<TripEntity?> GetWithSeatsAsync(Guid tripId, CancellationToken cancellationToken)
            => Task.FromResult<TripEntity?>(null);
        public Task<TripEntity?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<TripEntity?>(null);
        public Task<TripEntity> AddAsync(TripEntity entity, CancellationToken ct) => Task.FromResult(entity);
        public void Update(TripEntity entity) => throw new NotSupportedException();
        public void Remove(TripEntity entity) => throw new NotSupportedException();
        public IQueryable<TripEntity> Query() => Array.Empty<TripEntity>().AsQueryable();
        public IQueryable<TripEntity> QueryNoTracking() => Query();
    }

    private sealed class CapturingWriter : IExcelReportWriter
    {
        public ExcelReportSpec? Spec { get; private set; }
        public List<ExcelReportRow> Rows { get; } = [];

        public async Task<ExcelReportStream> WriteAsync(ExcelReportSpec spec, IAsyncEnumerable<ExcelReportRow> rows, CancellationToken cancellationToken = default)
        {
            Spec = spec;
            await foreach (var row in rows.WithCancellation(cancellationToken)) Rows.Add(row);
            return new ExcelReportStream(new MemoryStream(), spec.FileName, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
    }
}
