namespace VietRide.Payment.Infrastructure.Invoices;

public sealed class InvoicePdfOptions
{
    public const string SectionName = "InvoicePdf";

    public string Provider { get; init; } = "PDFsharp-MigraDoc";
    public int MaxAttempts { get; init; } = 5;
    public int StaleAfterMinutes { get; init; } = 15;
    public string ReconciliationCron { get; init; } = "*/5 * * * *";
    public string PublisherName { get; init; } = "CÔNG TY VIETRIDE";
    public string PublisherTaxCode { get; init; } = string.Empty;
    public string PublisherAddress { get; init; } = string.Empty;
    public string VatNote { get; init; } = "Giá dịch vụ đã bao gồm thuế giá trị gia tăng theo quy định hiện hành.";
}
