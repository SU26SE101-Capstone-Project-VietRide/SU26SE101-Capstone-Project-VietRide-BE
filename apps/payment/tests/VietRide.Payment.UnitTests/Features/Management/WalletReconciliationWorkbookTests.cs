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
                Sheet("Summary", "metric", "value"),
                Sheet("Transactions", "id", "amount"),
                Sheet("Allocations", "id", "amount"),
            ]);

        await using var report = await writer.WriteAsync(spec);
        using var workbook = new XLWorkbook(report.Content);

        workbook.Worksheets.Select(item => item.Name)
            .Should().Equal("Summary", "Transactions", "Allocations");
        workbook.Worksheet("Summary").Cell(2, 2).GetValue<long>().Should().Be(125_000);
        workbook.Worksheet("Summary").Cell(2, 2).Style.NumberFormat.Format.Should().Be("#,##0");
    }

    [Fact]
    public async Task Writer_PreservesRequiredOperatorReconciliationSheets()
    {
        var writer = new ClosedXmlFinancialWorkbookWriter();
        var spec = new FinancialWorkbookSpec(
            "operator-reconciliation.xlsx",
            [
                Sheet("Summary", "metric", "value"),
                Sheet("Ledger", "id", "amount"),
                Sheet("Trip Settlements", "id", "amount"),
                Sheet("Wallet Transactions", "id", "amount"),
            ]);

        await using var report = await writer.WriteAsync(spec);
        using var workbook = new XLWorkbook(report.Content);

        workbook.Worksheets.Select(item => item.Name)
            .Should().Equal("Summary", "Ledger", "Trip Settlements", "Wallet Transactions");
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
