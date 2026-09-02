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
        var spec = Spec("Đặt vé", ["Mã đặt vé", "Tổng tiền"], "bao-cao-dat-ve.xlsx", new HashSet<int> { 1 });

        var report = await writer.WriteAsync(spec, Rows([]));
        var fileStream = report.Content.Should().BeOfType<FileStream>().Subject;
        var tempPath = fileStream.Name;

        report.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        report.FileName.Should().Be("bao-cao-dat-ve.xlsx");
        report.Content.Position.Should().Be(0);
        using (var workbook = new XLWorkbook(report.Content))
        {
            var sheet = workbook.Worksheet("Đặt vé");
            sheet.Cell(1, 1).GetString().Should().Be("Báo cáo kiểm thử");
            sheet.Cell(5, 1).GetString().Should().Be("Mã đặt vé");
            sheet.Cell(5, 2).GetString().Should().Be("Tổng tiền");
            sheet.LastRowUsed()!.RowNumber().Should().Be(5);
        }

        await report.DisposeAsync();
        File.Exists(tempPath).Should().BeFalse();
    }

    [Fact]
    public async Task WriteAsync_TypedUnicodeRow_PreservesCellTypesAndFormats()
    {
        var writer = new ClosedXmlExcelReportWriter();
        var when = new DateTimeOffset(2026, 7, 18, 8, 30, 0, TimeSpan.Zero);
        var spec = Spec(
            "Doanh thu",
            ["Diễn giải", "Số tiền", "Tỷ lệ", "Ngày", "Thời gian", "Hoạt động"],
            "bao-cao-doanh-thu.xlsx",
            new HashSet<int> { 1 },
            new HashSet<int> { 2 });
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
        var sheet = workbook.Worksheet("Doanh thu");

        sheet.Cell(6, 1).GetString().Should().Be("Doanh thu tuyến Thành phố Hồ Chí Minh");
        sheet.Cell(6, 2).GetValue<long>().Should().Be(1_234_000);
        sheet.Cell(6, 2).Style.NumberFormat.Format.Should().Be("#,##0 \"₫\"");
        sheet.Cell(6, 3).GetValue<decimal>().Should().Be(62.5m);
        sheet.Cell(6, 3).Style.NumberFormat.Format.Should().Be("0.00\"%\"");
        sheet.Cell(6, 4).DataType.Should().Be(XLDataType.DateTime);
        sheet.Cell(6, 4).Style.DateFormat.Format.Should().Be("dd/MM/yyyy");
        sheet.Cell(6, 5).GetDateTime().Should().Be(new DateTime(2026, 7, 18, 15, 30, 0));
        sheet.Cell(6, 5).Style.DateFormat.Format.Should().Be("dd/MM/yyyy HH:mm");
        sheet.Cell(6, 6).GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task WriteAsync_BlankAndFormulaLikeText_RemainBlankAndPlainText()
    {
        var writer = new ClosedXmlExcelReportWriter();
        var spec = Spec(
            "Đặt vé",
            ["Thời gian xác nhận", "Diễn giải"],
            "bao-cao-dat-ve.xlsx");
        var row = new ExcelReportRow([
            ExcelReportCell.BlankValue(),
            ExcelReportCell.TextValue("=HYPERLINK(\"https://invalid.example\",\"x\")"),
        ]);

        await using var report = await writer.WriteAsync(spec, Rows([row]));
        using var workbook = new XLWorkbook(report.Content);
        var sheet = workbook.Worksheet("Đặt vé");

        sheet.Cell(6, 1).IsEmpty().Should().BeTrue();
        sheet.Cell(6, 2).HasFormula.Should().BeFalse();
        sheet.Cell(6, 2).GetString().Should().Be("=HYPERLINK(\"https://invalid.example\",\"x\")");
    }

    [Fact]
    public async Task WriteAsync_TenThousandRows_ProducesCompleteWorkbook()
    {
        const int rowCount = 10_000;
        var writer = new ClosedXmlExcelReportWriter();
        var spec = Spec("Tỷ lệ lấp đầy", ["Mã chuyến", "Ghế đã đặt"], "bao-cao-ty-le-lap-day.xlsx");

        await using var report = await writer.WriteAsync(spec, GenerateRows(rowCount));
        using var workbook = new XLWorkbook(report.Content);
        var sheet = workbook.Worksheet("Tỷ lệ lấp đầy");

        sheet.LastRowUsed()!.RowNumber().Should().Be(rowCount + 5);
        sheet.Cell(rowCount + 5, 1).GetString().Should().Be($"trip-{rowCount - 1}");
        sheet.Cell(rowCount + 5, 2).GetValue<long>().Should().Be(rowCount - 1);
    }

    [Fact]
    public async Task WriteAsync_RowSourceFails_DeletesNewTempFile()
    {
        var writer = new ClosedXmlExcelReportWriter();
        var spec = Spec("Đặt vé", ["Mã đặt vé"], "bao-cao-dat-ve.xlsx");
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
        var spec = Spec("Bưu kiện", ["Mã bưu kiện"], "bao-cao-buu-kien.xlsx");
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

    private static ExcelReportSpec Spec(
        string sheetName,
        IReadOnlyList<string> headers,
        string fileName,
        IReadOnlySet<int>? currencyColumns = null,
        IReadOnlySet<int>? percentageColumns = null)
        => new(
            sheetName,
            headers,
            fileName,
            currencyColumns,
            percentageColumns,
            "Báo cáo kiểm thử",
            "18/07/2026 - 18/07/2026",
            new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero));

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
