namespace VietRide.Payment.Infrastructure.Invoices;

public sealed class InvoiceStorageOptions
{
    public const string SectionName = "InvoiceStorage";
    public const int DefaultSignedUrlTtlMinutes = 60;

    public string Bucket { get; init; } = string.Empty;
    public string StableBaseUrl { get; init; } = string.Empty;
    public int SignedUrlTtlMinutes { get; init; } = DefaultSignedUrlTtlMinutes;
    public string Provider { get; init; } = "GCS";
    public string LocalRootPath { get; init; } = string.Empty;
}
