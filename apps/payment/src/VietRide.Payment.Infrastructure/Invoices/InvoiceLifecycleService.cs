using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using VietRide.Payment.Application.Abstractions.Services;
using VietRide.Payment.Application.Features.Invoices;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.Infrastructure.Invoices;

internal sealed class InvoiceLifecycleService : IInvoiceLifecycleService
{
    private const int DownloadLimitPerMinute = 10;

    private readonly PaymentDbContext _db;
    private readonly IInvoiceStorage _storage;
    private readonly IInvoiceJobScheduler _jobs;
    private readonly IConnectionMultiplexer _redis;
    private readonly IClock _clock;

    public InvoiceLifecycleService(
        PaymentDbContext db,
        IInvoiceStorage storage,
        IInvoiceJobScheduler jobs,
        IConnectionMultiplexer redis,
        IClock clock)
    {
        _db = db;
        _storage = storage;
        _jobs = jobs;
        _redis = redis;
        _clock = clock;
    }

    public async Task<RetryInvoiceResult> RetryAsync(
        Guid invoiceId,
        CancellationToken cancellationToken)
    {
        var affected = await _db.Invoices
            .Where(invoice => invoice.Id == invoiceId
                && invoice.Status == InvoiceStatus.DRAFT
                && invoice.PdfGenerationStatus == InvoicePdfGenerationStatus.FAILED
                && invoice.PdfGenerationAttempts < 5)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(invoice => invoice.PdfGenerationStatus, InvoicePdfGenerationStatus.PENDING)
                .SetProperty(invoice => invoice.PdfGenerationNextRetryAt, (DateTimeOffset?)null)
                .SetProperty(invoice => invoice.PdfGenerationStartedAt, (DateTimeOffset?)null)
                .SetProperty(invoice => invoice.UpdatedAt, _clock.UtcNow), cancellationToken);

        if (affected == 1)
        {
            var attempts = await _db.Invoices
                .Where(invoice => invoice.Id == invoiceId)
                .Select(invoice => invoice.PdfGenerationAttempts)
                .SingleAsync(cancellationToken);
            _jobs.EnqueuePdfGeneration(invoiceId);
            return new RetryInvoiceResult(invoiceId, InvoicePdfGenerationStatus.PENDING.ToString(), attempts);
        }

        var state = await _db.Invoices
            .AsNoTracking()
            .Where(invoice => invoice.Id == invoiceId)
            .Select(invoice => new
            {
                invoice.Status,
                invoice.PdfGenerationStatus,
                invoice.PdfGenerationAttempts,
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new CodedNotFoundException("INVOICE_NOT_FOUND", "Invoice was not found.");

        if (state.PdfGenerationStatus is InvoicePdfGenerationStatus.PENDING or InvoicePdfGenerationStatus.PROCESSING)
        {
            throw new CodedConflictException(
                "INVOICE_RETRY_ALREADY_PENDING",
                "Invoice PDF generation is already pending or processing.");
        }

        throw new CodedConflictException(
            "INVOICE_RETRY_NOT_ALLOWED",
            "Invoice is issued, cancelled, or has exhausted its PDF generation attempts.");
    }

    public async Task<InvoiceDownloadUrl> CreateDownloadAsync(
        Guid invoiceId,
        Guid operatorId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var invoice = await _db.Invoices
            .AsNoTracking()
            .Where(candidate => candidate.Id == invoiceId && candidate.OperatorId == operatorId)
            .Select(candidate => new
            {
                candidate.Status,
                candidate.PdfGenerationStatus,
                candidate.StorageObjectPath,
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new CodedNotFoundException("INVOICE_NOT_FOUND", "Invoice was not found.");

        if (invoice.Status != InvoiceStatus.ISSUED
            || invoice.PdfGenerationStatus != InvoicePdfGenerationStatus.COMPLETED
            || string.IsNullOrWhiteSpace(invoice.StorageObjectPath))
        {
            throw new InvoicePdfUnavailableException();
        }

        var redis = _redis.GetDatabase();
        var key = $"payment:invoice_download:{userId:N}:{invoiceId:N}";
        var count = await redis.StringIncrementAsync(key);
        if (count == 1)
            await redis.KeyExpireAsync(key, TimeSpan.FromMinutes(1));
        if (count > DownloadLimitPerMinute)
        {
            throw new TooManyRequestsException(
                "RATE_LIMIT_EXCEEDED",
                "Invoice download rate limit exceeded.");
        }

        return await _storage.CreateDownloadUrlAsync(
            operatorId,
            invoiceId,
            cancellationToken);
    }
}
