using FluentAssertions;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Features.OperatorReports;
using VietRide.Payment.Domain.Entities;
using VietRide.Shared.Application.Reporting;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.UnitTests.Features.OperatorReports;

public sealed class ExportOperatorLedgerReportQueryHandlerTests
{
    private static readonly Guid OperatorId = Guid.Parse("41000000-0000-4000-8000-000000000011");

    [Theory]
    [InlineData(OperatorLedgerReportKind.Revenue, "Revenue", "revenue-report-20260718-20260718.xlsx", false)]
    [InlineData(OperatorLedgerReportKind.Refunds, "Refunds", "refunds-report-20260718-20260718.xlsx", true)]
    public async Task Handle_UsesLedgerTenantAndStableWorkbookContract(
        OperatorLedgerReportKind kind,
        string sheet,
        string fileName,
        bool refundOnly)
    {
        var repository = new ReportLedgerRepository();
        var writer = new CapturingWriter();
        var handler = new ExportOperatorLedgerReportQueryHandler(repository, writer, new FixedClock());

        await using var report = await handler.Handle(
            new ExportOperatorLedgerReportQuery(
                OperatorId,
                new DateOnly(2026, 7, 18),
                new DateOnly(2026, 7, 18),
                kind),
            CancellationToken.None);

        repository.OperatorId.Should().Be(OperatorId);
        repository.RefundOnly.Should().Be(refundOnly);
        repository.FromUtc.Should().Be(new DateTimeOffset(2026, 7, 17, 17, 0, 0, TimeSpan.Zero));
        repository.ToUtc.Should().Be(new DateTimeOffset(2026, 7, 18, 17, 0, 0, TimeSpan.Zero));
        writer.Spec!.SheetName.Should().Be(sheet);
        writer.Spec.FileName.Should().Be(fileName);
        writer.Rows.Should().ContainSingle();
        writer.Rows[0].Cells[5].Integer.Should().Be(refundOnly ? -50_000 : 50_000);
    }

    private sealed class ReportLedgerRepository : IOperatorLedgerEntryRepository
    {
        public Guid OperatorId { get; private set; }
        public DateTimeOffset FromUtc { get; private set; }
        public DateTimeOffset ToUtc { get; private set; }
        public bool RefundOnly { get; private set; }

        public async IAsyncEnumerable<OperatorLedgerReportRow> StreamOperatorReportRowsAsync(
            Guid operatorId,
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            bool refundOnly,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            OperatorId = operatorId;
            FromUtc = fromUtc;
            ToUtc = toUtc;
            RefundOnly = refundOnly;
            yield return new OperatorLedgerReportRow(
                Guid.NewGuid(),
                refundOnly ? "BOOKING_REFUND" : "BOOKING_REVENUE",
                "BOOKING",
                Guid.NewGuid(),
                Guid.NewGuid(),
                refundOnly ? -50_000 : 50_000,
                new DateTimeOffset(2026, 7, 18, 1, 0, 0, TimeSpan.Zero),
                null);
            await Task.Yield();
        }

        public Task<long> SumTripNetAmountAsync(Guid operatorId, Guid tripId, CancellationToken cancellationToken)
            => Task.FromResult(0L);
        public Task<OperatorLedgerEntry?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<OperatorLedgerEntry?>(null);
        public Task<OperatorLedgerEntry> AddAsync(OperatorLedgerEntry entity, CancellationToken ct) => Task.FromResult(entity);
        public void Update(OperatorLedgerEntry entity) => throw new NotSupportedException();
        public void Remove(OperatorLedgerEntry entity) => throw new NotSupportedException();
        public IQueryable<OperatorLedgerEntry> Query() => Array.Empty<OperatorLedgerEntry>().AsQueryable();
        public IQueryable<OperatorLedgerEntry> QueryNoTracking() => Query();
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
