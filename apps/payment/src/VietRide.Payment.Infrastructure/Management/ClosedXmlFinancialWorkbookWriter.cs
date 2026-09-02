using ClosedXML.Excel;
using VietRide.Payment.Application.Features.Management;
using VietRide.Shared.Application.Reporting;
using VietRide.Shared.Kernel.Time;

namespace VietRide.Payment.Infrastructure.Management;

public sealed class ClosedXmlFinancialWorkbookWriter : IFinancialWorkbookWriter
{
    private const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public async Task<ExcelReportStream> WriteAsync(
        FinancialWorkbookSpec spec,
        CancellationToken cancellationToken = default)
    {
        Validate(spec);
        var path = Path.Combine(Path.GetTempPath(), $"vietride-financial-{Guid.NewGuid():N}.xlsx");
        FileStream? stream = null;
        try
        {
            stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
            using (var workbook = new XLWorkbook())
            {
                foreach (var sheetSpec in spec.Sheets)
                {
                    var sheet = workbook.Worksheets.Add(sheetSpec.SheetName);
                    var isOverview = string.Equals(sheetSpec.SheetName, "Tổng quan", StringComparison.Ordinal);
                    var headerRow = isOverview ? 5 : 1;
                    var firstDataRow = headerRow + 1;
                    if (isOverview)
                    {
                        sheet.Cell(1, 1).Value = spec.Title;
                        sheet.Range(1, 1, 1, sheetSpec.Headers.Count).Merge();
                        sheet.Cell(1, 1).Style.Font.Bold = true;
                        sheet.Cell(1, 1).Style.Font.FontSize = 16;
                        sheet.Cell(2, 1).Value = "Kỳ báo cáo";
                        sheet.Cell(2, 2).Value = spec.ReportPeriod;
                        sheet.Cell(3, 1).Value = "Thời gian xuất";
                        sheet.Cell(3, 2).Value = BusinessTime.ToLocalDateTime(spec.ExportedAt);
                        sheet.Cell(3, 2).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
                        sheet.Range(2, 1, 3, 1).Style.Font.Bold = true;
                    }
                    for (var column = 0; column < sheetSpec.Headers.Count; column++)
                        sheet.Cell(headerRow, column + 1).Value = sheetSpec.Headers[column];
                    var header = sheet.Range(headerRow, 1, headerRow, sheetSpec.Headers.Count);
                    header.Style.Font.Bold = true;
                    header.Style.Fill.BackgroundColor = XLColor.FromHtml("#E2E8F0");
                    header.SetAutoFilter();
                    sheet.SheetView.FreezeRows(headerRow);

                    var rowNumber = firstDataRow;
                    await foreach (var row in sheetSpec.Rows.WithCancellation(cancellationToken).ConfigureAwait(false))
                    {
                        if (row.Cells.Count != sheetSpec.Headers.Count)
                            throw new InvalidDataException("Financial report row does not match its header.");
                        for (var column = 0; column < row.Cells.Count; column++)
                            SetCell(sheet.Cell(rowNumber, column + 1), row.Cells[column], sheetSpec.CurrencyColumns?.Contains(column) == true);
                        rowNumber++;
                    }

                    sheet.Columns(1, sheetSpec.Headers.Count)
                        .AdjustToContents(1, Math.Min(Math.Max(headerRow, rowNumber - 1), 1_000));
                }

                workbook.SaveAs(stream);
            }

            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Position = 0;
            return new ExcelReportStream(stream, spec.FileName, ContentType);
        }
        catch
        {
            if (stream is not null)
                await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static void SetCell(IXLCell cell, ExcelReportCell value, bool currency)
    {
        switch (value.Type)
        {
            case ExcelReportCellType.Text:
                cell.Value = value.Text ?? string.Empty;
                break;
            case ExcelReportCellType.Integer:
                cell.Value = value.Integer ?? 0L;
                break;
            case ExcelReportCellType.Decimal:
                cell.Value = value.Decimal ?? 0m;
                break;
            case ExcelReportCellType.Date:
                cell.Value = value.Date?.ToDateTime(TimeOnly.MinValue) ?? DateTime.MinValue;
                cell.Style.DateFormat.Format = "dd/MM/yyyy";
                break;
            case ExcelReportCellType.DateTime:
                cell.Value = value.Instant.HasValue
                    ? BusinessTime.ToLocalDateTime(value.Instant.Value)
                    : DateTime.MinValue;
                cell.Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
                break;
            case ExcelReportCellType.Boolean:
                cell.Value = value.Boolean ?? false;
                break;
            case ExcelReportCellType.Blank:
                cell.Clear(XLClearOptions.Contents);
                break;
            default:
                throw new InvalidDataException("Unsupported report cell type.");
        }

        if (currency || value.IsCurrency)
            cell.Style.NumberFormat.Format = "#,##0 \"₫\"";
    }

    private static void Validate(FinancialWorkbookSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.FileName)
            || string.IsNullOrWhiteSpace(spec.Title)
            || string.IsNullOrWhiteSpace(spec.ReportPeriod)
            || spec.Sheets.Count == 0
            || spec.Sheets.Select(item => item.SheetName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != spec.Sheets.Count
            || spec.Sheets.Any(item => string.IsNullOrWhiteSpace(item.SheetName)
                || item.SheetName.Length > 31
                || item.Headers.Count == 0
                || item.Headers.Any(string.IsNullOrWhiteSpace)))
        {
            throw new ArgumentException("The financial workbook specification is invalid.", nameof(spec));
        }
    }
}
