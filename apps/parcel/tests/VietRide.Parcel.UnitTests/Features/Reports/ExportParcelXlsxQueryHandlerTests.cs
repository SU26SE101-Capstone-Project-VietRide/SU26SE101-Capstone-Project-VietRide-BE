using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels.Reports;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Reporting;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.UnitTests.Features.Reports;

public sealed class ExportParcelXlsxQueryHandlerTests
{
    [Fact]
    public async Task Handle_UsesTenantRangeAndStableWorkbookContract()
    {
        var operatorId = Guid.Parse("41000000-0000-4000-8000-000000000021");
        var parcelId = Guid.Parse("41000000-0000-4000-8000-000000000022");
        var repository = Substitute.For<IParcelRepository>();
        repository.StreamOperatorReportRowsAsync(
                operatorId,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Rows(new ParcelOperatorReportRow(
                parcelId,
                "VRP-REPORT",
                Guid.NewGuid(),
                "TP.HCM - Đà Lạt",
                "Bến xe Miền Đông",
                "Bến xe Đà Lạt",
                "51B-123.45",
                ParcelStatus.DELIVERY_CONFIRMED,
                ParcelSizeCategory.SMALL,
                150_000,
                100_000,
                50_000,
                10_000,
                new DateTimeOffset(2026, 7, 18, 1, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 18, 2, 0, 0, TimeSpan.Zero))));
        var writer = new CapturingWriter();
        var handler = new ExportParcelXlsxQueryHandler(repository, writer, new FixedClock());

        await using var report = await handler.Handle(
            new ExportParcelXlsxQuery(
                operatorId,
                new DateOnly(2026, 7, 18),
                new DateOnly(2026, 7, 18)),
            CancellationToken.None);

        writer.Spec!.SheetName.Should().Be("Bưu kiện");
        writer.Spec.FileName.Should().Be("bao-cao-buu-kien-20260718-20260718.xlsx");
        writer.Spec.CurrencyColumns.Should().BeEquivalentTo([7, 8, 9, 10]);
        writer.Rows.Should().ContainSingle();
        writer.Rows[0].Cells[0].Text.Should().Be("VRP-REPORT");
        writer.Rows[0].Cells[5].Text.Should().Be("Đã xác nhận nhận hàng");
        writer.Rows[0].Cells[6].Text.Should().Be("Nhỏ");
        writer.Rows[0].Cells[^2].Text.Should().Be(parcelId.ToString("D"));
        repository.Received(1).StreamOperatorReportRowsAsync(
            operatorId,
            new DateTimeOffset(2026, 7, 17, 17, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 18, 17, 0, 0, TimeSpan.Zero),
            Arg.Any<CancellationToken>());
    }

    private static async IAsyncEnumerable<ParcelOperatorReportRow> Rows(
        ParcelOperatorReportRow row)
    {
        yield return row;
        await Task.Yield();
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
