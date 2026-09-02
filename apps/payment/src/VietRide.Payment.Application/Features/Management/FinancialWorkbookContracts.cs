using VietRide.Shared.Application.Reporting;

namespace VietRide.Payment.Application.Features.Management;

public sealed record FinancialWorkbookSheet(
    string SheetName,
    IReadOnlyList<string> Headers,
    IAsyncEnumerable<ExcelReportRow> Rows,
    IReadOnlySet<int>? CurrencyColumns = null);

public sealed record FinancialWorkbookSpec(
    string FileName,
    IReadOnlyList<FinancialWorkbookSheet> Sheets);

public interface IFinancialWorkbookWriter
{
    Task<ExcelReportStream> WriteAsync(
        FinancialWorkbookSpec spec,
        CancellationToken cancellationToken = default);
}
