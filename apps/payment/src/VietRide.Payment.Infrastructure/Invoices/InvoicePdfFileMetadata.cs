namespace VietRide.Payment.Infrastructure.Invoices;

public static class InvoicePdfFileMetadata
{
    public static string DownloadFileName(string invoiceNumber)
    {
        if (string.IsNullOrWhiteSpace(invoiceNumber))
            throw new ArgumentException("Invoice number is required.", nameof(invoiceNumber));
        return $"hoa-don-{invoiceNumber}.pdf";
    }

    public static string ContentDisposition(string downloadFileName)
    {
        if (string.IsNullOrWhiteSpace(downloadFileName))
            throw new ArgumentException("Invoice download filename is required.", nameof(downloadFileName));
        return $"attachment; filename*=UTF-8''{Uri.EscapeDataString(downloadFileName)}";
    }
}
