using Google.Apis.Auth.OAuth2;
using StorageObject = Google.Apis.Storage.v1.Data.Object;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Options;
using VietRide.Payment.Application.Abstractions.Services;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.Infrastructure.Invoices;

public sealed class GoogleCloudInvoiceStorage : IInvoiceStorage
{
    private const string PdfContentType = "application/pdf";
    private readonly StorageClient _storageClient;
    private readonly UrlSigner _urlSigner;
    private readonly InvoiceStorageOptions _options;
    private readonly IClock _clock;

    public GoogleCloudInvoiceStorage(
        StorageClient storageClient,
        GoogleCredential credential,
        IOptions<InvoiceStorageOptions> options,
        IClock clock)
    {
        _storageClient = storageClient;
        _urlSigner = UrlSigner.FromCredential(credential);
        _options = options.Value;
        _clock = clock;

        if (string.IsNullOrWhiteSpace(_options.Bucket))
            throw new InvalidOperationException("InvoiceStorage:Bucket must be configured.");
        if (!Uri.TryCreate(_options.StableBaseUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException("InvoiceStorage:StableBaseUrl must be an absolute URL.");
        if (_options.SignedUrlTtlMinutes <= 0 || _options.SignedUrlTtlMinutes > 60)
            throw new InvalidOperationException("InvoiceStorage:SignedUrlTtlMinutes must be between 1 and 60.");
    }

    public async Task<string> UploadPdfAsync(
        Guid operatorId,
        Guid invoiceId,
        string downloadFileName,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        ValidateIds(operatorId, invoiceId);
        if (content.IsEmpty)
            throw new ArgumentException("Invoice PDF content cannot be empty.", nameof(content));
        if (string.IsNullOrWhiteSpace(downloadFileName))
            throw new ArgumentException("Invoice download filename is required.", nameof(downloadFileName));

        var objectPath = BuildObjectPath(operatorId, invoiceId);
        await using var stream = new MemoryStream(content.ToArray(), writable: false);
        await _storageClient.UploadObjectAsync(
            new StorageObject
            {
                Bucket = _options.Bucket,
                Name = objectPath,
                ContentType = PdfContentType,
                ContentDisposition = InvoicePdfFileMetadata.ContentDisposition(downloadFileName),
            },
            stream,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return objectPath;
    }

    public async Task<InvoiceDownloadUrl> CreateDownloadUrlAsync(
        Guid operatorId,
        Guid invoiceId,
        CancellationToken cancellationToken)
    {
        ValidateIds(operatorId, invoiceId);
        cancellationToken.ThrowIfCancellationRequested();

        var ttl = TimeSpan.FromMinutes(_options.SignedUrlTtlMinutes);
        var expiresAt = _clock.UtcNow.Add(ttl);
        var url = await _urlSigner.SignAsync(
            _options.Bucket,
            BuildObjectPath(operatorId, invoiceId),
            ttl,
            HttpMethod.Get,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return new InvoiceDownloadUrl(url, expiresAt);
    }

    internal static string BuildObjectPath(Guid operatorId, Guid invoiceId)
    {
        ValidateIds(operatorId, invoiceId);
        return $"invoices/{operatorId:D}/{invoiceId:D}.pdf";
    }

    private static void ValidateIds(Guid operatorId, Guid invoiceId)
    {
        if (operatorId == Guid.Empty)
            throw new ArgumentException("Operator identifier is required.", nameof(operatorId));
        if (invoiceId == Guid.Empty)
            throw new ArgumentException("Invoice identifier is required.", nameof(invoiceId));
    }

}
