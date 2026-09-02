using ClosedXML.Excel;
using FluentAssertions;
using VietRide.Payment.Application.Features.Management;
using VietRide.Payment.Infrastructure.Management;
using VietRide.Shared.Application.Reporting;

namespace VietRide.Payment.UnitTests.Features.Management;

public sealed class WalletReconciliationWorkbookTests
{
    [Fact]
    public async Task Writer_PreservesRequiredSheetNamesAndCurrencyValues()
    {
        var writer = new ClosedXmlFinancialWorkbookWriter();
        var spec = new FinancialWorkbookSpec(
            "reconciliation.xlsx",
            [
                Sheet("Tổng quan", "Chỉ số", "Giá trị"),
                Sheet("Giao dịch", "Mã", "Số tiền"),
                Sheet("Phân bổ", "Mã", "Số tiền"),
            ],
            "Đối soát ví nền tảng",
            "18/07/2026 - 18/07/2026",
            new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero));

        await using var report = await writer.WriteAsync(spec);
        using var workbook = new XLWorkbook(report.Content);

        workbook.Worksheets.Select(item => item.Name)
            .Should().Equal("Tổng quan", "Giao dịch", "Phân bổ");
        workbook.Worksheet("Tổng quan").Cell(6, 2).GetValue<long>().Should().Be(125_000);
        workbook.Worksheet("Tổng quan").Cell(6, 2).Style.NumberFormat.Format.Should().Be("#,##0 \"₫\"");
        workbook.Worksheet("Tổng quan").Cell(1, 1).GetString().Should().Be("Đối soát ví nền tảng");
        workbook.Worksheet("Giao dịch").Cell(1, 1).GetString().Should().Be("Mã");
    }

    [Fact]
    public async Task Writer_PreservesRequiredOperatorReconciliationSheets()
    {
        var writer = new ClosedXmlFinancialWorkbookWriter();
        var spec = new FinancialWorkbookSpec(
            "operator-reconciliation.xlsx",
            [
                Sheet("Tổng quan", "Chỉ số", "Giá trị"),
                Sheet("Sổ cái", "Mã", "Số tiền"),
                Sheet("Quyết toán chuyến", "Mã", "Số tiền"),
                Sheet("Biến động ví", "Mã", "Số tiền"),
            ],
            "Đối soát ví nhà xe",
            "18/07/2026 - 18/07/2026",
            new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero));

        await using var report = await writer.WriteAsync(spec);
        using var workbook = new XLWorkbook(report.Content);

        workbook.Worksheets.Select(item => item.Name)
            .Should().Equal("Tổng quan", "Sổ cái", "Quyết toán chuyến", "Biến động ví");
    }

    private static FinancialWorkbookSheet Sheet(string name, params string[] headers)
        => new(name, headers, Rows(), new HashSet<int> { 1 });

    private static async IAsyncEnumerable<ExcelReportRow> Rows()
    {
        yield return new ExcelReportRow([
            ExcelReportCell.TextValue("row"),
            ExcelReportCell.IntegerValue(125_000),
        ]);
        await Task.CompletedTask;
    }
}
