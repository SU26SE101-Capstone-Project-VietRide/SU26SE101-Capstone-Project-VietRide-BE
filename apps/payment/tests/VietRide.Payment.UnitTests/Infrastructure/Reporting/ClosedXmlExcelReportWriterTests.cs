using ClosedXML.Excel;
using FluentAssertions;
using VietRide.Shared.Application.Reporting;
using VietRide.Shared.Reporting;

namespace VietRide.Payment.UnitTests.Infrastructure.Reporting;

public sealed class ClosedXmlExcelReportWriterTests
{
    [Fact]
    public async Task WriteAsync_UsesVietnameseMetadataHeaderAndNumberFormats()
    {
        var writer = new ClosedXmlExcelReportWriter();
        var spec = new ExcelReportSpec(
            "Dữ liệu",
            ["Nội dung", "Số tiền", "Thời gian", "Tỷ lệ"],
            "bao-cao.xlsx",
            new HashSet<int> { 1 },
            new HashSet<int> { 3 },
            "Báo cáo thử nghiệm",
            "18/07/2026 - 18/07/2026",
            new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero));

        await using var report = await writer.WriteAsync(spec, Rows());
        using var workbook = new XLWorkbook(report.Content);
        var sheet = workbook.Worksheet("Dữ liệu");

        sheet.Cell(1, 1).GetString().Should().Be("Báo cáo thử nghiệm");
        sheet.Cell(2, 1).GetString().Should().Be("Kỳ báo cáo");
        sheet.Cell(3, 1).GetString().Should().Be("Thời gian xuất");
        sheet.Cell(5, 1).GetString().Should().Be("Nội dung");
        sheet.Cell(6, 1).GetString().Should().Be("Tiếng Việt");
        sheet.Cell(6, 2).DataType.Should().Be(XLDataType.Number);
        sheet.Cell(6, 2).Style.NumberFormat.Format.Should().Be("#,##0 \"₫\"");
        sheet.Cell(6, 3).Style.DateFormat.Format.Should().Be("dd/MM/yyyy HH:mm");
        sheet.Cell(6, 3).GetDateTime().Should().Be(new DateTime(2026, 7, 18, 8, 0, 0));
        sheet.Cell(6, 4).Style.NumberFormat.Format.Should().Be("0.00\"%\"");
        sheet.AutoFilter.Range!.RangeAddress.FirstAddress.RowNumber.Should().Be(5);
    }

    [Fact]
    public async Task WriteAsync_AllowsEmptyDataSet()
    {
        var writer = new ClosedXmlExcelReportWriter();
        var spec = new ExcelReportSpec(
            "Dữ liệu",
            ["Nội dung"],
            "bao-cao-rong.xlsx",
            Title: "Báo cáo rỗng",
            ReportPeriod: "18/07/2026 - 18/07/2026",
            ExportedAt: new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero));

        await using var report = await writer.WriteAsync(spec, EmptyRows());
        using var workbook = new XLWorkbook(report.Content);
        workbook.Worksheet("Dữ liệu").Cell(5, 1).GetString().Should().Be("Nội dung");
    }

    private static async IAsyncEnumerable<ExcelReportRow> Rows()
    {
        yield return new ExcelReportRow([
            ExcelReportCell.TextValue("Tiếng Việt"),
            ExcelReportCell.IntegerValue(125_000),
            ExcelReportCell.DateTimeValue(new DateTimeOffset(2026, 7, 18, 1, 0, 0, TimeSpan.Zero)),
            ExcelReportCell.DecimalValue(66.67m),
        ]);
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<ExcelReportRow> EmptyRows()
    {
        await Task.CompletedTask;
        yield break;
    }
}
