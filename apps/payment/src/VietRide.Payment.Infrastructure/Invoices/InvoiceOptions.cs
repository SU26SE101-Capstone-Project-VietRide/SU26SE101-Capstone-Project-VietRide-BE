namespace VietRide.Payment.Infrastructure.Invoices;

public sealed class OperatorWebOptions
{
    public const string SectionName = "OperatorWeb";

    public string InvoiceDetailBaseUrl { get; set; } = "https://operator.vietride.app/invoices";
}

public sealed class InvoiceBackfillOptions
{
    public const string SectionName = "InvoiceBackfill";

    public bool Enabled { get; set; } = true;
    public int MaxBatchSize { get; set; } = 100;
}
