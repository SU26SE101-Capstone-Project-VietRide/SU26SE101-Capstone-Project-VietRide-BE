namespace VietRide.Payment.Application.Abstractions.Services;

public interface IInvoiceStorage
{
    Task<string> UploadPdfAsync(
        Guid operatorId,
        Guid invoiceId,
        string downloadFileName,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken);

    Task<InvoiceDownloadUrl> CreateDownloadUrlAsync(
        Guid operatorId,
        Guid invoiceId,
        CancellationToken cancellationToken);
}

public sealed record InvoiceDownloadUrl(string DownloadUrl, DateTimeOffset ExpiresAt);
