using Hangfire;
using VietRide.Payment.Application.Features.Invoices;

namespace VietRide.Payment.Infrastructure.Jobs;

internal sealed class InvoiceJobScheduler : IInvoiceJobScheduler
{
    private readonly IBackgroundJobClient _jobs;

    public InvoiceJobScheduler(IBackgroundJobClient jobs) => _jobs = jobs;

    public void EnqueuePdfGeneration(Guid invoiceId)
        => _jobs.Enqueue<InvoicePdfGenerationJob>(
            job => job.RunAsync(invoiceId, CancellationToken.None));
}
