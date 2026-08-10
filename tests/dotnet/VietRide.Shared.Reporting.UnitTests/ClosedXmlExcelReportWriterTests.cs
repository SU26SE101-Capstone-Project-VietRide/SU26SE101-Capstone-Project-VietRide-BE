using ClosedXML.Excel;
using FluentAssertions;
using VietRide.Shared.Application.Reporting;
using VietRide.Shared.Reporting;

namespace VietRide.Shared.Reporting.UnitTests;

public sealed class ClosedXmlExcelReportWriterTests
{
    [Fact]
    public async Task WriteAsync_EmptyReport_ProducesValidWorkbookAndDeletesTempFileOnDispose()
    {
        var writer = new ClosedXmlExcelReportWriter();
        var spec = new ExcelReportSpec("Bookings", ["booking_id", "amount_vnd"], "bookings.xlsx", new HashSet<int> { 1 });

        var report = await writer.WriteAsync(spec, Rows([]));
        var fileStream = report.Content.Should().BeOfType<FileStream>().Subject;
        var tempPath = fileStream.Name;

        report.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        report.FileName.Should().Be("bookings.xlsx");
        report.Content.Position.Should().Be(0);
        using (var workbook = new XLWorkbook(report.Content))
        {
            var sheet = workbook.Worksheet("Bookings");
            sheet.Cell(1, 1).GetString().Should().Be("booking_id");
            sheet.Cell(1, 2).GetString().Should().Be("amount_vnd");
            sheet.LastRowUsed()!.RowNumber().Should().Be(1);
        }

        await report.DisposeAsync();
        File.Exists(tempPath).Should().BeFalse();
    }

    [Fact]
    public async Task WriteAsync_TypedUnicodeRow_PreservesCellTypesAndFormats()
    {
        var writer = new ClosedXmlExcelReportWriter();
        var when = new DateTimeOffset(2026, 7, 18, 8, 30, 0, TimeSpan.Zero);
        var spec = new ExcelReportSpec(
            "Revenue",
            ["note", "amount_vnd", "ratio", "date", "occurred_at", "active"],
            "revenue.xlsx",
            new HashSet<int> { 1 });
        var row = new ExcelReportRow([
            ExcelReportCell.TextValue("Doanh thu tuyến Thành phố Hồ Chí Minh"),
            ExcelReportCell.IntegerValue(1_234_000),
            ExcelReportCell.DecimalValue(62.5m),
            ExcelReportCell.DateValue(new DateOnly(2026, 7, 18)),
            ExcelReportCell.DateTimeValue(when),
            ExcelReportCell.BooleanValue(true),
        ]);

        await using var report = await writer.WriteAsync(spec, Rows([row]));
        using var workbook = new XLWorkbook(report.Content);
        var sheet = workbook.Worksheet("Revenue");

        sheet.Cell(2, 1).GetString().Should().Be("Doanh thu tuyến Thành phố Hồ Chí Minh");
        sheet.Cell(2, 2).GetValue<long>().Should().Be(1_234_000);
        sheet.Cell(2, 2).Style.NumberFormat.Format.Should().Be("#,##0");
        sheet.Cell(2, 3).GetValue<decimal>().Should().Be(62.5m);
        sheet.Cell(2, 4).DataType.Should().Be(XLDataType.DateTime);
        sheet.Cell(2, 5).GetDateTime().Should().Be(new DateTime(2026, 7, 18, 15, 30, 0));
        sheet.Cell(2, 6).GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task WriteAsync_BlankAndFormulaLikeText_RemainBlankAndPlainText()
    {
        var writer = new ClosedXmlExcelReportWriter();
        var spec = new ExcelReportSpec(
            "Bookings",
            ["confirmed_at", "note"],
            "bookings.xlsx");
        var row = new ExcelReportRow([
            ExcelReportCell.BlankValue(),
            ExcelReportCell.TextValue("=HYPERLINK(\"https://invalid.example\",\"x\")"),
        ]);

        await using var report = await writer.WriteAsync(spec, Rows([row]));
        using var workbook = new XLWorkbook(report.Content);
        var sheet = workbook.Worksheet("Bookings");

        sheet.Cell(2, 1).IsEmpty().Should().BeTrue();
        sheet.Cell(2, 2).HasFormula.Should().BeFalse();
        sheet.Cell(2, 2).GetString().Should().Be("=HYPERLINK(\"https://invalid.example\",\"x\")");
    }

    [Fact]
    public async Task WriteAsync_TenThousandRows_ProducesCompleteWorkbook()
    {
        const int rowCount = 10_000;
        var writer = new ClosedXmlExcelReportWriter();
        var spec = new ExcelReportSpec("Occupancy", ["trip_id", "booked"], "occupancy.xlsx");

        await using var report = await writer.WriteAsync(spec, GenerateRows(rowCount));
        using var workbook = new XLWorkbook(report.Content);
        var sheet = workbook.Worksheet("Occupancy");

        sheet.LastRowUsed()!.RowNumber().Should().Be(rowCount + 1);
        sheet.Cell(rowCount + 1, 1).GetString().Should().Be($"trip-{rowCount - 1}");
        sheet.Cell(rowCount + 1, 2).GetValue<long>().Should().Be(rowCount - 1);
    }

    [Fact]
    public async Task WriteAsync_RowSourceFails_DeletesNewTempFile()
    {
        var writer = new ClosedXmlExcelReportWriter();
        var spec = new ExcelReportSpec("Bookings", ["booking_id"], "bookings.xlsx");
        var before = ReportTempFiles();

        var action = () => writer.WriteAsync(spec, FailingRows());

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("row source failed");
        ReportTempFiles().Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task WriteAsync_CancelledEnumeration_DeletesNewTempFile()
    {
        var writer = new ClosedXmlExcelReportWriter();
        var spec = new ExcelReportSpec("Parcels", ["parcel_id"], "parcels.xlsx");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var before = ReportTempFiles();

        var action = () => writer.WriteAsync(spec, GenerateRows(10), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        ReportTempFiles().Should().BeEquivalentTo(before);
    }

    private static async IAsyncEnumerable<ExcelReportRow> Rows(IReadOnlyList<ExcelReportRow> rows)
    {
        foreach (var row in rows)
        {
            yield return row;
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<ExcelReportRow> GenerateRows(int count)
    {
        for (var index = 0; index < count; index++)
        {
            yield return new ExcelReportRow([
                ExcelReportCell.TextValue($"trip-{index}"),
                ExcelReportCell.IntegerValue(index),
            ]);

            if (index % 1_000 == 0)
                await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<ExcelReportRow> FailingRows()
    {
        yield return new ExcelReportRow([ExcelReportCell.TextValue("first")]);
        await Task.Yield();
        throw new InvalidOperationException("row source failed");
    }

    private static string[] ReportTempFiles()
        => Directory.GetFiles(Path.GetTempPath(), "vietride-report-*.xlsx")
            .Order(StringComparer.Ordinal)
            .ToArray();
}
