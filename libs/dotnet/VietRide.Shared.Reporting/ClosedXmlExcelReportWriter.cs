using ClosedXML.Excel;
using VietRide.Shared.Application.Reporting;

namespace VietRide.Shared.Reporting;

public sealed class ClosedXmlExcelReportWriter : IExcelReportWriter
{
    private const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const int HeaderRow = 1;
    private const int FirstDataRow = 2;

    public async Task<ExcelReportStream> WriteAsync(
        ExcelReportSpec spec,
        IAsyncEnumerable<ExcelReportRow> rows,
        CancellationToken cancellationToken = default)
    {
        ValidateSpec(spec);
        var path = Path.Combine(Path.GetTempPath(), $"vietride-report-{Guid.NewGuid():N}.xlsx");
        FileStream? stream = null;
        try
        {
            stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 64 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);

            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.Worksheets.Add(spec.SheetName);
                for (var column = 0; column < spec.Headers.Count; column++)
                {
                    sheet.Cell(HeaderRow, column + 1).Value = spec.Headers[column];
                }

                var header = sheet.Range(HeaderRow, 1, HeaderRow, spec.Headers.Count);
                header.Style.Font.Bold = true;
                header.Style.Fill.BackgroundColor = XLColor.FromHtml("#E2E8F0");
                header.SetAutoFilter();
                sheet.SheetView.FreezeRows(1);

                var rowNumber = FirstDataRow;
                await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (row.Cells.Count != spec.Headers.Count)
                    {
                        throw new InvalidDataException("Report row cell count does not match the report header.");
                    }

                    for (var column = 0; column < row.Cells.Count; column++)
                    {
                        SetCell(sheet.Cell(rowNumber, column + 1), row.Cells[column], spec.CurrencyColumns?.Contains(column) == true);
                    }

                    rowNumber++;
                }

                var lastContentRow = Math.Max(HeaderRow, rowNumber - 1);
                sheet.Columns(1, spec.Headers.Count)
                    .AdjustToContents(HeaderRow, Math.Min(lastContentRow, 1_000));
                workbook.SaveAs(stream);
            }

            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Position = 0;
            return new ExcelReportStream(stream, spec.FileName, ContentType);
        }
        catch
        {
            if (stream is not null)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                TryDelete(path);
            }

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
                cell.Style.DateFormat.Format = "yyyy-mm-dd";
                break;
            case ExcelReportCellType.DateTime:
                cell.Value = value.DateTime ?? DateTime.MinValue;
                cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
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

        if (currency)
        {
            cell.Style.NumberFormat.Format = "#,##0";
        }
    }

    private static void ValidateSpec(ExcelReportSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.SheetName)
            || spec.SheetName.Length > 31
            || spec.Headers.Count == 0
            || spec.Headers.Any(string.IsNullOrWhiteSpace)
            || string.IsNullOrWhiteSpace(spec.FileName))
        {
            throw new ArgumentException("The XLSX report specification is invalid.", nameof(spec));
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup after a failed file creation.
        }
    }
}
