using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Time;

namespace VietRide.Shared.Application.Reporting;

public enum ExcelReportCellType
{
    Text,
    Integer,
    Decimal,
    Date,
    DateTime,
    Boolean,
    Blank,
}

public readonly record struct ExcelReportCell(
    ExcelReportCellType Type,
    string? Text = null,
    long? Integer = null,
    decimal? Decimal = null,
    DateOnly? Date = null,
    DateTimeOffset? Instant = null,
    bool? Boolean = null)
{
    public static ExcelReportCell TextValue(string value) => new(ExcelReportCellType.Text, Text: value);
    public static ExcelReportCell IntegerValue(long value) => new(ExcelReportCellType.Integer, Integer: value);
    public static ExcelReportCell DecimalValue(decimal value) => new(ExcelReportCellType.Decimal, Decimal: value);
    public static ExcelReportCell DateValue(DateOnly value) => new(ExcelReportCellType.Date, Date: value);
    public static ExcelReportCell DateTimeValue(DateTimeOffset value) =>
        new(ExcelReportCellType.DateTime, Instant: value.ToUniversalTime());
    public static ExcelReportCell BooleanValue(bool value) => new(ExcelReportCellType.Boolean, Boolean: value);
    public static ExcelReportCell BlankValue() => new(ExcelReportCellType.Blank);
}

public sealed record ExcelReportSpec(
    string SheetName,
    IReadOnlyList<string> Headers,
    string FileName,
    IReadOnlySet<int>? CurrencyColumns = null);

public sealed record ExcelReportRow(IReadOnlyList<ExcelReportCell> Cells);

public sealed record ExcelReportStream(
    Stream Content,
    string FileName,
    string ContentType) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        await Content.DisposeAsync().ConfigureAwait(false);
    }
}

public interface IExcelReportWriter
{
    Task<ExcelReportStream> WriteAsync(
        ExcelReportSpec spec,
        IAsyncEnumerable<ExcelReportRow> rows,
        CancellationToken cancellationToken = default);
}

public sealed record OperatorReportRange(
    DateOnly FromDate,
    DateOnly ToDate,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc)
{
    public const int DefaultDays = 30;
    public const int MaximumDays = 92;

    public static OperatorReportRange Create(DateOnly? from, DateOnly? to, IClock clock)
    {
        var today = BusinessTime.ToLocalDate(clock.UtcNow);
        var toDate = to ?? today;
        DateOnly fromDate;
        try
        {
            fromDate = from ?? toDate.AddDays(-(DefaultDays - 1));
        }
        catch (ArgumentOutOfRangeException)
        {
            throw InvalidRange();
        }

        var inclusiveDays = toDate.DayNumber - fromDate.DayNumber + 1;
        if (fromDate > toDate || inclusiveDays > MaximumDays || toDate == DateOnly.MaxValue)
            throw InvalidRange();

        var range = BusinessTime.GetUtcRange(fromDate, toDate);
        return new OperatorReportRange(fromDate, toDate, range.FromUtc, range.ToUtcExclusive);
    }

    private static VietRide.Shared.Application.Exceptions.CodedValidationException InvalidRange()
        => new(
            "REPORT_RANGE_INVALID",
            $"Report range must contain 1 to {MaximumDays} Asia/Ho_Chi_Minh calendar days.");

}
