using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VietRide.Payment.Application.Features.Invoices;
using VietRide.Payment.Domain.Enums;
using VietRide.Payment.Infrastructure.Invoices;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.Infrastructure.Jobs;

public sealed class InvoicePdfReconciliationJob
{
    public const string RecurringJobId = "payment.invoice-pdf-reconciliation";

    private readonly PaymentDbContext _db;
    private readonly IInvoiceJobScheduler _jobs;
    private readonly IClock _clock;
    private readonly InvoicePdfOptions _options;

    public InvoicePdfReconciliationJob(
        PaymentDbContext db,
        IInvoiceJobScheduler jobs,
        IClock clock,
        IOptions<InvoicePdfOptions> options)
    {
        _db = db;
        _jobs = jobs;
        _clock = clock;
        _options = options.Value;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var maxAttempts = Math.Clamp(_options.MaxAttempts, 1, 5);
        var staleBefore = now.AddMinutes(-Math.Max(1, _options.StaleAfterMinutes));

        await _db.Invoices
            .Where(invoice => invoice.Status == InvoiceStatus.DRAFT
                && invoice.PdfGenerationStatus == InvoicePdfGenerationStatus.PROCESSING
                && invoice.PdfGenerationStartedAt <= staleBefore)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(invoice => invoice.PdfGenerationStatus, InvoicePdfGenerationStatus.FAILED)
                .SetProperty(
                    invoice => invoice.PdfGenerationNextRetryAt,
                    invoice => invoice.PdfGenerationAttempts < maxAttempts ? now : null)
                .SetProperty(invoice => invoice.PdfGenerationLastError, "PDF_GENERATION_STALE")
                .SetProperty(invoice => invoice.UpdatedAt, now), cancellationToken);

        var dueIds = await _db.Invoices
            .AsNoTracking()
            .Where(invoice => invoice.Status == InvoiceStatus.DRAFT
                && invoice.PdfGenerationStatus == InvoicePdfGenerationStatus.FAILED
                && invoice.PdfGenerationAttempts < maxAttempts
                && invoice.PdfGenerationNextRetryAt <= now)
            .OrderBy(invoice => invoice.PdfGenerationNextRetryAt)
            .Select(invoice => invoice.Id)
            .Take(500)
            .ToArrayAsync(cancellationToken);

        foreach (var invoiceId in dueIds)
        {
            var requeued = await _db.Invoices
                .Where(invoice => invoice.Id == invoiceId
                    && invoice.Status == InvoiceStatus.DRAFT
                    && invoice.PdfGenerationStatus == InvoicePdfGenerationStatus.FAILED
                    && invoice.PdfGenerationAttempts < maxAttempts
                    && invoice.PdfGenerationNextRetryAt <= now)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(invoice => invoice.PdfGenerationStatus, InvoicePdfGenerationStatus.PENDING)
                    .SetProperty(invoice => invoice.PdfGenerationNextRetryAt, (DateTimeOffset?)null)
                    .SetProperty(invoice => invoice.PdfGenerationStartedAt, (DateTimeOffset?)null)
                    .SetProperty(invoice => invoice.UpdatedAt, now), cancellationToken);
            if (requeued == 1)
                _jobs.EnqueuePdfGeneration(invoiceId);
        }

        var pendingIds = await _db.Invoices
            .AsNoTracking()
            .Where(invoice => invoice.Status == InvoiceStatus.DRAFT
                && invoice.PdfGenerationStatus == InvoicePdfGenerationStatus.PENDING
                && invoice.PdfGenerationAttempts < maxAttempts)
            .OrderBy(invoice => invoice.CreatedAt)
            .Select(invoice => invoice.Id)
            .Take(500)
            .ToArrayAsync(cancellationToken);
        foreach (var invoiceId in pendingIds)
            _jobs.EnqueuePdfGeneration(invoiceId);
    }
}
