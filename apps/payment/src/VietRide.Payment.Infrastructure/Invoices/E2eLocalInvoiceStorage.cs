using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VietRide.Payment.Application.Abstractions.Services;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.Infrastructure.Invoices;

/// <summary>
/// Development-only storage boundary used by the isolated Day 38 acceptance stack.
/// </summary>
public sealed class E2eLocalInvoiceStorage : IInvoiceStorage
{
    private readonly InvoiceStorageOptions _options;
    private readonly IClock _clock;

    public E2eLocalInvoiceStorage(
        IOptions<InvoiceStorageOptions> options,
        IClock clock,
        IHostEnvironment environment)
    {
        if (!environment.IsDevelopment())
            throw new InvalidOperationException("E2E local invoice storage is forbidden outside Development.");

        _options = options.Value;
        _clock = clock;
        if (string.IsNullOrWhiteSpace(_options.LocalRootPath))
            throw new InvalidOperationException("InvoiceStorage:LocalRootPath must be configured for E2E local storage.");
        if (!Uri.TryCreate(_options.StableBaseUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException("InvoiceStorage:StableBaseUrl must be an absolute URL.");
        if (_options.SignedUrlTtlMinutes is <= 0 or > 60)
            throw new InvalidOperationException("InvoiceStorage:SignedUrlTtlMinutes must be between 1 and 60.");
    }

    public async Task<string> UploadPdfAsync(
        Guid operatorId,
        Guid invoiceId,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        Validate(operatorId, invoiceId);
        if (content.IsEmpty)
            throw new ArgumentException("Invoice PDF content cannot be empty.", nameof(content));

        var objectPath = GoogleCloudInvoiceStorage.BuildObjectPath(operatorId, invoiceId);
        var failOncePath = Path.Combine(_options.LocalRootPath, ".fail-next-upload");
        if (File.Exists(failOncePath))
        {
            File.Delete(failOncePath);
            throw new IOException("Injected E2E invoice storage upload failure.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(_options.LocalRootPath, objectPath));
        var rootPath = Path.GetFullPath(_options.LocalRootPath) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPath, StringComparison.Ordinal))
            throw new InvalidOperationException("Invoice object path escaped the E2E storage root.");

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, content.ToArray(), cancellationToken).ConfigureAwait(false);
        return objectPath;
    }

    public Task<InvoiceDownloadUrl> CreateDownloadUrlAsync(
        Guid operatorId,
        Guid invoiceId,
        CancellationToken cancellationToken)
    {
        Validate(operatorId, invoiceId);
        cancellationToken.ThrowIfCancellationRequested();

        var expiresAt = _clock.UtcNow.AddMinutes(_options.SignedUrlTtlMinutes);
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var url = $"{_options.StableBaseUrl.TrimEnd('/')}/day38-e2e-objects/" +
            $"{operatorId:D}/{invoiceId:D}.pdf?expires={expiresAt.ToUnixTimeSeconds()}&nonce={nonce}";
        return Task.FromResult(new InvoiceDownloadUrl(url, expiresAt));
    }

    private static void Validate(Guid operatorId, Guid invoiceId)
    {
        if (operatorId == Guid.Empty || invoiceId == Guid.Empty)
            throw new ArgumentException("Operator and invoice identifiers are required.");
    }
}
