using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VietRide.Payment.Application.Abstractions.Services;
using VietRide.Payment.Application.Features.Invoices;
using VietRide.Payment.Domain.Enums;
using VietRide.Payment.Infrastructure.Invoices;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.Infrastructure.Jobs;

public sealed class InvoicePdfGenerationJob
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly int[] RetryBackoffMinutes = [1, 5, 15, 30];

    private readonly PaymentDbContext _db;
    private readonly IInvoicePdfGenerator _pdfGenerator;
    private readonly IInvoiceStorage _storage;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IClock _clock;
    private readonly InvoicePdfOptions _pdfOptions;
    private readonly InvoiceStorageOptions _storageOptions;
    private readonly OperatorWebOptions _operatorWebOptions;
    private readonly ILogger<InvoicePdfGenerationJob> _logger;

    public InvoicePdfGenerationJob(
        PaymentDbContext db,
        IInvoicePdfGenerator pdfGenerator,
        IInvoiceStorage storage,
        IIntegrationEventOutbox outbox,
        IClock clock,
        IOptions<InvoicePdfOptions> pdfOptions,
        IOptions<InvoiceStorageOptions> storageOptions,
        IOptions<OperatorWebOptions> operatorWebOptions,
        ILogger<InvoicePdfGenerationJob> logger)
    {
        _db = db;
        _pdfGenerator = pdfGenerator;
        _storage = storage;
        _outbox = outbox;
        _clock = clock;
        _pdfOptions = pdfOptions.Value;
        _storageOptions = storageOptions.Value;
        _operatorWebOptions = operatorWebOptions.Value;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var maxAttempts = Math.Clamp(_pdfOptions.MaxAttempts, 1, 5);
        var claimed = await _db.Invoices
            .Where(invoice => invoice.Id == invoiceId
                && invoice.Status == InvoiceStatus.DRAFT
                && invoice.PdfGenerationStatus == InvoicePdfGenerationStatus.PENDING
                && invoice.PdfGenerationAttempts < maxAttempts)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(invoice => invoice.PdfGenerationStatus, InvoicePdfGenerationStatus.PROCESSING)
                .SetProperty(invoice => invoice.PdfGenerationAttempts, invoice => invoice.PdfGenerationAttempts + 1)
                .SetProperty(invoice => invoice.PdfGenerationStartedAt, now)
                .SetProperty(invoice => invoice.PdfGenerationNextRetryAt, (DateTimeOffset?)null)
                .SetProperty(invoice => invoice.PdfGenerationLastError, (string?)null)
                .SetProperty(invoice => invoice.UpdatedAt, now), cancellationToken);
        if (claimed == 0)
            return;

        var invoice = await _db.Invoices
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == invoiceId, cancellationToken);

        try
        {
            var metadata = InvoiceMetadataCodec.Deserialize(invoice.Metadata ?? "{}");
            var contents = await _pdfGenerator.GenerateAsync(
                new InvoicePdfDocument(
                    invoice.InvoiceNumber,
                    now,
                    invoice.PeriodFrom,
                    invoice.PeriodTo,
                    metadata.PlanName,
                    metadata.BillingPeriod,
                    invoice.Amount.Amount,
                    new InvoicePdfBuyer(
                        metadata.BuyerSnapshot.Name,
                        metadata.BuyerSnapshot.BusinessRegistrationNumber,
                        metadata.BuyerSnapshot.TaxCode,
                        metadata.BuyerSnapshot.ContactEmail,
                        metadata.BuyerSnapshot.ContactPhone,
                        metadata.BuyerSnapshot.AddressStreet,
                        metadata.BuyerSnapshot.AddressWard,
                        metadata.BuyerSnapshot.AddressDistrict,
                        metadata.BuyerSnapshot.AddressProvince)),
                cancellationToken);
            if (contents.Length == 0)
                throw new InvalidOperationException("Generated invoice PDF is empty.");

            var objectPath = await _storage.UploadPdfAsync(
                invoice.OperatorId,
                invoice.Id,
                contents,
                cancellationToken);
            await CompleteAsync(invoice, objectPath, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await FailAsync(invoice.Id, invoice.PdfGenerationAttempts, maxAttempts, exception, cancellationToken);
        }
    }

    private async Task CompleteAsync(
        Domain.Entities.Invoice invoice,
        string objectPath,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var now = _clock.UtcNow;
        var downloadApiUrl = BuildUrl(
            _storageOptions.StableBaseUrl,
            $"v1/operator/invoices/{invoice.Id:D}/download");
        var invoiceWebUrl = BuildUrl(
            _operatorWebOptions.InvoiceDetailBaseUrl,
            invoice.Id.ToString("D"));
        var affected = await _db.Invoices
            .Where(candidate => candidate.Id == invoice.Id
                && candidate.Status == InvoiceStatus.DRAFT
                && candidate.PdfGenerationStatus == InvoicePdfGenerationStatus.PROCESSING
                && candidate.PdfGenerationAttempts == invoice.PdfGenerationAttempts)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(candidate => candidate.Status, InvoiceStatus.ISSUED)
                .SetProperty(candidate => candidate.IssuedAt, now)
                .SetProperty(candidate => candidate.PdfUrl, downloadApiUrl)
                .SetProperty(candidate => candidate.StorageObjectPath, objectPath)
                .SetProperty(candidate => candidate.PdfGenerationStatus, InvoicePdfGenerationStatus.COMPLETED)
                .SetProperty(candidate => candidate.PdfGenerationNextRetryAt, (DateTimeOffset?)null)
                .SetProperty(candidate => candidate.PdfGenerationLastError, (string?)null)
                .SetProperty(candidate => candidate.UpdatedAt, now), cancellationToken);
        if (affected == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        var integrationEvent = new InvoiceIssuedIntegrationEvent(
            invoice.Id,
            invoice.InvoiceNumber,
            invoice.OperatorId,
            invoice.Amount.Amount,
            invoiceWebUrl,
            downloadApiUrl);
        await _outbox.EnqueueAsync(
            integrationEvent.EventType,
            JsonSerializer.Serialize(integrationEvent, JsonOptions),
            cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task FailAsync(
        Guid invoiceId,
        int attempt,
        int maxAttempts,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var nextRetryAt = attempt < maxAttempts
            ? now.AddMinutes(RetryBackoffMinutes[Math.Min(attempt - 1, RetryBackoffMinutes.Length - 1)])
            : (DateTimeOffset?)null;
        var errorCode = $"PDF_GENERATION_FAILED:{exception.GetType().Name}";
        await _db.Invoices
            .Where(invoice => invoice.Id == invoiceId
                && invoice.PdfGenerationStatus == InvoicePdfGenerationStatus.PROCESSING
                && invoice.PdfGenerationAttempts == attempt)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(invoice => invoice.PdfGenerationStatus, InvoicePdfGenerationStatus.FAILED)
                .SetProperty(invoice => invoice.PdfGenerationNextRetryAt, nextRetryAt)
                .SetProperty(invoice => invoice.PdfGenerationLastError, errorCode)
                .SetProperty(invoice => invoice.UpdatedAt, now), cancellationToken);

        _logger.LogWarning(
            "Invoice PDF generation failed for invoice {InvoiceId} at attempt {Attempt}; retry scheduled: {RetryScheduled}.",
            invoiceId,
            attempt,
            nextRetryAt.HasValue);
    }

    private static string BuildUrl(string baseUrl, string path)
        => $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
}
