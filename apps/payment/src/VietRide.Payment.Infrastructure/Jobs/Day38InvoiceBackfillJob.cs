using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Features.Invoices;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Payment.Infrastructure.Invoices;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Infrastructure.Jobs;

public sealed class Day38InvoiceBackfillJob
{
    public const string RecurringJobId = "payment.day38-invoice-backfill";

    private readonly PaymentDbContext _db;
    private readonly IInvoiceNumberCounterRepository _counters;
    private readonly IInvoiceJobScheduler _jobs;
    private readonly InvoiceBackfillOptions _options;
    private readonly ILogger<Day38InvoiceBackfillJob> _logger;

    public Day38InvoiceBackfillJob(
        PaymentDbContext db,
        IInvoiceNumberCounterRepository counters,
        IInvoiceJobScheduler jobs,
        IOptions<InvoiceBackfillOptions> options,
        ILogger<Day38InvoiceBackfillJob> logger)
    {
        _db = db;
        _counters = counters;
        _jobs = jobs;
        _options = options.Value;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return;

        var batchSize = Math.Clamp(_options.MaxBatchSize, 1, 1_000);
        var paymentIds = await _db.Payments
            .AsNoTracking()
            .Where(payment => payment.ReferenceType == PaymentReferenceType.SUBSCRIPTION
                && payment.Status == PaymentStatus.SUCCEEDED
                && payment.SucceededAt != null
                && payment.Context != "{}")
            .Where(payment => !_db.Invoices.Any(invoice => invoice.PaymentId == payment.Id))
            .OrderBy(payment => payment.SucceededAt)
            .Select(payment => payment.Id)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken);

        var created = 0;
        foreach (var paymentId in paymentIds)
        {
            Guid? invoiceId = null;
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                if (await _db.Invoices.AnyAsync(
                    invoice => invoice.PaymentId == paymentId,
                    cancellationToken))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    continue;
                }

                var payment = await _db.Payments
                    .AsNoTracking()
                    .SingleAsync(candidate => candidate.Id == paymentId, cancellationToken);
                var context = SubscriptionPaymentContextCodec.DeserializeTrusted(payment.Context);
                var issuedMonth = InvoiceNumberPeriod.FromInstant(payment.SucceededAt!.Value);
                var sequence = await _counters.NextAsync(issuedMonth, cancellationToken);
                var invoice = Invoice.CreateDraft(
                    $"VR-INV-{issuedMonth}-{sequence:000000}",
                    payment.OperatorId!.Value,
                    context.OperatorSubscriptionId,
                    payment.Id,
                    Money.FromRaw(payment.Amount.Amount),
                    context.PeriodFrom,
                    context.PeriodTo,
                    InvoiceMetadataCodec.Serialize(new InvoiceMetadataV1(
                        1,
                        context.PlanName,
                        context.BillingPeriod,
                        context.BuyerSnapshot)));
                await _db.Invoices.AddAsync(invoice, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                invoiceId = invoice.Id;
                created++;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }

            if (invoiceId.HasValue)
                _jobs.EnqueuePdfGeneration(invoiceId.Value);
        }

        _logger.LogInformation(
            "Day 38 invoice backfill created {InvoiceCount} missing subscription invoice(s).",
            created);
    }
}
